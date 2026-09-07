using System.Text;
using System.Text.Json;
using OcrComparison.Benchmark;
using OcrComparison.Engines;
using OcrComparison.Manifest;
using OcrComparison.Preprocessing;

if (args.Length is < 2 or > 3)
{
    Console.WriteLine(
        "Usage: OcrComparison <chemin-du-corpus> <chemin-du-manifest> " +
        "[tesseract|pdfpig|ppocr-tiny|ppocr-small|ppocr-medium]");
    return;
}

const string split = "dev";
var (cheminCorpus, cheminManifest) = (args[0], args[1]);

using var moteur = CreerMoteur(args.Length == 3 ? args[2] : "tesseract");

var titre = ConstruireTitre(moteur.Parametres);
var runId = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{moteur.Name.ToLowerInvariant()}_{titre}";
var dossierRun = Directory.CreateDirectory(Path.Combine("run", runId)).FullName;

var metadata = RunMetadata.Creer(runId, split, cheminManifest, moteur);
File.WriteAllText(Path.Combine(dossierRun, "run.json"),
    JsonSerializer.Serialize(metadata, JsonRun.OptionsIndentees));

var entrees = ManifestReader.ReadManifest(cheminManifest)
    .Where(e => string.Equals(e.Split, split))
    .ToList();

var resultats = new List<ResultatDocument>();
var cheminResultats = Path.Combine(dossierRun, "resultats.jsonl");

using (var writer = new ResultWriter(cheminResultats))
{
    foreach (var resultat in new BenchmarkRunner(moteur, cheminCorpus).Executer(entrees))
    {
        writer.Ecrire(resultat);

        // Les textes intégraux partent au jsonl et n'y reviennent pas : le rapport n'agrège que
        // des compteurs et des scores. Les garder en mémoire ferait grossir le working set à
        // chaque document, c'est-à-dire exactement la mesure que le run cherche à relever.
        resultats.Add(resultat with { TexteReference = null, TexteExtrait = null });
    }
}

File.WriteAllText(Path.Combine(dossierRun, "score.md"), ConstruireRapport(runId, metadata, resultats));

// Relu depuis le jsonl plutôt que gardé en mémoire : les textes intégraux ne doivent pas séjourner
// dans le processus pendant que le run relève ses pics mémoire.
using (var flux = File.Create(Path.Combine(dossierRun, "textes.json")))
{
    var textes = File.ReadLines(cheminResultats)
        .Select(ligne => JsonSerializer.Deserialize<ResultatDocument>(ligne, JsonRun.Options)!)
        .Select(r => new
        {
            r.Id,
            r.ScoreJaccard,
            TexteReference = DecouperEnLignes(r.TexteReference),
            TexteExtrait = DecouperEnLignes(r.TexteExtrait)
        });

    JsonSerializer.Serialize(flux, textes, JsonRun.OptionsIndentees);
}

return;

static string[] DecouperEnLignes(string? texte) =>
    (texte ?? string.Empty)
        .Split('\n')
        .Select(ligne => ligne.Trim())
        .Where(ligne => ligne.Length > 0)
        .ToArray();

static ITextExtractionEngine CreerMoteur(string nom) => nom.ToLowerInvariant() switch
{
    "tesseract" => new TesseractOcrEngine(
        new OptionsPretraitement(Redresser: true, DetourerAvantOsd: true, Deskew: true)),
    "pdfpig" => new PdfPigNativeTextEngine(),
    "ppocr-tiny" => new PpOcrEngine("tiny"),
    "ppocr-small" => new PpOcrEngine("small"),
    "ppocr-medium" => new PpOcrEngine("medium"),
    _ => throw new ArgumentException(
        $"Moteur inconnu : {nom}. Attendu : tesseract, pdfpig, ppocr-tiny, ppocr-small ou ppocr-medium.")
};

// Ne garde que les réglages actifs (booléens à true) : un run "brut" (rien activé) donne
// simplement "brut", un run avec tout activé "redressement-detourage_avant_osd-deskew". Les
// seuils numériques n'y figurent pas, ils restent dans le tableau de paramètres de score.md.
static string ConstruireTitre(IReadOnlyDictionary<string, object> parametres)
{
    var actifs = parametres.Where(p => p.Value is true).Select(p => p.Key);
    var titre = string.Join('-', actifs);
    // titre += "-1thread";
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

    const double Mo = 1024 * 1024;

    rapport.AppendLine("## Coût");
    rapport.AppendLine();
    rapport.AppendLine("| | |");
    rapport.AppendLine("|---|---|");
    rapport.AppendLine($"| Documents mesurés | {resultats.Count} |");
    rapport.AppendLine($"| Temps total | {resultats.Sum(r => r.DureeMs) / 1000:F1} s |");
    rapport.AppendLine($"| Temps médian par document | {Mediane(resultats.Select(r => r.DureeMs)):F0} ms |");
    rapport.AppendLine($"| CPU total | {resultats.Sum(r => r.CpuMs) / 1000:F1} s |");
    rapport.AppendLine($"| CPU médian par document | {Mediane(resultats.Select(r => r.CpuMs)):F0} ms |");
    if (resultats.Any(r => r.DureeRenduPdfMs.HasValue))
    {
        rapport.AppendLine($"| Rendu PDF vers PNG | {resultats.Sum(r => r.DureeRenduPdfMs ?? 0) / 1000:F1} s |");
        rapport.AppendLine($"| Décodage PNG | {resultats.Sum(r => r.DureeDecodagePngMs ?? 0) / 1000:F1} s |");
        rapport.AppendLine($"| Détection OCR | {resultats.Sum(r => r.DureeDetectMs ?? 0) / 1000:F1} s |");
    }
    rapport.AppendLine(
        $"| Pic mémoire médian par document | {Mediane(resultats.Select(r => r.PicMemoireOctets / Mo)):F0} Mo |");
    rapport.AppendLine(
        $"| Pic mémoire, pire document | {resultats.Max(r => r.PicMemoireOctets) / Mo:F0} Mo |");

    // Ce que le document a fait monter à lui seul, le pic absolu portant aussi tout ce que le
    // processus occupait déjà. C'est la part comparable d'un moteur à l'autre à conditions égales.
    rapport.AppendLine(
        $"| Mémoire propre au document, pire cas | {resultats.Max(r => r.PicMemoireOctets - r.MemoireAvantOctets) / Mo:F0} Mo |");

    return rapport.ToString();
}

// Médiane basse : sur un effectif pair, la valeur inférieure du couple central plutôt que leur
// moyenne, pour que le chiffre publié soit toujours celui d'un document réel du run.
static double Mediane(IEnumerable<double> valeurs)
{
    var triees = valeurs.OrderBy(v => v).ToList();
    return triees.Count == 0 ? 0 : triees[(triees.Count - 1) / 2];
}
