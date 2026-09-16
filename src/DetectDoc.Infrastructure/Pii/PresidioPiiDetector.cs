using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DetectDoc.Core.Pii;

namespace DetectDoc.Infrastructure.Pii;

public sealed class PresidioPiiDetector : IPiiDetector, IDisposable
{
    // Le handshake laisse le temps à spaCy de charger fr_core_news_md ; une analyse est censée
    // répondre en bien moins. À réévaluer lors de la phase de charge (D54).
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DetectionTimeout = TimeSpan.FromSeconds(30);

    // Un enfant mort est relancé quelques fois puis le détecteur reste en faute : sans borne,
    // un crash systématique bouclerait sur des démarrages coûteux et la santé resterait verte.
    private const int MaxRestarts = 3;
    private static readonly TimeSpan RestartDelay = TimeSpan.FromMilliseconds(500);

    // stderr ne sert qu'à diagnostiquer un démarrage raté : on ne conserve que la fin.
    private const int StderrRetainedLength = 16 * 1024;

    private readonly TimeSpan _startupTimeout;
    private readonly TimeSpan _detectionTimeout;
    private readonly PresidioHealth _health;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private int _nextId;
    private int _restarts;
    private bool _faulted;

    // Génération courante du sous-processus : une relance remplace le processus et sa table de
    // correspondance ensemble, pour qu'une réponse du nouvel enfant ne résolve jamais une
    // requête écrite à l'ancien.
    private Process _process;
    private ConcurrentDictionary<int, TaskCompletionSource<Response>> _pending = new();

    public PresidioPiiDetector(
        TimeSpan? startupTimeout = null,
        TimeSpan? detectionTimeout = null,
        PresidioHealth? health = null)
    {
        _startupTimeout = startupTimeout ?? StartupTimeout;
        _detectionTimeout = detectionTimeout ?? DetectionTimeout;
        _health = health ?? new PresidioHealth();
        try
        {
            _process = StartProcess();
        }
        catch
        {
            _health.Unrecoverable = true;
            throw;
        }

        _ = Task.Run(() => DistributeAsync(_process, _pending));
    }

    public async Task<IReadOnlyList<PiiDetection>> DetectAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<Response>(TaskCreationOptions.RunContinuationsAsynchronously);
        ConcurrentDictionary<int, TaskCompletionSource<Response>>? pending = null;

        Response response;
        try
        {
            await _sendGate.WaitAsync(cancellationToken);
            try
            {
                EnsureAlive();
                pending = _pending;
                pending[id] = completion;
                // L'écriture est bornée par le délai de détection : un enfant qui ne lit plus
                // son entrée ne doit pas retenir indéfiniment le verrou que tout le monde traverse.
                var ligne = JsonSerializer.Serialize(new { id, texte = text });
                await _process.StandardInput.WriteLineAsync(ligne)
                    .WaitAsync(_detectionTimeout, cancellationToken);
                await _process.StandardInput.FlushAsync()
                    .WaitAsync(_detectionTimeout, cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Une ligne tronquée désynchroniserait le protocole : on abandonne l'enfant,
                // l'appel suivant le relance dans la limite du budget de redémarrage.
                KillProcess(_process);
                throw;
            }
            finally
            {
                _sendGate.Release();
            }

            // Sans borne propre, un analyze bloqué garderait l'entrée et le sémaphore
            // indéfiniment après l'annulation côté client.
            response = await completion.Task.WaitAsync(_detectionTimeout, cancellationToken);
        }
        finally
        {
            // Retire l'entrée dans tous les cas : réponse reçue, échec d'écriture ou annulation
            // par l'appelant, pour ne jamais laisser une requête abandonnée dans la table.
            pending?.TryRemove(id, out _);
        }

        if (response.Error is not null)
        {
            throw new InvalidOperationException($"Presidio : {response.Error}");
        }

        var detections = response.Detections
            ?? throw new InvalidDataException("Réponse Presidio sans détections ni erreur.");
        if (detections.Any(d => d.Start < 0 || d.Length <= 0 || d.Start > text.Length - d.Length))
        {
            throw new InvalidDataException("Presidio a retourné une plage invalide.");
        }

        return [.. detections
            .Select(d => new PiiDetection(
                // Une entité connue de Presidio mais pas du contrat reste masquée sous le
                // marqueur générique plutôt que d'interrompre la requête.
                Enum.TryParse<PiiCategory>(d.Category, out var category) ? category : PiiCategory.Unknown,
                d.Rule,
                PiiDetectionKind.Model,
                d.Start,
                d.Length))];
    }

    // Appelée sous _sendGate : un enfant mort est relancé dans la limite du budget, puis le
    // détecteur reste en faute jusqu'au redémarrage de l'hôte.
    private void EnsureAlive()
    {
        if (_faulted)
        {
            throw new InvalidOperationException("Le sous-processus Presidio est tombé et le budget de relances est épuisé.");
        }
        if (!_process.HasExited)
        {
            return;
        }

        if (_restarts >= MaxRestarts)
        {
            _faulted = true;
            _health.Unrecoverable = true;
            throw new InvalidOperationException("Le sous-processus Presidio est tombé et le budget de relances est épuisé.");
        }

        _restarts++;
        Thread.Sleep(RestartDelay);

        var process = StartProcess();
        var previous = _process;
        _process = process;
        var pending = _pending = new ConcurrentDictionary<int, TaskCompletionSource<Response>>();
        _ = Task.Run(() => DistributeAsync(process, pending));
        previous.Dispose();
    }

