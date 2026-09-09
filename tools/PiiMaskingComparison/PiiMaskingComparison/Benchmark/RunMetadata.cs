using System.Runtime.InteropServices;
using System.Security.Cryptography;
using PiiMaskingComparison.Engines;

namespace PiiMaskingComparison.Benchmark;

public sealed record RunMetadata
{
    public required string RunId { get; init; }
    public required DateTime Date { get; init; }
    public required string ManifestSha256 { get; init; }
    public required string Split { get; init; }
    public required MoteurInfo Moteur { get; init; }
    public required MachineInfo Machine { get; init; }

    public static RunMetadata Creer(string runId, string split, string cheminManifest, IPiiDetectionEngine moteur)
    {
        return new RunMetadata
        {
            RunId = runId,
            Date = DateTime.UtcNow,
            ManifestSha256 = CalculerSha256(cheminManifest),
            Split = split,
            Moteur = new MoteurInfo
            {
                Nom = moteur.Name,
                Version = moteur.Version,
                Parametres = moteur.Parametres
            },
            Machine = new MachineInfo
            {
                Os = RuntimeInformation.OSDescription,
                Dotnet = RuntimeInformation.FrameworkDescription,
                Cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? RuntimeInformation.ProcessArchitecture.ToString(),
                Coeurs = Environment.ProcessorCount,
                RamOctets = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes
            }
        };
    }

    private static string CalculerSha256(string cheminManifest)
    {
        using var flux = File.OpenRead(cheminManifest);
        return Convert.ToHexString(SHA256.HashData(flux)).ToLowerInvariant();
    }
}

public sealed record MoteurInfo
{
    public required string Nom { get; init; }
    public required string Version { get; init; }
    public required IReadOnlyDictionary<string, object> Parametres { get; init; }
}

public sealed record MachineInfo
{
    public required string Os { get; init; }
    public required string Dotnet { get; init; }
    public required string Cpu { get; init; }
    public required int Coeurs { get; init; }
    public required long RamOctets { get; init; }
}
