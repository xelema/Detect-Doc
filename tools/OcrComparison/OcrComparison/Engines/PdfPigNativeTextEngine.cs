using UglyToad.PdfPig;

namespace OcrComparison.Engines;

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

    public string Extract(string pdfPath)
    {
        using var document = PdfDocument.Open(pdfPath);
        var mots = document.GetPages().SelectMany(page => page.GetWords()).Select(mot => mot.Text);
        return string.Join(" ", mots);
    }
}
