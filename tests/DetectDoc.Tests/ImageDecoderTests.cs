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

    // Chaque origine EXIF est vérifiée par la destination du pixel (0,0) d'une source 2x3 :
    // les cas transposés échangent largeur et hauteur, les autres les conservent.
    [Theory]
    [InlineData(SKEncodedOrigin.TopRight, 2, 3, 1, 0)]
    [InlineData(SKEncodedOrigin.BottomRight, 2, 3, 1, 2)]
    [InlineData(SKEncodedOrigin.BottomLeft, 2, 3, 0, 2)]
    [InlineData(SKEncodedOrigin.LeftTop, 3, 2, 0, 0)]
    [InlineData(SKEncodedOrigin.RightTop, 3, 2, 2, 0)]
    [InlineData(SKEncodedOrigin.RightBottom, 3, 2, 2, 1)]
    [InlineData(SKEncodedOrigin.LeftBottom, 3, 2, 0, 1)]
    public void AppliquerOrientation_PlaceLePixelMarqueSelonLOrigine(
        SKEncodedOrigin origine, int largeur, int hauteur, int xAttendu, int yAttendu)
    {
        using var source = new SKBitmap(2, 3);
        source.Erase(SKColors.White);
        source.SetPixel(0, 0, SKColors.Black);

        using var resultat = ImageDecoder.AppliquerOrientation(source, origine);

        Assert.Equal(largeur, resultat.Width);
        Assert.Equal(hauteur, resultat.Height);
        Assert.Equal(SKColors.Black, resultat.GetPixel(xAttendu, yAttendu));
    }
}
