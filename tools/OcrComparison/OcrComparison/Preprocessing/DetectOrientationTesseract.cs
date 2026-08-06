namespace OcrComparison.Preprocessing;
using Tesseract;

public sealed class DetectOrientationTesseract(TesseractEngine moteur)
{
    public int DetectOrientation(Pix pix, out float confiance)
    {
        using var page = moteur.Process(pix, PageSegMode.OsdOnly);
        page.DetectBestOrientation(out int orientationDegree, out confiance);
        return orientationDegree;
    }
}