using SkiaSharp;

namespace DetectDoc.Infrastructure.Engines;

public static class ImageDecoder
{
    // SKCodec décode BMP, GIF (première frame), JPEG, PNG et WebP.
    public static SKBitmap DecodeOriented(Stream fluxImage)
    {
        using var codec = SKCodec.Create(fluxImage)
            ?? throw new InvalidDataException("L'image n'a pas pu être décodée.");
        var decodee = SKBitmap.Decode(codec)
            ?? throw new InvalidDataException("L'image n'a pas pu être décodée.");
        try
        {
            var orientee = AppliquerOrientation(decodee, codec.EncodedOrigin);
            if (!ReferenceEquals(orientee, decodee))
            {
                decodee.Dispose();
            }

            return orientee;
        }
        catch
        {
            decodee.Dispose();
            throw;
        }
    }

    // L'orientation EXIF n'est pas appliquée par le décodage : sans ce redressement une photo
    // de document pivotée arrive couchée à l'OCR. La source est retournée telle quelle quand
    // l'orientation est l'identité : l'appelant ne doit alors pas la disposer deux fois.
    public static SKBitmap AppliquerOrientation(SKBitmap source, SKEncodedOrigin origine)
    {
        if (origine is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default)
        {
            return source;
        }

        var transpose = origine is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var cible = new SKBitmap(
            transpose ? source.Height : source.Width,
            transpose ? source.Width : source.Height);
        try
        {
            using var canvas = new SKCanvas(cible);
            switch (origine)
            {
                case SKEncodedOrigin.TopRight:
                    canvas.Translate(source.Width, 0);
                    canvas.Scale(-1, 1);
                    break;
                case SKEncodedOrigin.BottomRight:
                    canvas.Translate(source.Width, source.Height);
                    canvas.RotateDegrees(180);
                    break;
                case SKEncodedOrigin.BottomLeft:
                    canvas.Translate(0, source.Height);
                    canvas.Scale(1, -1);
                    break;
                case SKEncodedOrigin.LeftTop:
                    canvas.RotateDegrees(90);
                    canvas.Scale(1, -1);
                    break;
                case SKEncodedOrigin.RightTop:
                    canvas.Translate(source.Height, 0);
                    canvas.RotateDegrees(90);
                    break;
                case SKEncodedOrigin.RightBottom:
                    canvas.Translate(source.Height, source.Width);
                    canvas.RotateDegrees(90);
                    canvas.Scale(-1, 1);
                    break;
                case SKEncodedOrigin.LeftBottom:
                    canvas.Translate(0, source.Width);
                    canvas.RotateDegrees(270);
                    break;
            }

            // Rotation et miroir exacts : l'échantillonnage au plus proche évite tout adoucissement.
            canvas.DrawBitmap(source, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
            canvas.Flush();
            return cible;
        }
        catch
        {
            cible.Dispose();
            throw;
        }
    }
}
