using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PiiMaskingComparison.Benchmark;
using PiiMaskingComparison.Engines;
using PiiMaskingComparison.Manifest;

if (args.Length is < 1 or > 2)
{
    Console.WriteLine("Usage: PiiMaskingComparison <chemin-du-manifest> [regex|presidio]");
    return;
}

const string split = "dev";
var cheminManifest = args[0];
using var moteur = CreerMoteur(args.Length >= 2 ? args[1] : "regex");

var runId = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{moteur.Name.ToLowerInvariant()}";
var dossierRun = Directory.CreateDirectory(Path.Combine("run", runId)).FullName;

var metadata = RunMetadata.Creer(runId, split, cheminManifest, moteur);
File.WriteAllText(Path.Combine(dossierRun, "run.json"),
    JsonSerializer.Serialize(metadata, JsonRun.OptionsIndentees));

var entrees = ManifestReader.ReadManifest(cheminManifest)
    .Where(e => string.Equals(e.Split, split))
    .ToList();

// Un document dérivé est une dégradation d'image de son parent : sur le texte du manifest, les
// deux portent la même chaîne et ce texte pèse donc double dans les totaux.
var derives = entrees.Count(e => e.DeriveDe is not null && entrees.Any(parent => parent.Id == e.DeriveDe));

var cpuAvant = Process.GetCurrentProcess().TotalProcessorTime;
var resultats = new List<ResultatDocument>();

using (var writer = new ResultWriter(Path.Combine(dossierRun, "resultats.jsonl")))
{
    foreach (var resultat in new BenchmarkRunner(moteur).Executer(entrees))
    {
        writer.Ecrire(resultat);
        resultats.Add(resultat);
    }
}

var cpuMs = (Process.GetCurrentProcess().TotalProcessorTime - cpuAvant).TotalMilliseconds;
File.WriteAllText(Path.Combine(dossierRun, "recap.md"),
    ConstruireRecap(runId, metadata, resultats, cpuMs, derives));

Console.WriteLine($"Run écrit dans {dossierRun}");
return;

static IPiiDetectionEngine CreerMoteur(string nom) => nom.ToLowerInvariant() switch
{
    "regex" => new RegexPiiEngine(),
    "presidio" => new PresidioPiiEngine(),
    _ => throw new ArgumentException($"Moteur inconnu : {nom}. Attendu : regex ou presidio.")
};

