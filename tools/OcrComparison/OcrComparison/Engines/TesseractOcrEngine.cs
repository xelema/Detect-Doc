using System.Text;
using PDFtoImage;
using SkiaSharp;
using Tesseract;

namespace OcrComparison.Engines;

public class TesseractOcrEngine : ITextExtractionEngine, IDisposable
{
    // Le DPI de rendu est le principal levier de réglage d'un bench OCR : il arbitre la qualité
    // contre le temps et la mémoire. Il se déclare donc ici, et Parametres n'en est que la
    // projection destinée aux métadonnées du run. L'inverse obligerait Extract à déballer un
    // object depuis un dictionnaire de reporting, et une faute de frappe sur la clé ne se verrait
    // qu'à l'exécution.
    private const int Dpi = 300;
    private const string Langue = "fra";
    private const EngineMode Mode = EngineMode.Default;

    private static readonly IReadOnlyDictionary<string, object> ParametresMoteur =
        new Dictionary<string, object>
        {
            ["langue"] = Langue,
            ["dpi"] = Dpi,
            ["engine_mode"] = Mode.ToString()
        };

    private readonly TesseractEngine _moteur = new(Path.Combine(AppContext.BaseDirectory, "tessdata"),
        Langue, Mode);

    public string Name => "Tesseract";
    public string Version => _moteur.Version;
    public IReadOnlyDictionary<string, object> Parametres => ParametresMoteur;

    public string Extract(string pdfPath)
    {
        var resultat = new StringBuilder();
        var documentBytes = File.ReadAllBytes(pdfPath);
        var dpi = (int)Parametres["dpi"];

        int numeroPage = 0;
        // Chaque page passe par un bitmap puis un PNG en mémoire : Tesseract ne lit pas le PDF,
        // et Pix n'accepte qu'une image encodée. Bitmap et PNG portent tous deux de la mémoire
        // non gérée, d'où le double using : sans lui, un corpus entier de pages rendues en 300
        // DPI s'accumule jusqu'au prochain passage du GC.
        foreach (var imagePage in Conversion.ToImages(documentBytes, options: new RenderOptions(Dpi: Dpi)))
        {
            using (imagePage)
            {
                using var pngPage = imagePage.Encode(SKEncodedImageFormat.Png, 100);
                
                // TEMPORAIRE, à retirer avant merge
                Directory.CreateDirectory("debug-images");
                var nomPage = $"{Path.GetFileNameWithoutExtension(pdfPath)}-p{numeroPage}.png";
                File.WriteAllBytes(Path.Combine("debug-images", nomPage), pngPage.ToArray());
                
                using var img = Pix.LoadFromMemory(pngPage.ToArray());
                img.XRes = Dpi;
                img.YRes = Dpi;

                using var page = _moteur.Process(img);
                resultat.Append(page.GetText()).Append(' ');
                
                numeroPage++;
            }
        }
        return resultat.ToString();
    }

    public void Dispose()
    {
        _moteur.Dispose();
        GC.SuppressFinalize(this);
    }
}
