using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PiiMaskingComparison.Benchmark;
using PiiMaskingComparison.Engines;
using PiiMaskingComparison.Manifest;

if (args.Length is < 1 or > 2)
{
    Console.WriteLine("Usage: PiiMaskingComparison <chemin-du-manifest> [regex]");
    return;
}

const string split = "dev";
var cheminManifest = args[0];
var moteur = CreerMoteur(args.Length >= 2 ? args[1] : "regex");

var runId = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{moteur.Name.ToLowerInvariant()}";
var dossierRun = Directory.CreateDirectory(Path.Combine("run", runId)).FullName;

var metadata = RunMetadata.Creer(runId, split, cheminManifest, moteur);
File.WriteAllText(Path.Combine(dossierRun, "run.json"),
    JsonSerializer.Serialize(metadata, JsonRun.OptionsIndentees));

var entrees = ManifestReader.ReadManifest(cheminManifest)
    .Where(e => string.Equals(e.Split, split))
    .ToList();

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
File.WriteAllText(Path.Combine(dossierRun, "recap.md"), ConstruireRecap(runId, metadata, resultats, cpuMs));

Console.WriteLine($"Run écrit dans {dossierRun}");
return;

static IPiiDetectionEngine CreerMoteur(string nom) => nom.ToLowerInvariant() switch
{
    "regex" => new RegexPiiEngine(),
    _ => throw new ArgumentException($"Moteur inconnu : {nom}. Attendu : regex.")
};

static string ConstruireRecap(string runId, RunMetadata metadata, List<ResultatDocument> resultats, double cpuMs)
{
    var mesures = resultats.Where(r => r.Statut == Statut.Ok).ToList();
    var occurrences = mesures.SelectMany(r => r.Occurrences).ToList();
    var fauxPositifs = mesures.SelectMany(r => r.FauxPositifs).ToList();
    var nonLocalisees = mesures.SelectMany(r => r.ValeursNonLocalisees).ToList();
    var nbDetections = mesures.Sum(r => r.NbDetections);

    var recap = new StringBuilder();
    recap.AppendLine($"# Run `{runId}`");
    recap.AppendLine();
    recap.AppendLine($"- **Date** : {metadata.Date:yyyy-MM-dd HH:mm:ss} UTC");
    recap.AppendLine($"- **Corpus** : split `{metadata.Split}`, {resultats.Count} documents, manifest `{metadata.ManifestSha256[..12]}…`");
    recap.AppendLine($"- **Moteur** : {metadata.Moteur.Nom} {metadata.Moteur.Version}");
    recap.AppendLine();
    recap.AppendLine("Mesuré sur le texte de référence du manifest, pas sur une sortie d'OCR.");
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

    recap.AppendLine("## Couverture par catégorie");
    recap.AppendLine();
    recap.AppendLine("| Catégorie | Attendues | Couvertes | % couvertes | Manquées | Faux positifs |");
    recap.AppendLine("|---|---|---|---|---|---|");
    foreach (var groupe in occurrences.GroupBy(o => o.Categorie).OrderBy(g => g.Key.ToString()))
    {
        var couvertes = groupe.Count(o => o.Couverte);
        var faux = fauxPositifs.Count(f => f.Categorie == groupe.Key);
        recap.AppendLine(
            $"| {groupe.Key} | {groupe.Count()} | {couvertes} | {100.0 * couvertes / groupe.Count():F1} % | {groupe.Count() - couvertes} | {faux} |");
    }
    recap.AppendLine();

    var totalCouvertes = occurrences.Count(o => o.Couverte);
    var docsAvecResiduel = mesures.Count(r => r.Occurrences.Any(o => !o.Couverte));

    recap.AppendLine("## Global");
    recap.AppendLine();
    recap.AppendLine("| Métrique | Valeur |");
    recap.AppendLine("|---|---|");
    recap.AppendLine($"| Occurrences couvertes | {100.0 * totalCouvertes / occurrences.Count:F1} % ({totalCouvertes} / {occurrences.Count}) |");
    recap.AppendLine($"| Détections fausses | {(nbDetections == 0 ? 0 : 100.0 * fauxPositifs.Count / nbDetections):F1} % ({fauxPositifs.Count} / {nbDetections}) |");
    recap.AppendLine($"| Documents avec au moins une PII résiduelle | {docsAvecResiduel} / {mesures.Count} |");
    recap.AppendLine($"| Documents sans texte de référence | {resultats.Count(r => r.Statut == Statut.ReferenceAbsente)} |");
    recap.AppendLine($"| Documents en erreur | {resultats.Count(r => r.Statut == Statut.Erreur)} |");
    recap.AppendLine();

    // Ces valeurs ne comptent ni en couvertes ni en manquées : le texte ne les porte pas sous la
    // forme déclarée au manifest, le détecteur n'avait donc rien à y trouver. Leur volume est la
    // marge d'incertitude du chiffre de couverture ci-dessus, pas un résultat du moteur.
    recap.AppendLine("## Valeurs du manifest non localisées dans le texte");
    recap.AppendLine();
    recap.AppendLine("| Catégorie | Valeurs |");
    recap.AppendLine("|---|---|");
    foreach (var groupe in nonLocalisees.GroupBy(v => v.Categorie).OrderBy(g => g.Key.ToString()))
    {
        recap.AppendLine($"| {groupe.Key} | {groupe.Count()} |");
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
