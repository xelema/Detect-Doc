using System.Text;
using System.Text.Json;
using OcrComparison.Benchmark;
using OcrComparison.Engines;
using OcrComparison.Manifest;

if (args.Length != 2)
{
    Console.WriteLine("Usage: OcrComparison <chemin-du-corpus> <chemin-du-manifest>");
    return;
}

EvaluerBenchmark(args[0], args[1]);
return;

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
    var resultats = new BenchmarkRunner(engine, cheminCorpus).Executer(entrees).ToList();
    foreach (var resultat in resultats)
    {
        writer.Ecrire(resultat);
    }
    
    // affichage des moyennes des scores temporaires dans la console
    var moyennesParTypeList = resultats
        .Where(r => r.Statut == Statut.Ok)
        .GroupBy(r => r.TypeAttendu)
        .Select(g => new
        {
            TypeAttendu = g.Key,
            MoyenneLevenshtein = g.Average(x => x.ScoreLevenshtein),
            MoyenneJaccard = g.Average(x => x.ScoreJaccard),
            NombreDocuments = g.Count()
        })
        .ToList();

    var sb = new StringBuilder();

    foreach (var item in moyennesParTypeList)
    {
        sb.AppendLine($"[{item.TypeAttendu}] ({item.NombreDocuments} docs)");
        sb.AppendLine($"  Moyenne Levenshtein : {item.MoyenneLevenshtein:F4}");
        sb.AppendLine($"  Moyenne Jaccard     : {item.MoyenneJaccard:F4}");
        sb.AppendLine();
    }

    if (moyennesParTypeList.Count > 0)
    {
        var macroLevenshtein = moyennesParTypeList.Average(m => m.MoyenneLevenshtein);
        var macroJaccard = moyennesParTypeList.Average(m => m.MoyenneJaccard);

        sb.AppendLine("========================================");
        sb.AppendLine("Moyenne GLOBALE (Macro par type) :");
        sb.AppendLine($"  Levenshtein : {macroLevenshtein:F4}");
        sb.AppendLine($"  Jaccard     : {macroJaccard:F4}");
        sb.AppendLine("========================================");

        File.WriteAllText($"{runId}_score.txt", sb.ToString());
    }
}