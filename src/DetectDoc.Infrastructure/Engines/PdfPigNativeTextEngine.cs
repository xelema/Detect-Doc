using DetectDoc.Core.Engines;
using UglyToad.PdfPig;

namespace DetectDoc.Infrastructure.Engines;

public sealed class PdfPigNativeTextEngine : ITextExtractionEngine
{
    public string Name => "pdfpig";

    public IReadOnlyList<PageExtractionResult> Extract(
        Stream fluxDocument,
        CancellationToken cancellationToken = default)
    {
        PdfStream.Validate(fluxDocument);
        cancellationToken.ThrowIfCancellationRequested();

        using var document = PdfDocument.Open(fluxDocument);
        var resultats = new List<PageExtractionResult>(document.NumberOfPages);
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var texte = string.Join(" ", page.GetWords().Select(mot => mot.Text));
            resultats.Add(new PageExtractionResult(page.Number, texte, Confiance: null));
        }

        return resultats;
    }
}
