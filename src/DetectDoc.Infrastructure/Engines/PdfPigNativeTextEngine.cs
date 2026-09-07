using DetectDoc.Core.Engines;
using UglyToad.PdfPig;

namespace DetectDoc.Infrastructure.Engines;

public class PdfPigNativeTextEngine : ITextExtractionEngine
{
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

    public ResultatExtraction Extract(Stream fluxDocument)
    {
        ArgumentNullException.ThrowIfNull(fluxDocument);
        using var document = PdfDocument.Open(fluxDocument);
        return ExtraireDepuisDocument(document);
    }

    private static ResultatExtraction ExtraireDepuisDocument(PdfDocument document)
    {
        var mots = document.GetPages().SelectMany(page => page.GetWords()).Select(mot => mot.Text);
        return new ResultatExtraction(string.Join(" ", mots), Confiance: null);
    }

    public void Dispose()
    {
    }
}
