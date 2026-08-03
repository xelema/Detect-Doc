using OcrComparison.Engines;

if (args.Length == 0)
{
    Console.WriteLine("Usage: OcrComparison <dossier-contenant-des-pdf>");
    return;
}

var engine = new PdfPigNativeTextEngine();
foreach (var pdfFile in Directory.GetFiles(args[0], "*.pdf"))
{
    var text = engine.Extract(pdfFile);
    Console.WriteLine(text);
}