using System.Text;
using OcrComparison.Preprocessing;
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

    private readonly DetectOrientationTesseract? _detecteurOrientation;
    private readonly OptionsPretraitement _options;

    private static readonly string TesseractDataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
    private readonly TesseractEngine _moteur = new(TesseractDataPath, Langue, Mode);
    private readonly TesseractEngine _orientationEngine = new TesseractEngine(TesseractDataPath, "osd", Mode);

    public string Name => "Tesseract";
    public string Version => _moteur.Version;
    
    public IReadOnlyDictionary<string, object> Parametres => new Dictionary<string, object>
    {
        ["langue"] = Langue,
        ["dpi"] = Dpi,
        ["engine_mode"] = Mode.ToString(),
        ["redressement"] = _options.Redresser
    };

    public TesseractOcrEngine(OptionsPretraitement? optionsPretraitement){
        _options = optionsPretraitement ?? new OptionsPretraitement();
        _detecteurOrientation ??= _options.Redresser ? new DetectOrientationTesseract(_orientationEngine) : null;
    }

    public string Extract(string pdfPath)
    {
        var resultat = new StringBuilder();
        var documentBytes = File.ReadAllBytes(pdfPath);

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
                Directory.CreateDirectory("debug-images-before");
                var nomPage = $"{Path.GetFileNameWithoutExtension(pdfPath)}-p{numeroPage}.png";
                File.WriteAllBytes(Path.Combine("debug-images-before", nomPage), pngPage.ToArray());
                
                using var img = Pix.LoadFromMemory(pngPage.ToArray());
                img.XRes = Dpi;
                img.YRes = Dpi;
                
                // TEMPORAIRE, à retirer avant merge
                Directory.CreateDirectory("debug-images-after");
                
                Pix? pixTourne = null;
                if (_detecteurOrientation != null)
                {
                    int orientationDegree = 0;
                    float confiance = 0;
                    try
                    {
                        orientationDegree = _detecteurOrientation.DetectOrientation(img, out confiance);
                    }
                    catch (TesseractException ex)
                    {
                        Console.WriteLine($"{nomPage}: échec détection d'orientation ({ex.Message}), page laissée telle quelle");
                    }

                    Console.WriteLine($"{nomPage}: orientation : {orientationDegree}° avec une confiance de {confiance}");
                    if (orientationDegree != 0)
                    {
                        pixTourne = PreprocessImage.ApplyRotation(img, orientationDegree);
                    }
                }
                
                var pixAProcess = pixTourne ?? img;

                try
                {
                    ProcessImageToText(resultat, pixAProcess);

                    if (pixTourne != null)
                    {
                        pixTourne.Save(Path.Combine("debug-images-after", nomPage), ImageFormat.Png);
                    }
                }
                finally
                {
                    pixTourne?.Dispose();
                }

                numeroPage++;
            }
        }
        return resultat.ToString();
    }

    private void ProcessImageToText(StringBuilder resultat, Pix img)
    {
        using var page = _moteur.Process(img);
        resultat.Append(page.GetText()).Append(' ');
    }

    public void Dispose()
    {
        _orientationEngine.Dispose();
        _moteur.Dispose();
        GC.SuppressFinalize(this);
    }
}
