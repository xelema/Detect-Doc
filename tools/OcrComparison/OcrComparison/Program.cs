using F23.StringSimilarity;
using OcrComparison.Engines;
using OcrComparison.Manifest;

if (args.Length != 2)
{
    Console.WriteLine("Usage: OcrComparison <chemin-du-corpus> <chemin-du-manifest>");
    return;
}

var corpusPath = args[0];
var manifestPath = args[1];
var engine = new PdfPigNativeTextEngine();
var levenshtein = new NormalizedLevenshtein();

using var writer = new StreamWriter("result.txt");

foreach (var manifestEntry in ManifestReader.ReadManifest(manifestPath))
{
    if (manifestEntry.Id is "cvi-0004" or "dev-0002")
    {
        var textExtrait = engine.Extract(Path.Combine(corpusPath, manifestEntry.Fichier));
        var score = levenshtein.Distance(manifestEntry.TextReference, textExtrait);
        writer.WriteLine("====================================================================================");
        writer.WriteLine($"{manifestEntry.Id} ({manifestEntry.TypeAttendu}) - Score: {score}\n");
        writer.WriteLine($"Texte de référence : \n{manifestEntry.TextReference}\n");
        writer.WriteLine($"Texte extrait : \n{textExtrait}\n");
        writer.WriteLine("====================================================================================");
    }
}