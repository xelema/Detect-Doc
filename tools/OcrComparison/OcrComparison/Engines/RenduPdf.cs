using PDFtoImage;
using SkiaSharp;

namespace OcrComparison.Engines;

/// <summary>
/// Rend un PDF en une image PNG par page, en mémoire.
/// </summary>
/// <remarks>
/// Partagé par tous les moteurs qui travaillent sur des pixels plutôt que sur le PDF.
/// </remarks>
public static class RenduPdf
{
    public const int Dpi = 300;

    public static List<byte[]> EnPngs(string cheminPdf)
    {
        var documentBytes = File.ReadAllBytes(cheminPdf);
        var pages = new List<byte[]>();

        foreach (var imagePage in Conversion.ToImages(documentBytes, options: new RenderOptions(Dpi: Dpi)))
        {
            using (imagePage)
            using (var pngPage = imagePage.Encode(SKEncodedImageFormat.Png, 100))
            {
                pages.Add(pngPage.ToArray());
            }
        }

        return pages;
    }
}
