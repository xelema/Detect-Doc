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

    // Mesuré le 06/08/2026 sur les cinq documents qui échouaient à l'OSD sur le split dev :
    // les quatre détections correctes tombent à 2,04 ou plus, la seule détection fausse
    // (cvi-0014-p1, un quart de tour opposé à celui attendu) tombe à 0,77. Ce plancher rejette
    // ce cas précis sans perdre aucun des quatre gains.
    private const float SeuilConfianceOsd = 1.0f;

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
        ["redressement"] = _options.Redresser,
        ["detourage_avant_osd"] = _options.DetourerAvantOsd,
        ["deskew"] = _options.Deskew
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
                Directory.CreateDirectory("debug-images-pass-crop");
                Directory.CreateDirectory("debug-images-pass-deskew");
                
                
                Pix? pixTourne = null;
                if (_detecteurOrientation != null)
                {
                    var octetsPourOsd = _options.DetourerAvantOsd
                        ? DetourerZoneUtile.ApplyDetourage(pngPage.ToArray())
                        : pngPage.ToArray();
                    
                    using var pixPourOsd = Pix.LoadFromMemory(octetsPourOsd);
                    
                    // TEMPORAIRE
                    pixPourOsd.Save(Path.Combine("debug-images-pass-crop", nomPage), ImageFormat.Png);
                    
                    pixPourOsd.XRes = Dpi;
                    pixPourOsd.YRes = Dpi;


                    var orientationDegree = 0;
                    float confiance = 0;
                    try
                    {
                        orientationDegree = _detecteurOrientation.DetectOrientation(pixPourOsd, out confiance);
                    }
                    catch (TesseractException ex)
                    {
                        Console.WriteLine($"{nomPage}: échec détection d'orientation ({ex.Message}), page laissée telle quelle");
                    }

                    Console.WriteLine($"{nomPage}: orientation : {orientationDegree}° avec une confiance de {confiance}");
                    if (orientationDegree != 0 && confiance >= SeuilConfianceOsd)
                    {
                        pixTourne = PreprocessImage.ApplyRotation(img, orientationDegree);
                    }
                    else if (orientationDegree != 0)
                    {
                        Console.WriteLine($"{nomPage}: rotation ignorée, confiance {confiance} sous le seuil de {SeuilConfianceOsd}");
                    }
                }
                
                var pixAvantDeskew = pixTourne ?? img;

                // Corrige le biais résiduel de quelques degrés qu'aucune
                // détection d'orientation par quart de tour ne peut voir. 
                Pix? pixDeskewed = null;
                if (_options.Deskew)
                {
                    pixDeskewed = pixAvantDeskew.Deskew(out Scew scew);
                    Console.WriteLine($"{nomPage}: deskew {scew.Angle}° avec une confiance de {scew.Confidence}");
                    
                    // TEMPORAIRE
                    pixDeskewed.Save(Path.Combine("debug-images-pass-deskew", nomPage), ImageFormat.Png);
                }

                var pixAProcess = pixDeskewed ?? pixAvantDeskew;

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
                    pixDeskewed?.Dispose();
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
