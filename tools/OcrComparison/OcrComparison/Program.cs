using F23.StringSimilarity;
using OcrComparison.Engines;
using OcrComparison.Manifest;
using OcrComparison.Scoring;

if (args.Length != 2)
{
    Console.WriteLine("Usage: OcrComparison <chemin-du-corpus> <chemin-du-manifest>");
    return;
}

var corpusPath = args[0];
var manifestPath = args[1];
var engine = new PdfPigNativeTextEngine();
var levenshtein = new NormalizedLevenshtein();

using var writer = new StreamWriter($"result_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_get_words.txt");

foreach (var manifestEntry in ManifestReader.ReadManifest(manifestPath))
{
    if (manifestEntry.Id is "cvi-0004" or "dev-0002")
    {
        var textReferenceNorm = TexteNormalise.Normaliser(manifestEntry.TextReference);
        var textExtraitNorm = TexteNormalise.Normaliser(engine.Extract(Path.Combine(corpusPath, manifestEntry.Fichier)));
        var scoreLevenshtein = 1 - levenshtein.Distance(textReferenceNorm, textExtraitNorm);
        var scoreJaccard = SimilariteMots.Jaccard(textReferenceNorm, textExtraitNorm);
        writer.WriteLine("====================================================================================");
        writer.WriteLine($"{manifestEntry.Id} ({manifestEntry.TypeAttendu}) - Score Levenshtein: {scoreLevenshtein} - Score Jaccard: {scoreJaccard}\n");
        writer.WriteLine($"Texte de référence : \n{textReferenceNorm}\n");
        writer.WriteLine($"Texte extrait : \n{textExtraitNorm}\n");
        writer.WriteLine("====================================================================================");
    }
}