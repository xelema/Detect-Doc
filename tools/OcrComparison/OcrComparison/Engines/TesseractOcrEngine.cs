using System.Text;
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
        var resultat = new StringBuilder();
        var documentBytes = File.ReadAllBytes(pdfPath);
        // Chaque page passe par un bitmap puis un PNG en mémoire : Tesseract ne lit pas le PDF,
        // et Pix n'accepte qu'une image encodée. Bitmap et PNG portent tous deux de la mémoire
        // non gérée, d'où le double using : sans lui, un corpus entier de pages rendues en 300
        // DPI s'accumule jusqu'au prochain passage du GC.
        foreach (var imagePage in Conversion.ToImages(documentBytes))
        {
            using (imagePage)
            {
                using var pngPage = imagePage.Encode(SKEncodedImageFormat.Png, 100);
                using var img = Pix.LoadFromMemory(pngPage.ToArray());

                using var page = _moteur.Process(img);
                resultat.Append(page.GetText()).Append(' ');
            }
        }
        return resultat.ToString();
    }

    public void Dispose() => _moteur.Dispose();
}
