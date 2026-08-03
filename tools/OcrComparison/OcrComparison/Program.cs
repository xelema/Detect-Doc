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

foreach (var manifestEntry in ManifestReader.ReadManifest(manifestPath))
{
    var text = engine.Extract(Path.Combine(corpusPath, manifestEntry.Fichier));
    Console.WriteLine($"{manifestEntry.Id} ({manifestEntry.TypeAttendu}) : {text}");
}