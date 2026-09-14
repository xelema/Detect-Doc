using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DetectDoc.Core.Pii;

namespace DetectDoc.Infrastructure.Pii;

public sealed class PresidioPiiDetector : IPiiDetector, IDisposable
{
    private readonly Process _process;
    private readonly Task<string> _standardError;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<Response>> _pending = new();
    private readonly Lock _sendLock = new();
    private int _nextId;

    public PresidioPiiDetector()
    {
        var python = Environment.GetEnvironmentVariable("PII_PYTHON") ?? "python3";
        var script = Path.Combine(AppContext.BaseDirectory, "Pii", "detecteur.py");
        _process = Process.Start(new ProcessStartInfo(python, ["-u", script])
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardInputEncoding = new UTF8Encoding(false)
        }) ?? throw new InvalidOperationException("Le processus Presidio n'a pas démarré.");
        _standardError = _process.StandardError.ReadToEndAsync();

        var startupLine = _process.StandardOutput.ReadLine();
        var startup = startupLine is null ? null : JsonSerializer.Deserialize<Startup>(startupLine);
        if (startup?.Error is not null || startupLine is null)
        {
            throw Fail($"Chargement de Presidio impossible : {startup?.Error ?? "réponse absente"}.");
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

            response = await completion.Task.WaitAsync(cancellationToken);
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

    private InvalidOperationException Fail(string message)
    {
        _process.StandardInput.Close();
        _process.WaitForExit(5000);
        var error = _standardError.IsCompletedSuccessfully ? _standardError.Result.TrimEnd() : string.Empty;
        return new InvalidOperationException($"{message} {error}".TrimEnd());
    }

    public void Dispose()
    {
        if (!_process.HasExited)
        {
            _process.StandardInput.Close();
            _process.WaitForExit(5000);
        }

        _process.Dispose();
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
