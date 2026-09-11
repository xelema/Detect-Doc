using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

if (args.SequenceEqual(["--self-check"]))
{
    Metrics.RunSelfCheck();
    Console.WriteLine("Self-check réussi.");
    return;
}

var options = RunnerOptions.Parse(args);
var entries = ReadManifest(options.ManifestPath)
    .Where(entry => string.Equals(entry.Split, options.Split, StringComparison.Ordinal))
    .ToList();
if (entries.Count == 0)
{
    throw new InvalidOperationException($"Aucun document pour le split '{options.Split}'.");
}

var documents = entries.Select(entry => new
{
    Entry = entry,
    Path = Path.GetFullPath(Path.Combine(options.CorpusPath, entry.FilePath))
}).ToList();
var missing = documents.Where(document => !File.Exists(document.Path)).Select(document => document.Path).ToList();
if (missing.Count > 0)
{
    throw new FileNotFoundException($"Documents absents : {string.Join(", ", missing)}");
}
if (documents.Select(document => Path.GetFileName(document.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != documents.Count)
{
    throw new InvalidOperationException("Les noms de fichiers du lot doivent être uniques.");
}

using var content = new MultipartFormDataContent();
foreach (var document in documents)
{
    var file = new StreamContent(File.OpenRead(document.Path));
    file.Headers.ContentType = new("application/pdf");
    content.Add(file, "files", Path.GetFileName(document.Path));
}
content.Add(new StringContent(JsonSerializer.Serialize(options.Types)), "types");
content.Add(new StringContent(options.Engine), "engine");

using var client = new HttpClient { BaseAddress = new Uri(options.ApiUrl), Timeout = TimeSpan.FromMinutes(10) };
using var response = await client.PostAsync("/api/documents/classify-batch", content);
response.EnsureSuccessStatusCode();
var batchResults = await response.Content.ReadFromJsonAsync<List<ApiBatchResult>>() 
    ?? throw new InvalidDataException("La réponse de lot est vide.");
if (batchResults.Count != documents.Count)
{
    throw new InvalidDataException("Le nombre de résultats ne correspond pas au nombre de documents envoyés.");
}

var resultsByName = batchResults.ToDictionary(result => result.FileName, StringComparer.OrdinalIgnoreCase);
if (resultsByName.Count != batchResults.Count || documents.Any(document => !resultsByName.ContainsKey(Path.GetFileName(document.Path))))
{
    throw new InvalidDataException("Les résultats ne correspondent pas aux fichiers envoyés.");
}

var runId = $"{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss}_classification";
var runDirectory = Directory.CreateDirectory(Path.Combine("run", runId)).FullName;
var metadata = RunMetadata.Create(runId, options);
await File.WriteAllTextAsync(Path.Combine(runDirectory, "run.json"), JsonSerializer.Serialize(metadata, Json.Indented));

var results = documents.Select(document => ResultDocument.Create(document.Entry,
    resultsByName[Path.GetFileName(document.Path)])).ToList();
await using (var writer = File.CreateText(Path.Combine(runDirectory, "resultats.jsonl")))
{
    foreach (var result in results)
    {
        await writer.WriteLineAsync(JsonSerializer.Serialize(result, Json.Options));
    }
}

await File.WriteAllTextAsync(Path.Combine(runDirectory, "recap.md"), Metrics.BuildReport(runId, metadata, results));
await File.WriteAllTextAsync(Path.Combine(runDirectory, "matrice-confusion.csv"), Metrics.BuildConfusionCsv(results, options.Types));
Console.WriteLine($"Run écrit dans {runDirectory}");

static IEnumerable<ManifestEntry> ReadManifest(string path) => File.ReadLines(path).Select((line, index) =>
    JsonSerializer.Deserialize<ManifestEntry>(line)
    ?? throw new InvalidDataException($"Manifest illisible, ligne {index + 1}."));

public sealed record ManifestEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("fichier")] string FilePath,
    [property: JsonPropertyName("type_attendu")] string ExpectedType,
    [property: JsonPropertyName("regime")] string Regime,
    [property: JsonPropertyName("split")] string Split);

