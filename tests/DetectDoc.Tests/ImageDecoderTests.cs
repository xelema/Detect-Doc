using DetectDoc.Infrastructure.Engines;
using SkiaSharp;
using Xunit;

namespace DetectDoc.Tests;

public class ImageDecoderTests
{
    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    [InlineData("bmp")]
    [InlineData("webp")]
    [InlineData("gif")]
    public void DecodeOriented_DecodeChaqueFormat(string format)
    {
        var contenu = format switch
        {
            "jpeg" => FichiersDeTest.Jpeg(),
            "bmp" => FichiersDeTest.Bmp(),
            "webp" => FichiersDeTest.WebP(),
            "gif" => FichiersDeTest.Gif(),
            _ => FichiersDeTest.Png()
        };
        using var flux = new MemoryStream(contenu);

        using var bitmap = ImageDecoder.DecodeOriented(flux);

        Assert.True(bitmap.Width > 0 && bitmap.Height > 0);
    }

    [Fact]
    public void DecodeOriented_SignaleUneImageIllisible()
    {
        // Signature PNG valide mais contenu tronqué : le codec n'aboutit pas.
        var tronque = FichiersDeTest.Png()[..20];
        using var flux = new MemoryStream(tronque);

        Assert.Throws<InvalidDataException>(() => ImageDecoder.DecodeOriented(flux));
    }

    [Fact]
    public void AppliquerOrientation_RetourneLaSourceSansChangement()
    {
        using var source = new SKBitmap(2, 3);

        var resultat = ImageDecoder.AppliquerOrientation(source, SKEncodedOrigin.TopLeft);

        Assert.Same(source, resultat);
    }

    [Fact]
    public void AppliquerOrientation_PivoteDe90Degres()
    {
        // Bitmap 2x3 avec un pixel marqué en haut à gauche : après rotation horaire il doit
        // se retrouver en haut à droite d'un bitmap 3x2.
        using var source = new SKBitmap(2, 3);
        source.Erase(SKColors.White);
        source.SetPixel(0, 0, SKColors.Black);

        using var resultat = ImageDecoder.AppliquerOrientation(source, SKEncodedOrigin.RightTop);

        Assert.Equal(3, resultat.Width);
        Assert.Equal(2, resultat.Height);
        Assert.Equal(SKColors.Black, resultat.GetPixel(2, 0));
        Assert.Equal(SKColors.White, resultat.GetPixel(0, 0));
    }

    [Fact]
    public void AppliquerOrientation_PivoteDe180Degres()
    {
        using var source = new SKBitmap(2, 3);
        source.Erase(SKColors.White);
        source.SetPixel(0, 0, SKColors.Black);

        using var resultat = ImageDecoder.AppliquerOrientation(source, SKEncodedOrigin.BottomRight);

        Assert.Equal(2, resultat.Width);
        Assert.Equal(3, resultat.Height);
        Assert.Equal(SKColors.Black, resultat.GetPixel(1, 2));
    }

    [Fact]
    public void AppliquerOrientation_MiroirHorizontal()
    {
        using var source = new SKBitmap(3, 2);
        source.Erase(SKColors.White);
        source.SetPixel(0, 0, SKColors.Black);

        using var resultat = ImageDecoder.AppliquerOrientation(source, SKEncodedOrigin.TopRight);

        Assert.Equal(3, resultat.Width);
        Assert.Equal(2, resultat.Height);
        Assert.Equal(SKColors.Black, resultat.GetPixel(2, 0));
    }
}
