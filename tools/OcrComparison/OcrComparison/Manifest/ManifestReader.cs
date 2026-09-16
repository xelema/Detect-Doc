using System.Text.Json;

namespace OcrComparison.Manifest;

public static class ManifestReader
{
    public static IEnumerable<ManifestEntry> ReadManifest(string manifestPath)
    {
        return File.ReadLines(manifestPath).Select((jsonLine, index) =>
            JsonSerializer.Deserialize<ManifestEntry>(jsonLine)
            ?? throw new InvalidOperationException($"Entrée de manifest illisible : {manifestPath}, ligne {index + 1}."));
    }
}