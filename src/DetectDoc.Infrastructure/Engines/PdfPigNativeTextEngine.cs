using DetectDoc.Core.Engines;
using UglyToad.PdfPig;

namespace DetectDoc.Infrastructure.Engines;

public sealed class PdfPigNativeTextEngine : ITextExtractionEngine
{
    public string Name => "PdfPig";

    public IReadOnlyList<ResultatExtractionPage> Extract(
        Stream fluxDocument,
        CancellationToken cancellationToken = default)
    {
        FluxPdf.Valider(fluxDocument);
        cancellationToken.ThrowIfCancellationRequested();

        using var document = PdfDocument.Open(fluxDocument);
        var resultats = new List<ResultatExtractionPage>(document.NumberOfPages);
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var texte = string.Join(" ", page.GetWords().Select(mot => mot.Text));
            resultats.Add(new ResultatExtractionPage(page.Number, texte, Confiance: null));
        }

        return resultats;
    }
}
