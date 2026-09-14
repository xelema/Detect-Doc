using DetectDoc.Core.Pii;

namespace DetectDoc.Infrastructure.Pii;

public sealed class HybridPresidioPiiDetector(
    RegexPiiDetector regex,
    PresidioPiiDetector presidio) : IPiiDetector, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<PiiDetection>> DetectAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        // Le scan regex est un calcul CPU sans état partagé : il n'a besoin d'aucune synchronisation,
        // contrairement à l'appel Presidio qui reste sérialisé derrière le sémaphore ci-dessous.
        var detectionsRegex = await regex.DetectAsync(text, cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return [.. detectionsRegex, .. await presidio.DetectAsync(text, cancellationToken)];
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        presidio.Dispose();
        _gate.Dispose();
    }
}
