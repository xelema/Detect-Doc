using System.Globalization;
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
// Evaluer(args[0], args[1]);

void EvaluerBenchmark(string cheminCorpus, string cheminManifest)
{
    using var engine = new TesseractOcrEngine();
    var entrees = ManifestReader.ReadManifest(cheminManifest).Where(e => e.Split == "dev");
    using var writer = new ResultWriter($"run_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{engine.Name}.jsonl");
    foreach (var resultat in new BenchmarkRunner(engine, cheminCorpus).Executer(entrees))
    {
        writer.Ecrire(resultat);
    }
}

void Evaluer(string cheminCorpus, string cheminManifest)
{
    using var engine = new TesseractOcrEngine();
    var levenshtein = new NormalizedLevenshtein();

    using var writer = new StreamWriter($"result_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{engine.Name}.txt");

    foreach (var manifestEntry in ManifestReader.ReadManifest(cheminManifest))
    {
        if ((manifestEntry.Regime != "scanne") || (manifestEntry.Split != "dev"))
        {
            continue;
        }

        // Un manifest produit avant l'ajout de texte_reference n'a aucune vérité terrain à
        // opposer à l'extraction : on le signale et on passe, plutôt que de laisser la
        // normalisation lever une exception opaque au milieu du parcours du corpus.
        if (string.IsNullOrEmpty(manifestEntry.TextReference))
        {
            writer.WriteLine($"{manifestEntry.Id} ({manifestEntry.TypeAttendu}) : ignoré, texte_reference absent du manifest.");
            continue;
        }

        var textReferenceNorm = TexteNormalise.NormaliserPourScore(manifestEntry.TextReference);
        var textExtraitNorm = TexteNormalise.NormaliserPourScore(engine.Extract(Path.Combine(cheminCorpus, manifestEntry.Fichier)));
        var scoreLevenshtein = 1 - levenshtein.Distance(textReferenceNorm, textExtraitNorm);
        var scoreJaccard = SimilariteMots.Jaccard(textReferenceNorm, textExtraitNorm);
        writer.WriteLine("====================================================================================");
        writer.WriteLine($"{manifestEntry.Id} ({manifestEntry.TypeAttendu}) - Score Levenshtein: {Score(scoreLevenshtein)} - Score Jaccard: {Score(scoreJaccard)}\n");
        writer.WriteLine($"Texte de référence (normalisé pour score) : \n{textReferenceNorm}\n");
        writer.WriteLine($"Texte extrait (normalisé pour score) : \n{textExtraitNorm}\n");
        writer.WriteLine("====================================================================================");
    }
}

// Permet d'afficher les scores toujours de la même manière (F4 = 4 décimales fixes)
string Score(double valeur) => valeur.ToString("F4", CultureInfo.InvariantCulture);


