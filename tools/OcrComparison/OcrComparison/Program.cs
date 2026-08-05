using System.Globalization;
using System.Text.Json;
using F23.StringSimilarity;
using OcrComparison.Benchmark;
using OcrComparison.Engines;
using OcrComparison.Manifest;
using OcrComparison.Scoring;


if (args.Length != 2)
{
    Console.WriteLine("Usage: OcrComparison <chemin-du-corpus> <chemin-du-manifest>");
    return;
}

EvaluerBenchmark(args[0], args[1]);

void EvaluerBenchmark(string cheminCorpus, string cheminManifest)
{
    const string split = "dev";
    var engine = new TesseractOcrEngine();
    var runId = $"ocr-{engine.Name.ToLowerInvariant()}-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
    
    var entrees = ManifestReader.ReadManifest(cheminManifest).Where(e => e.Split == split);
    
    var metadata = RunMetadata.Creer(runId, split, cheminManifest, engine);
    File.WriteAllText($"{runId}.json", JsonSerializer.Serialize(metadata, 
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, 
            WriteIndented = true }));
    using var writer = new ResultWriter($"{runId}.jsonl");
    foreach (var resultat in new BenchmarkRunner(engine, cheminCorpus).Executer(entrees))
    {
        writer.Ecrire(resultat);
    }
}