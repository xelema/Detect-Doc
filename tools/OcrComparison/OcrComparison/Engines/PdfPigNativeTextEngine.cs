namespace OcrComparison.Engines;
using UglyToad.PdfPig;

public class PdfPigNativeTextEngine : ITextExtractionEngine
{
    public string Name => "PdfPigNativeTextEngine";

    public string Extract(string pdfPath)
    {
        using var document = PdfDocument.Open(pdfPath);
        return string.Concat(document.GetPages().Select(p => p.Text));
    }
    
}