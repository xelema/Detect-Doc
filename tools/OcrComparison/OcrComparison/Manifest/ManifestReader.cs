using System.Text.Json;

namespace OcrComparison.Manifest;

public static class ManifestReader
{
    public static IEnumerable<ManifestEntry> ReadManifest(string manifestPath)
    {
        return File.ReadLines(manifestPath).Select(jsonLine => 
            JsonSerializer.Deserialize<ManifestEntry>(jsonLine) ?? throw new InvalidOperationException());
    }
}