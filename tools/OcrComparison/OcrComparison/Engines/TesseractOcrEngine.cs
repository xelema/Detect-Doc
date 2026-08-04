using PDFtoImage;
using SkiaSharp;
using Tesseract;

namespace OcrComparison.Engines;

public class TesseractOcrEngine : ITextExtractionEngine, IDisposable
{
    private readonly TesseractEngine _moteur = new(Path.Combine(AppContext.BaseDirectory, "tessdata"),
        "fra", EngineMode.Default);

    public string Name => "Tesseract";
    
    public string Extract(string pdfPath)
    {
        var resultat = "";
        var documentBytes = File.ReadAllBytes(pdfPath);
        foreach (var pageImageStream in Conversion.ToImages(documentBytes))
        {
            using (pageImageStream)
            {
                var pageOctetPng = pageImageStream.Encode(SKEncodedImageFormat.Png, 100).ToArray();
                using var img = Pix.LoadFromMemory(pageOctetPng);
                
                using var page = _moteur.Process(img);
                resultat = string.Concat(resultat, page.GetText(), " ");
            }
        }
        return resultat;
    }
    
    public void Dispose() => _moteur.Dispose();
}