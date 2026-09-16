using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace DetectDoc.Tests;

internal static class PdfDeTest
{
    // Une page par texte fourni ; un texte vide produit une page sans texte natif.
    public static byte[] Construire(params string[] textes)
    {
        var builder = new PdfDocumentBuilder();
        var police = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var texte in textes)
        {
            var page = builder.AddPage(PageSize.A4);
            if (texte.Length > 0)
            {
                page.AddText(texte, 12, new PdfPoint(50, 700), police);
            }
        }

        return builder.Build();
    }
}
