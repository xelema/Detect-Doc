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

var optionsPretraitement = new OptionsPretraitement(Redresser: true, DetourerAvantOsd: true, Deskew: true);
using var moteur = new TesseractOcrEngine(optionsPretraitement);

var titre = ConstruireTitre(moteur.Parametres);
var runId = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{moteur.Name.ToLowerInvariant()}_{titre}";
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

File.WriteAllText(Path.Combine(dossierRun, "score.md"), ConstruireRapport(runId, metadata, resultats));
return;

// Ne garde que les réglages actifs (booléens à true) : un run "brut" (rien activé) donne
// simplement "brut", un run avec tout activé "redressement-detourage_avant_osd-deskew". Les
// seuils numériques n'y figurent pas, ils restent dans le tableau de paramètres de score.md.
static string ConstruireTitre(IReadOnlyDictionary<string, object> parametres)
{
    var actifs = parametres.Where(p => p.Value is true).Select(p => p.Key);
    var titre = string.Join('-', actifs);
    return titre.Length == 0 ? "brut" : titre;
}

static string ConstruireRapport(string runId, RunMetadata metadata, List<ResultatDocument> resultats)
{
    var rapport = new StringBuilder();
    rapport.AppendLine($"# Run `{runId}`");
    rapport.AppendLine();
    rapport.AppendLine($"- **Date** : {metadata.Date:yyyy-MM-dd HH:mm:ss} UTC");
    rapport.AppendLine($"- **Corpus** : split `{metadata.Split}`, {resultats.Count} documents, manifest `{metadata.ManifestSha256[..12]}…`");
    rapport.AppendLine($"- **Moteur** : {metadata.Ocr.Nom} {metadata.Ocr.Version}");
    rapport.AppendLine();

    rapport.AppendLine("## Paramètres");
    rapport.AppendLine();
    rapport.AppendLine("| Paramètre | Valeur |");
    rapport.AppendLine("|---|---|");
    foreach (var (cle, valeur) in metadata.Ocr.Parametres)
    {
        rapport.AppendLine($"| {cle} | {valeur} |");
    }
    rapport.AppendLine();

    var parType = resultats
        .Where(r => r.Statut == Statut.Ok)
        .GroupBy(r => r.TypeAttendu)
        .Select(g => new
        {
            TypeAttendu = g.Key,
            N = g.Count(),
            MoyenneJaccard = g.Average(r => r.ScoreJaccard),
            PireCas = g.Min(r => r.ScoreJaccard),
            MoyenneLevenshtein = g.Average(r => r.ScoreLevenshtein),
            Replis = g.Count(r => r.RepliDeclenche)
        })
        .OrderBy(t => t.TypeAttendu)
        .ToList();

    if (parType.Count == 0)
    {
        rapport.AppendLine("Aucun document au statut `ok`, rien à agréger.");
        return rapport.ToString();
    }

    rapport.AppendLine("## Scores par type");
    rapport.AppendLine();
    rapport.AppendLine("| Type | n | Jaccard moyen | Pire Jaccard | Levenshtein moyen | Repli 4 orientations |");
    rapport.AppendLine("|---|---|---|---|---|---|");
    foreach (var t in parType)
    {
        rapport.AppendLine(
            $"| {t.TypeAttendu} | {t.N} | {t.MoyenneJaccard:F4} | {t.PireCas:F4} | {t.MoyenneLevenshtein:F4} | {t.Replis} |");
    }
    rapport.AppendLine();

    // Macro-moyenne : chaque type pèse pareil, quel que soit son effectif, pour qu'un type
    // surreprésenté au corpus ne dicte pas le score global du moteur.
    rapport.AppendLine("## Moyenne globale");
    rapport.AppendLine();
    rapport.AppendLine("| Métrique | Valeur |");
    rapport.AppendLine("|---|---|");
    rapport.AppendLine($"| Jaccard | {parType.Average(t => t.MoyenneJaccard):F4} |");
    rapport.AppendLine($"| Levenshtein | {parType.Average(t => t.MoyenneLevenshtein):F4} |");
    rapport.AppendLine();

    var durees = resultats.Select(r => r.DureeMs).OrderBy(d => d).ToList();
    var dureeMediane = durees.Count == 0 ? 0 : durees[durees.Count / 2];

    rapport.AppendLine("## Coût");
    rapport.AppendLine();
    rapport.AppendLine("| | |");
    rapport.AppendLine("|---|---|");
    rapport.AppendLine($"| Documents mesurés | {resultats.Count} |");
    rapport.AppendLine($"| Temps total | {resultats.Sum(r => r.DureeMs) / 1000:F1} s |");
    rapport.AppendLine($"| Temps médian par document | {dureeMediane:F0} ms |");
    rapport.AppendLine($"| Pic mémoire (max sur le run) | {resultats.Max(r => r.PicMemoireOctets) / 1024 / 1024} Mo |");

    return rapport.ToString();
}
