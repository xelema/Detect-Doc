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

const string split = "dev";
var (cheminCorpus, cheminManifest) = (args[0], args[1]);

using var moteur = new TesseractOcrEngine();
var runId = $"ocr-{moteur.Name.ToLowerInvariant()}-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";

var metadata = RunMetadata.Creer(runId, split, cheminManifest, moteur);
File.WriteAllText($"{runId}.json", JsonSerializer.Serialize(metadata, JsonRun.OptionsIndentees));

var entrees = ManifestReader.ReadManifest(cheminManifest).Where(e => e.Split == split);
var resultats = new List<ResultatDocument>();

// Chaque résultat part sur le disque dès qu'il est produit : un run OCR complet dure des
// heures, et une interruption à mi-parcours ne doit pas emporter les documents déjà payés
// en temps de calcul. La liste ne sert qu'aux moyennes, calculées après coup.
using (var writer = new ResultWriter($"{runId}.jsonl"))
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

File.WriteAllText($"{runId}_score.txt", rapport.ToString());
