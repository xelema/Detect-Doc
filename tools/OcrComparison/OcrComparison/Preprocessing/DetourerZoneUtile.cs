namespace OcrComparison.Preprocessing;
using OpenCvSharp;

public static class DetourerZoneUtile
{
    public static byte[] ApplyDetourage(byte[] source)
    {
        using var img = Mat.ImDecode(source, ImreadModes.Grayscale);
        
        // threshold
        using var thresholdMat = new Mat();
        Cv2.Threshold(img, thresholdMat, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);

        // Supprime le liseré de 1 px généré par le rendu PDF/PNG : sans cela, le contour
        // de la page entière masque la carte réelle.
        const int epaisseurBordure = 3;
        Cv2.Rectangle(thresholdMat, new Rect(0, 0, thresholdMat.Cols, thresholdMat.Rows), Scalar.Black,
            thickness: epaisseurBordure);

        // Ne garde que la plus grande composante connexe pour ignorer les
        // artefacts (bruit, ombre, tampon) qui étireraient le rectangle englobant. 
        Cv2.FindContours(thresholdMat, out Point[][] contours, out _, RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        if (contours.Length == 0) return source;

        var plusGrandContour = contours.OrderByDescending(c => Cv2.ContourArea(c)).First();
        var rect = Cv2.BoundingRect(plusGrandContour);

        // marge autour du rectangle trouve, bornee aux dimensions de l'image
        const double margeRatio = 0.08;
        var margeX = (int)(rect.Width * margeRatio);
        var margeY = (int)(rect.Height * margeRatio);

        var x = Math.Max(0, rect.X - margeX);
        var y = Math.Max(0, rect.Y - margeY);
        var right = Math.Min(img.Width, rect.X + rect.Width + margeX);
        var bottom = Math.Min(img.Height, rect.Y + rect.Height + margeY);

        var rectElargi = new Rect(x, y, right - x, bottom - y);

        // recadrage de l'image d'origine (pas du masque seuille) sur ce rectangle
        using var cropped = img[rectElargi];

        Cv2.ImEncode(".png", cropped, out byte[] buf);
        return buf;
    }
}