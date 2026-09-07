using DetectDoc.Core.Engines;
using UglyToad.PdfPig;

namespace DetectDoc.Infrastructure.Engines;

public class PdfPigNativeTextEngine : ITextExtractionEngine
{
    private const int NombrePagesMaximum = 20;

    private static readonly IReadOnlyDictionary<string, object> ParametresMoteur =
        new Dictionary<string, object>
        {
            ["extraction"] = "GetWords"
        };

    private static readonly string VersionAssembly =
        typeof(PdfDocument).Assembly.GetName().Version?.ToString() ?? string.Empty;

    public string Name => "PdfPig";
    public string Version => VersionAssembly;
    public IReadOnlyDictionary<string, object> Parametres => ParametresMoteur;

    public IReadOnlyList<ResultatExtractionPage> Extract(
        Stream fluxDocument,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fluxDocument);
        if (!fluxDocument.CanRead || !fluxDocument.CanSeek)
        {
            throw new ArgumentException("Le flux doit être lisible et seekable.", nameof(fluxDocument));
        }

        cancellationToken.ThrowIfCancellationRequested();
        ValiderSignature(fluxDocument);

        using var document = PdfDocument.Open(fluxDocument);
        if (document.NumberOfPages is < 1 or > NombrePagesMaximum)
        {
            throw new InvalidDataException($"Le PDF doit contenir entre 1 et {NombrePagesMaximum} pages.");
        }

        var resultats = new List<ResultatExtractionPage>(document.NumberOfPages);
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var texte = string.Join(" ", page.GetWords().Select(mot => mot.Text));
            resultats.Add(new ResultatExtractionPage(page.Number, texte, Confiance: null));
        }

        return resultats;
    }

    private static void ValiderSignature(Stream fluxDocument)
    {
        Span<byte> signature = stackalloc byte[5];
        fluxDocument.Position = 0;
        var estPdf = fluxDocument.ReadAtLeast(signature, signature.Length, throwOnEndOfStream: false) == signature.Length
            && signature.SequenceEqual("%PDF-"u8);
        fluxDocument.Position = 0;

        if (!estPdf)
        {
            throw new InvalidDataException("Le flux ne porte pas une signature PDF valide.");
        }
    }

    public void Dispose()
    {
    }
}
