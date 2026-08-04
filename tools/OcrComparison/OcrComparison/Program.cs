using F23.StringSimilarity;
using OcrComparison.Engines;
using OcrComparison.Manifest;
using OcrComparison.Scoring;
using Tesseract;

void EvaluateText(string[] strings)
{
    var corpusPath = strings[0];
    var manifestPath = strings[1];
    using var engine = new TesseractOcrEngine();
    var levenshtein = new NormalizedLevenshtein();

    using var writer = new StreamWriter($"result_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_tesseract_1.txt");

    foreach (var manifestEntry in ManifestReader.ReadManifest(manifestPath))
    {
        if (manifestEntry.Regime is "scanne")
        {
            var textReferenceNorm = TexteNormalise.NormaliserPourScore(manifestEntry.TextReference);
            var textExtraitNorm = TexteNormalise.NormaliserPourScore(engine.Extract(Path.Combine(corpusPath, manifestEntry.Fichier)));
            var scoreLevenshtein = 1 - levenshtein.Distance(textReferenceNorm, textExtraitNorm);
            var scoreJaccard = SimilariteMots.Jaccard(textReferenceNorm, textExtraitNorm);
            writer.WriteLine("====================================================================================");
            writer.WriteLine($"{manifestEntry.Id} ({manifestEntry.TypeAttendu}) - Score Levenshtein: {scoreLevenshtein} - Score Jaccard: {scoreJaccard}\n");
            writer.WriteLine($"Texte de référence (normalisé pour score) : \n{textReferenceNorm}\n");
            writer.WriteLine($"Texte extrait (normalisé pour score) : \n{textExtraitNorm}\n");
            writer.WriteLine("====================================================================================");
        }
    }
}

if (args.Length != 2)
{
    Console.WriteLine("Usage: OcrComparison <chemin-du-corpus> <chemin-du-manifest>");
    return;
}

EvaluateText(args);

// using var engine = new TesseractEngine(Path.Combine(AppContext.BaseDirectory, "tessdata"), "fra", EngineMode.Default);
// using var img = Pix.LoadFromFile("test.png");
// using var page = engine.Process(img);
// Console.WriteLine(page.GetText());