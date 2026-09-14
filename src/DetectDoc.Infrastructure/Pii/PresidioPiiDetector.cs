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

    private readonly Process _process;
    private readonly Task<string> _standardError;
    private readonly TimeSpan _startupTimeout;
    private readonly TimeSpan _detectionTimeout;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<Response>> _pending = new();
    private readonly Lock _sendLock = new();
    private int _nextId;

    public PresidioPiiDetector(TimeSpan? startupTimeout = null, TimeSpan? detectionTimeout = null)
    {
        _startupTimeout = startupTimeout ?? StartupTimeout;
        _detectionTimeout = detectionTimeout ?? DetectionTimeout;
        var python = Environment.GetEnvironmentVariable("PII_PYTHON") ?? "python3";
        var script = Path.Combine(AppContext.BaseDirectory, "Pii", "detecteur.py");
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
            _standardError = process.StandardError.ReadToEndAsync();

            // Lecture bornée : un Python qui pend gèlerait ce constructeur sous le verrou du
            // singleton DI, et avec lui toutes les requêtes PII.
            var startupLine = process.StandardOutput.ReadLineAsync()
                .WaitAsync(_startupTimeout)
                .GetAwaiter()
                .GetResult();
            var startup = startupLine is null ? null : JsonSerializer.Deserialize<Startup>(startupLine);
            if (startup?.Error is not null || startupLine is null)
            {
                throw Fail(process, $"Chargement de Presidio impossible : {startup?.Error ?? "réponse absente"}.");
            }

            _process = process;
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

        _ = Task.Run(DistributeAsync);
    }

    public async Task<IReadOnlyList<PiiDetection>> DetectAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<Response>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;

        Response response;
        try
        {
            lock (_sendLock)
            {
                _process.StandardInput.WriteLine(JsonSerializer.Serialize(new { id, texte = text }));
                _process.StandardInput.Flush();
            }

            // Sans borne propre, un analyze bloqué garderait l'entrée et le sémaphore
            // indéfiniment après l'annulation côté client.
            response = await completion.Task.WaitAsync(_detectionTimeout, cancellationToken);
        }
        finally
        {
            // Retire l'entrée dans tous les cas : réponse reçue, échec d'écriture ou annulation
            // par l'appelant, pour ne jamais laisser une requête abandonnée dans la table.
            _pending.TryRemove(id, out _);
        }

        if (response.Error is not null)
        {
            throw new InvalidOperationException($"Presidio : {response.Error}");
        }

        if (response.Detections.Any(d => d.Start < 0 || d.Length <= 0 || d.Start > text.Length - d.Length))
        {
            throw new InvalidDataException("Presidio a retourné une plage invalide.");
        }

        return [.. response.Detections
            .Select(d => new PiiDetection(
                Enum.Parse<PiiCategory>(d.Category),
                d.Rule,
                PiiDetectionKind.Model,
                d.Start,
                d.Length))];
    }

    private async Task DistributeAsync()
    {
        try
        {
            while (await _process.StandardOutput.ReadLineAsync() is { } line)
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

                if (response is not null && _pending.TryRemove(response.Id, out var completion))
                {
                    completion.SetResult(response);
                }
            }
        }
        finally
        {
            foreach (var completion in _pending.Values)
            {
                completion.TrySetException(new InvalidOperationException("Le processus Presidio s'est interrompu."));
            }
        }
    }

    private InvalidOperationException Fail(Process process, string message)
    {
        process.StandardInput.Close();
        process.WaitForExit(5000);
        var error = _standardError.IsCompletedSuccessfully ? _standardError.Result.TrimEnd() : string.Empty;
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
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("detections")] IReadOnlyList<PythonDetection> Detections,
        [property: JsonPropertyName("erreur")] string? Error);

    private sealed record PythonDetection(
        [property: JsonPropertyName("categorie")] string Category,
        [property: JsonPropertyName("start")] int Start,
        [property: JsonPropertyName("length")] int Length,
        [property: JsonPropertyName("regle")] string Rule);
}