    private Process StartProcess()
    {
        var python = Environment.GetEnvironmentVariable("PII_PYTHON") ?? "python3";
        var script = Environment.GetEnvironmentVariable("PII_PYTHON_SCRIPT")
            ?? Path.Combine(AppContext.BaseDirectory, "Pii", "detecteur.py");
        Process? process = null;
        try
        {
            process = Process.Start(new ProcessStartInfo(python, ["-u", script])
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardInputEncoding = new UTF8Encoding(false)
            }) ?? throw new InvalidOperationException("Le processus Presidio n'a pas démarré.");

            // Le drain tourne pendant toute la vie de l'enfant : sans lecture concurrente, un
            // stderr bavard finirait par remplir le tube et geler le processus. La rétention
            // reste bornée, seule la fin sert au diagnostic d'un démarrage raté.
            var standardError = new StringBuilder();
            _ = Task.Run(() => DrainStderrAsync(process.StandardError, standardError));

            // Lecture bornée : un Python qui pend gèlerait le démarrage sous le verrou d'envoi.
            var startupLine = process.StandardOutput.ReadLineAsync()
                .WaitAsync(_startupTimeout)
                .GetAwaiter()
                .GetResult();
            var startup = startupLine is null ? null : JsonSerializer.Deserialize<Startup>(startupLine);
            if (startup?.Error is not null || startupLine is null)
            {
                throw Fail(process, standardError, $"Chargement de Presidio impossible : {startup?.Error ?? "réponse absente"}.");
            }

            _health.Unrecoverable = false;
            return process;
        }
        catch (Exception exception)
        {
            // Un échec de démarrage est une indisponibilité (503), pas un 500 : exécutable absent,
            // première ligne stdout illisible (un avertissement natif contourne redirect_stdout)…
            // Le processus éventuellement lancé ne doit pas survivre orphelin.
            KillProcess(process);
            process?.Dispose();
            throw new InvalidOperationException("Le démarrage du détecteur Presidio a échoué.", exception);
        }
    }

    private static async Task DrainStderrAsync(StreamReader stderr, StringBuilder retained)
    {
        try
        {
            var chunk = new char[2048];
            int lu;
            while ((lu = await stderr.ReadAsync(chunk)) > 0)
            {
                lock (retained)
                {
                    retained.Append(chunk, 0, lu);
                    if (retained.Length > StderrRetainedLength)
                    {
                        retained.Remove(0, retained.Length - StderrRetainedLength);
                    }
                }
            }
        }
        catch (Exception)
        {
            // Meilleur effort : ce flux ne sert qu'au diagnostic, jamais au protocole.
        }
    }

    private static async Task DistributeAsync(
        Process process,
        ConcurrentDictionary<int, TaskCompletionSource<Response>> pending)
    {
        try
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                Response? response;
                try
                {
                    response = JsonSerializer.Deserialize<Response>(line);
                }
                catch (JsonException)
                {
                    // Une ligne stdout illisible ne doit pas arrêter la distribution des réponses suivantes.
                    continue;
                }

                // Une réponse sans id (ligne entrante illisible côté Python) n'est pas
                // corrélable : elle est ignorée, la requête émettrice expirera sur son délai.
                if (response?.Id is { } responseId && pending.TryRemove(responseId, out var completion))
                {
                    completion.SetResult(response);
                }
            }
        }
        finally
        {
            foreach (var completion in pending.Values)
            {
                completion.TrySetException(new InvalidOperationException("Le processus Presidio s'est interrompu."));
            }
        }
    }

    private static InvalidOperationException Fail(Process process, StringBuilder standardError, string message)
    {
        process.StandardInput.Close();
        process.WaitForExit(5000);
        string error;
        lock (standardError)
        {
            error = standardError.ToString().TrimEnd();
        }

        return new InvalidOperationException($"{message} {error}".TrimEnd());
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.Close();
                if (!_process.WaitForExit(5000))
                {
                    KillProcess(_process);
                }
            }
        }
        catch (Exception)
        {
            // La fermeture reste du meilleur effort : le processus peut se terminer entre les
            // contrôles, et un Dispose qui lève compliquerait l'arrêt de l'hôte.
        }

        _process.Dispose();
        _sendGate.Dispose();
    }

    private static void KillProcess(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // Meilleur effort : l'appelant est déjà en train d'échouer ou de s'arrêter.
        }
    }

    private sealed record Startup([property: JsonPropertyName("erreur")] string? Error);

    private sealed record Response(
        [property: JsonPropertyName("id")] int? Id,
        [property: JsonPropertyName("detections")] IReadOnlyList<PythonDetection>? Detections,
        [property: JsonPropertyName("erreur")] string? Error);

    private sealed record PythonDetection(
        [property: JsonPropertyName("categorie")] string Category,
        [property: JsonPropertyName("start")] int Start,
        [property: JsonPropertyName("length")] int Length,
        [property: JsonPropertyName("regle")] string Rule);
}

// État santé partagé entre le détecteur et /health : lire l'état en résolvant
// PresidioPiiDetector lancerait le sous-processus.
public sealed class PresidioHealth
{
    public bool Unrecoverable { get; set; }
}