public sealed record ApiBatchResult(
    [property: JsonPropertyName("nomFichier")] string FileName,
    [property: JsonPropertyName("resultat")] ApiClassification? Result,
    [property: JsonPropertyName("erreur")] ApiProblem? Error);

public sealed record ApiClassification(string Type, double Confiance, string Moteur, int PagesTraitees,
    long ExtractionMs, long PiiMaskingMs, long ClassificationMs, long TotalMs, string? ExecutionId);

public sealed record ApiProblem(int? Status, string? Title, string? Detail);

public sealed record ResultDocument(string Id, string FileName, string ExpectedType, string Regime, string Split,
    string Status, string? PredictedType, double? Confidence, string? Engine, long? ExtractionMs,
    long? PiiMaskingMs, long? ClassificationMs, long? TotalMs, int? HttpStatus, string? Error)
{
    public static ResultDocument Create(ManifestEntry entry, ApiBatchResult response) => response.Result is { } result
        ? new(entry.Id, response.FileName, entry.ExpectedType, entry.Regime, entry.Split, "ok", result.Type,
            result.Confiance, result.Moteur, result.ExtractionMs, result.PiiMaskingMs, result.ClassificationMs,
            result.TotalMs, null, null)
        : new(entry.Id, response.FileName, entry.ExpectedType, entry.Regime, entry.Split, "error", null, null,
            null, null, null, null, null, response.Error?.Status,
            response.Error?.Detail ?? response.Error?.Title ?? "Erreur sans détail.");
}

public sealed record RunMetadata(string RunId, DateTime Date, string ManifestSha256, string ApiUrl, string Engine,
    string Split, IReadOnlyList<string> Types, MachineInfo Machine)
{
    public static RunMetadata Create(string runId, RunnerOptions options)
    {
        using var stream = File.OpenRead(options.ManifestPath);
        return new(runId, DateTime.UtcNow, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(),
            options.ApiUrl, options.Engine, options.Split, options.Types,
            new(RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription,
                Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? RuntimeInformation.ProcessArchitecture.ToString(),
                Environment.ProcessorCount, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes));
    }
}

public sealed record MachineInfo(string Os, string Dotnet, string Cpu, int Cores, long RamBytes);

public sealed record RunnerOptions(string CorpusPath, string ManifestPath, string ApiUrl, string Engine,
    string Split, IReadOnlyList<string> Types)
{
    private static readonly string[] DefaultTypes = ["carte_visite", "feuille_emar", "offre_emploi", "rib", "rib_sepa", "sepa"];

    public static RunnerOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--") || index + 1 == args.Length || !values.TryAdd(args[index], args[index + 1]))
            {
                throw new ArgumentException("Usage: ClassificationEvaluation --corpus <chemin> --manifest <chemin> [--api <url>] [--engine <auto|pdfpig|ppocr-tiny>] [--types <csv>] [--split <dev|test>].");
            }
        }

        if (!values.TryGetValue("--corpus", out var corpus) || !values.TryGetValue("--manifest", out var manifest))
        {
            throw new ArgumentException("Les options --corpus et --manifest sont requises.");
        }

        var types = values.TryGetValue("--types", out var typeList)
            ? typeList.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : DefaultTypes;
        if (types.Length == 0 || types.Distinct(StringComparer.Ordinal).Count() != types.Length)
        {
            throw new ArgumentException("La liste de types doit être non vide et sans doublon.");
        }

        return new(Path.GetFullPath(corpus), Path.GetFullPath(manifest),
            values.GetValueOrDefault("--api", "http://localhost:5080"), values.GetValueOrDefault("--engine", "auto"),
            values.GetValueOrDefault("--split", "dev"), types);
    }
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    public static readonly JsonSerializerOptions Indented = new(Options) { WriteIndented = true };
}