static string ConstruireRecap(string runId, RunMetadata metadata, List<ResultatDocument> resultats,
    double cpuMs, int derives)
{
    var mesures = resultats.Where(r => r.Statut == Statut.Ok).ToList();
    var occurrences = mesures.SelectMany(r => r.Occurrences).ToList();
    var detections = mesures
        .SelectMany(r => r.Detections.Select(d => (Document: r.Id, Detection: d)))
        .DistinctBy(d => (d.Document, d.Detection.Start, d.Detection.Length))
        .Select(d => d.Detection)
        .ToList();
    var fauxPositifs = detections.Where(d => d.Fausse).ToList();
    var nonLocalisees = mesures.SelectMany(r => r.ValeursNonLocalisees).ToList();

    var recap = new StringBuilder();
    recap.AppendLine($"# Run `{runId}`");
    recap.AppendLine();
    recap.AppendLine($"- **Date** : {metadata.Date:yyyy-MM-dd HH:mm:ss} UTC");
    recap.AppendLine($"- **Corpus** : split `{metadata.Split}`, {resultats.Count} documents, manifest `{metadata.ManifestSha256[..12]}…`");
    recap.AppendLine($"- **Moteur** : {metadata.Moteur.Nom} {metadata.Moteur.Version}");
    recap.AppendLine();
    recap.AppendLine("Mesuré sur le texte de référence du manifest, pas sur une sortie d'OCR.");
    if (derives > 0)
    {
        recap.AppendLine();
        recap.AppendLine(
            $"Dont {derives} documents dérivés, dégradations d'image dont le texte est celui de leur " +
            "parent : ces textes pèsent double dans les totaux tant que l'entrée mesurée est le manifest.");
    }
    recap.AppendLine();

    recap.AppendLine("## Paramètres");
    recap.AppendLine();
    recap.AppendLine("| Paramètre | Valeur |");
    recap.AppendLine("|---|---|");
    foreach (var (cle, valeur) in metadata.Moteur.Parametres)
    {
        recap.AppendLine($"| {cle} | {valeur} |");
    }
    recap.AppendLine();

    if (occurrences.Count == 0)
    {
        recap.AppendLine("Aucune occurrence attendue localisée dans le corpus, rien à agréger.");
        return recap.ToString();
    }

    // Toutes les catégories sont listées, y compris celles qu'aucune valeur du split n'alimente
    // et celles qu'aucune règle ne vise : un zéro affiché est une information, une ligne absente
    // se lit comme un oubli.
    recap.AppendLine("## Couverture par catégorie");
    recap.AppendLine();
    recap.AppendLine("| Catégorie | Attendues | Couvertes | % couvertes | Manquées | Faux positifs | Non localisées |");
    recap.AppendLine("|---|---|---|---|---|---|---|");
    foreach (var categorie in Enum.GetValues<CategoriePii>())
    {
        var attendues = occurrences.Where(o => o.Categorie == categorie).ToList();
        var couvertes = attendues.Count(o => o.Couverte);
        var pourcentage = attendues.Count == 0 ? "-" : $"{100.0 * couvertes / attendues.Count:F1} %";
        recap.AppendLine(
            $"| {categorie} | {attendues.Count} | {couvertes} | {pourcentage} | {attendues.Count - couvertes} " +
            $"| {fauxPositifs.Count(f => f.Categorie == categorie)} | {nonLocalisees.Count(v => v.Categorie == categorie)} |");
    }
    recap.AppendLine();

    var totalCouvertes = occurrences.Count(o => o.Couverte);
    var docsAvecResiduel = mesures.Count(r => r.Occurrences.Any(o => !o.Couverte));

    recap.AppendLine("## Global");
    recap.AppendLine();
    recap.AppendLine("| Métrique | Valeur |");
    recap.AppendLine("|---|---|");
    recap.AppendLine($"| Occurrences couvertes | {100.0 * totalCouvertes / occurrences.Count:F1} % ({totalCouvertes} / {occurrences.Count}) |");
    recap.AppendLine($"| Détections fausses | {(detections.Count == 0 ? 0 : 100.0 * fauxPositifs.Count / detections.Count):F1} % ({fauxPositifs.Count} / {detections.Count}) |");
    recap.AppendLine($"| Documents avec au moins une PII résiduelle | {docsAvecResiduel} / {mesures.Count} |");
    recap.AppendLine($"| Documents sans texte de référence | {resultats.Count(r => r.Statut == Statut.ReferenceAbsente)} |");
    recap.AppendLine($"| Documents en erreur | {resultats.Count(r => r.Statut == Statut.Erreur)} |");
    recap.AppendLine();

    // Ces valeurs ne comptent ni en couvertes ni en manquées : le texte ne les porte sous aucune
    // forme comparable, le détecteur n'avait donc rien à y trouver. Leur volume est la marge
    // d'incertitude du chiffre de couverture, pas un résultat du moteur.
    recap.AppendLine(
        $"Valeurs du manifest non localisées dans le texte : {nonLocalisees.Count}, hors mesure.");
    recap.AppendLine();

    // Le manifest déclare ce que le générateur a injecté, pas tout ce que le document contient.
    // Sans ce rappel, la colonne des faux positifs se lit comme du bruit du moteur.
    recap.AppendLine(
        "Est compté faux positif ce qui ne recouvre aucune valeur déclarée au manifest. " +
        "L'adresse de l'organisme émetteur ou l'employeur d'un participant sont détectés à juste titre " +
        "sans y figurer : lire le détail par règle avant de conclure sur la précision.");
    recap.AppendLine();

    recap.AppendLine("## Détections par règle");
    recap.AppendLine();
    recap.AppendLine("| Règle | Détections | dont faux positifs |");
    recap.AppendLine("|---|---|---|");
    foreach (var groupe in mesures.SelectMany(r => r.Detections).GroupBy(d => d.Regle).OrderBy(g => g.Key))
    {
        recap.AppendLine($"| {groupe.Key} | {groupe.Count()} | {groupe.Count(d => d.Fausse)} |");
    }
    recap.AppendLine();

    recap.AppendLine("## Coût");
    recap.AppendLine();
    recap.AppendLine("| | |");
    recap.AppendLine("|---|---|");
    recap.AppendLine($"| Documents mesurés | {mesures.Count} |");
    recap.AppendLine($"| Temps de détection total | {mesures.Sum(r => r.DureeMs):F0} ms |");
    recap.AppendLine($"| Temps médian par document | {Mediane(mesures.Select(r => r.DureeMs)):F2} ms |");
    recap.AppendLine($"| Temps du pire document | {mesures.Max(r => r.DureeMs):F1} ms |");
    recap.AppendLine($"| CPU du processus sur le run | {cpuMs:F0} ms |");
    recap.AppendLine($"| Alloué par la détection | {mesures.Sum(r => r.OctetsAlloues) / 1024.0 / 1024.0:F1} Mo |");
    recap.AppendLine($"| Pic mémoire du processus | {Process.GetCurrentProcess().PeakWorkingSet64 / 1024.0 / 1024.0:F0} Mo |");

    return recap.ToString();
}

// Médiane basse : sur un effectif pair, la valeur inférieure du couple central plutôt que leur
// moyenne, pour que le chiffre publié soit toujours celui d'un document réel du run.
static double Mediane(IEnumerable<double> valeurs)
{
    var triees = valeurs.OrderBy(v => v).ToList();
    return triees.Count == 0 ? 0 : triees[(triees.Count - 1) / 2];
}
