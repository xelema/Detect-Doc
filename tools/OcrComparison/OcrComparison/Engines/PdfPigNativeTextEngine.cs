namespace OcrComparison.Engines;
using UglyToad.PdfPig;

public class PdfPigNativeTextEngine : ITextExtractionEngine
{
    public string Name => "PdfPig";

    public string Extract(string pdfPath)
    {
        using var document = PdfDocument.Open(pdfPath);
        var mots = document.GetPages().SelectMany(page => page.GetWords()).Select(mot => mot.Text);
        return string.Join(" ", mots);
    }
}
