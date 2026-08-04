namespace OcrComparison.Engines;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

public class PdfPigNativeTextEngine : ITextExtractionEngine
{
    public string Name => "PdfPig";

    public string Extract(string pdfPath)
    {
        var resultat = "";
        using var document = PdfDocument.Open(pdfPath);
        foreach(var page in document.GetPages())
        {
            var listeMots = page.GetWords().ToList();
            listeMots.ForEach(mot => resultat = string.Concat(resultat, mot.Text, " "));
        }
        return resultat;
    }
}