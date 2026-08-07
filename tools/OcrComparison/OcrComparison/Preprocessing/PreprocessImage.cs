namespace OcrComparison.Preprocessing;
using Tesseract;

public static class PreprocessImage
{
    public static Pix ApplyRotation(Pix source, int orientationDegres)
    {
        switch (orientationDegres)
        {
            case 0:
                return source.Clone();
            case 90:
                return source.Rotate90(-1);
            case 270:
                return source.Rotate90(1);
            case 180:
                // Leptonica ne sait pas faire le demi-tour d'un coup : deux quarts de tour, en
                // libérant l'intermédiaire. C'est un Pix comme un autre, donc une image pleine
                // page en mémoire non managée que personne ne récupérerait sans ce using.
                using (var quartDeTour = source.Rotate90(1))
                {
                    return quartDeTour.Rotate90(1);
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(orientationDegres), orientationDegres,
                    "Tesseract (DetectBestOrientation()) ne devrait renvoyer que 0, 90, 180 ou 270");
        }
    }
}
