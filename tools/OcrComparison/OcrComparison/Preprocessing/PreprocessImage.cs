namespace OcrComparison.Preprocessing;
using Tesseract;

public static class PreprocessImage
{
    public static Pix ApplyRotation(Pix source, int orientationDegres) => orientationDegres switch
    {
        0 => source.Clone(),
        90 => source.Rotate90(-1),
        180 => source.Rotate90(1).Rotate90(1),
        270 => source.Rotate90(1),
        _ => throw new ArgumentOutOfRangeException(nameof(orientationDegres), orientationDegres,
            "Tesseract (DetectBestOrientation()) ne devrait renvoyer que 0, 90, 180 ou 270")
    };
}