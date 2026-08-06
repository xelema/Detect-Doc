using System.Text;
using OcrComparison.Preprocessing;
using PDFtoImage;
using SkiaSharp;
using Tesseract;

namespace OcrComparison.Engines;

public class TesseractOcrEngine : ITextExtractionEngine, IDisposable
{
    private const int Dpi = 300;
    private const string Langue = "fra";
    private const EngineMode Mode = EngineMode.Default;

    // mesuré le 06/08/2026 sur les cinq documents qui échouaient à l'OSD
    // à changer si trop d'erreurs
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
        var nomDocument = Path.GetFileNameWithoutExtension(pdfPath);

        // PDF converti en PNG en mémoire (requis par Tesseract) une seule fois pour tout le doc.
        var pages = new List<byte[]>();
        foreach (var imagePage in Conversion.ToImages(documentBytes, options: new RenderOptions(Dpi: Dpi)))
        {
            using (imagePage)
            using (var pngPage = imagePage.Encode(SKEncodedImageFormat.Png, 100))
            {
                pages.Add(pngPage.ToArray());
            }
        }

        // TEMPORAIRE, à retirer avant merge
        Directory.CreateDirectory("debug-images-before");
        Directory.CreateDirectory("debug-images-after");
        Directory.CreateDirectory("debug-images-pass-crop");
        Directory.CreateDirectory("debug-images-pass-deskew");
        for (var i = 0; i < pages.Count; i++)
        {
            File.WriteAllBytes(Path.Combine("debug-images-before", NomPage(nomDocument, i)), pages[i]);
        }

        // Orientation globale basée sur la page ayant le meilleur score OSD
        var orientationDocument = _detecteurOrientation != null
            ? DeciderOrientationDocument(nomDocument, pages)
            : 0;

        for (var i = 0; i < pages.Count; i++)
        {
            var nomPage = NomPage(nomDocument, i);
            using var img = Pix.LoadFromMemory(pages[i]);
            img.XRes = Dpi;
            img.YRes = Dpi;

            var pixTourne = orientationDocument != 0 ? PreprocessImage.ApplyRotation(img, orientationDocument) : null;
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

                pixAProcess.Save(Path.Combine("debug-images-after", nomPage), ImageFormat.Png);
            }
            finally
            {
                pixDeskewed?.Dispose();
                pixTourne?.Dispose();
            }
        }

        return resultat.ToString();
    }

    // N'applique aucune rotation : ne fait que consulter l'OSD page par page pour choisir
    // l'orientation qui vaudra pour tout le document.
    private int DeciderOrientationDocument(string nomDocument, List<byte[]> pages)
    {
        var orientationDocument = 0;
        var meilleureConfiance = 0f;

        for (var i = 0; i < pages.Count; i++)
        {
            var nomPage = NomPage(nomDocument, i);
            var octetsPourOsd = _options.DetourerAvantOsd
                ? DetourerZoneUtile.ApplyDetourage(pages[i])
                : pages[i];

            using var pixPourOsd = Pix.LoadFromMemory(octetsPourOsd);

            // TEMPORAIRE
            pixPourOsd.Save(Path.Combine("debug-images-pass-crop", nomPage), ImageFormat.Png);

            pixPourOsd.XRes = Dpi;
            pixPourOsd.YRes = Dpi;

            var orientationDegree = 0;
            var confiance = 0f;
            try
            {
                orientationDegree = _detecteurOrientation!.DetectOrientation(pixPourOsd, out confiance);
            }
            catch (TesseractException ex)
            {
                Console.WriteLine($"{nomPage}: échec détection d'orientation ({ex.Message})");
            }

            Console.WriteLine($"{nomPage}: orientation : {orientationDegree}° avec une confiance de {confiance}");
            if (orientationDegree != 0 && confiance > meilleureConfiance)
            {
                meilleureConfiance = confiance;
                orientationDocument = orientationDegree;
            }
        }

        if (meilleureConfiance < SeuilConfianceOsd)
        {
            if (orientationDocument != 0)
            {
                Console.WriteLine(
                    $"{nomDocument}: orientation ignorée, confiance {meilleureConfiance} sous le seuil de {SeuilConfianceOsd}");
            }

            return 0;
        }

        Console.WriteLine($"{nomDocument}: orientation retenue pour le document : {orientationDocument}° (confiance {meilleureConfiance})");
        return orientationDocument;
    }

    private static string NomPage(string nomDocument, int index) => $"{nomDocument}-p{index}.png";

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
