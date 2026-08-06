using System.Text;
using System.Text.Json;
using OcrComparison.Benchmark;
using OcrComparison.Engines;
using OcrComparison.Manifest;
using OcrComparison.Preprocessing;

if (args.Length != 2)
{
    Console.WriteLine("Usage: OcrComparison <chemin-du-corpus> <chemin-du-manifest>");
    return;
}

const string split = "dev";
var (cheminCorpus, cheminManifest) = (args[0], args[1]);

var optionsPretraitement = new OptionsPretraitement(Redresser: true);
using var moteur = new TesseractOcrEngine(optionsPretraitement);

var runId = $"ocr-{moteur.Name.ToLowerInvariant()}-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
var dossierRun = Directory.CreateDirectory(Path.Combine("run", runId)).FullName;

var metadata = RunMetadata.Creer(runId, split, cheminManifest, moteur);
File.WriteAllText(Path.Combine(dossierRun, "run.json"),
    JsonSerializer.Serialize(metadata, JsonRun.OptionsIndentees));

var entrees = ManifestReader.ReadManifest(cheminManifest)
    .Where(e => string.Equals(e.Split, split, StringComparison.OrdinalIgnoreCase)
                && ((e.Variante?.AngleDeg ?? 0) != 0
                    || (e.Variante?.RotationPageDeg ?? 0) != 0
                    || (e.DegradationParameters?.Contains("Geometric") ?? false)))
    .ToList();

var resultats = new List<ResultatDocument>();

using (var writer = new ResultWriter(Path.Combine(dossierRun, "resultats.jsonl")))
{
    foreach (var resultat in new BenchmarkRunner(moteur, cheminCorpus).Executer(entrees))
    {
        writer.Ecrire(resultat);
        resultats.Add(resultat);
    }
}

var moyennesParType = resultats
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

if (moyennesParType.Count == 0)
{
    return;
}

var rapport = new StringBuilder();

foreach (var moyenne in moyennesParType)
{
    rapport.AppendLine($"[{moyenne.TypeAttendu}] ({moyenne.NombreDocuments} docs)");
    rapport.AppendLine($"  Moyenne Levenshtein : {moyenne.MoyenneLevenshtein:F4}");
    rapport.AppendLine($"  Moyenne Jaccard     : {moyenne.MoyenneJaccard:F4}");
    rapport.AppendLine();
}

// Macro-moyenne : chaque type documentaire pèse pareil, quel que soit son nombre de documents,
// pour qu'un type surreprésenté au corpus ne dicte pas le score global du moteur.
rapport.AppendLine("========================================");
rapport.AppendLine("Moyenne GLOBALE (Macro par type) :");
rapport.AppendLine($"  Levenshtein : {moyennesParType.Average(m => m.MoyenneLevenshtein):F4}");
rapport.AppendLine($"  Jaccard     : {moyennesParType.Average(m => m.MoyenneJaccard):F4}");
rapport.AppendLine("========================================");

File.WriteAllText(Path.Combine(dossierRun, "score.txt"), rapport.ToString());
