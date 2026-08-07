using System.Diagnostics;
using System.Text;
using OcrComparison.Preprocessing;
using Tesseract;

namespace OcrComparison.Engines;

public class TesseractOcrEngine : ITextExtractionEngine
{
    private const string Langue = "fra";
    private const EngineMode Mode = EngineMode.Default;

    // mesuré le 06/08/2026 sur les cinq documents qui échouaient à l'OSD
    // permet à une page de garder son orientation si la confiance sur la ROTATION
    // est SUPERIEURE à SeuilConfianceOsd
    // sinon prend celle du reste du document
    private const float SeuilConfianceOsd = 1.0f;

    // mesuré le 07/08/2026 sur 67 documents
    // permet de remesurer l'OCR sur les 4 orientations (0°, 90°, 180°, 270°) si la confiance sur le TEXTE
    // est INFERIEURE à SeuilConfianceExtraction
    private const float SeuilConfianceExtraction = 0.80f;

    private static readonly int[] OrientationsPossibles = [0, 90, 180, 270];

    private readonly DetectOrientationTesseract? _detecteurOrientation;
    private readonly OptionsPretraitement _options;
    private readonly IReadOnlyDictionary<string, object> _parametres;

    private static readonly string TesseractDataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
    private readonly TesseractEngine _moteur = new(TesseractDataPath, Langue, Mode);
    private readonly TesseractEngine? _orientationEngine;

    public string Name => "Tesseract";
    public string Version => _moteur.Version;

    public IReadOnlyDictionary<string, object> Parametres => _parametres;

    public TesseractOcrEngine(OptionsPretraitement? optionsPretraitement)
    {
        _options = optionsPretraitement ?? new OptionsPretraitement();

        // Le modèle « osd » pèse une dizaine de mégaoctets et doit être présent dans tessdata :
        // ne le charger que si le redressement est demandé, sinon rien ne s'en sert.
        if (_options.Redresser)
        {
            _orientationEngine = new TesseractEngine(TesseractDataPath, "osd", Mode);
            _detecteurOrientation = new DetectOrientationTesseract(_orientationEngine);
        }

        _parametres = new Dictionary<string, object>
        {
            ["langue"] = Langue,
            ["dpi"] = RenduPdf.Dpi,
            ["engine_mode"] = Mode.ToString(),
            ["redressement"] = _options.Redresser,
            ["detourage_avant_osd"] = _options.DetourerAvantOsd,
            ["deskew"] = _options.Deskew,
            ["seuil_confiance_osd"] = SeuilConfianceOsd,
            ["seuil_confiance_extraction"] = SeuilConfianceExtraction
        };
    }

    /// <summary>
    /// OCRise le document, puis rejoue les quatre orientations si le résultat n'inspire pas
    /// confiance.
    /// </summary>
    /// <remarks>
    /// Le déclenchement se fait sur la confiance OCR du document (moyenne des
    /// <see cref="Page.GetMeanConfidence"/> de chaque page) et jamais sur un écart au texte
    /// attendu : sur un document réel, il n'y a pas de texte attendu.
    /// </remarks>
    public ResultatExtraction Extract(string cheminDocument)
    {
        var (texte, confiance) = Ocriser(cheminDocument);

        if (!string.IsNullOrWhiteSpace(texte) && confiance >= SeuilConfianceExtraction)
        {
            return new ResultatExtraction(texte, confiance, RepliDeclenche: false);
        }

        var nom = Path.GetFileNameWithoutExtension(cheminDocument);
        Console.WriteLine(
            $"{nom}: confiance OCR {confiance:F4} sous le seuil de {SeuilConfianceExtraction}, tentative des quatre orientations");

        var (texteRepli, confianceRepli) = OcriserToutesOrientations(cheminDocument);

        // Le repli ne passe ni par l'OSD ni par le détourage : il peut rendre moins bien que la
        // pipeline normale, auquel cas son texte est jeté plutôt que de dégrader le document.
        return confianceRepli > confiance
            ? new ResultatExtraction(texteRepli, confianceRepli, RepliDeclenche: true)
            : new ResultatExtraction(texte, confiance, RepliDeclenche: true);
    }

    private (string Texte, float ConfianceMoyenne) Ocriser(string pdfPath)
    {
        var pages = RenduPdf.EnPngs(pdfPath);
        var nomDocument = Path.GetFileNameWithoutExtension(pdfPath);

        // Une orientation par page, pas une pour tout le document : un recto-verso retourné
        // tête-bêche (au lieu de tourné comme une page de livre) donne des pages à 180° l'une de
        // l'autre, une seule orientation partagée serait fausse sur l'une des deux.
        var orientationsParPage = _detecteurOrientation != null
            ? DeciderOrientationsParPage(_detecteurOrientation, nomDocument, pages)
            : new int[pages.Count];

        var resultat = new StringBuilder();
        var confiances = new List<float>();

        for (var i = 0; i < pages.Count; i++)
        {
            var (texte, confiance) = TraiterPage(pages[i], orientationsParPage[i], NomPage(nomDocument, i));
            resultat.Append(texte).Append(' ');
            confiances.Add(confiance);
        }

        var confianceMoyenne = confiances.Count > 0 ? confiances.Average() : 0f;
        return (resultat.ToString(), confianceMoyenne);
    }

    /// <summary>
    /// OCRise le document dans les quatre orientations et garde la plus confiante. Coûte quatre
    /// OCR complets, donc réservé au repli.
    /// </summary>
    private (string Texte, float ConfianceMoyenne) OcriserToutesOrientations(string pdfPath)
    {
        var pages = RenduPdf.EnPngs(pdfPath);
        var nomDocument = Path.GetFileNameWithoutExtension(pdfPath);

        var meilleurTexte = string.Empty;
        var meilleureConfiance = float.MinValue;
        var meilleureOrientation = 0;

        foreach (var orientation in OrientationsPossibles)
        {
            var chrono = Stopwatch.StartNew();
            var resultat = new StringBuilder();
            var confiancesPages = new List<float>();

            for (var i = 0; i < pages.Count; i++)
            {
                var (texte, confiance) = TraiterPage(pages[i], orientation, NomPage(nomDocument, i));
                resultat.Append(texte).Append(' ');
                confiancesPages.Add(confiance);
            }

            var confianceMoyenne = confiancesPages.Count > 0 ? confiancesPages.Average() : 0f;
            Console.WriteLine(
                $"{nomDocument}: orientation {orientation}° essayée, confiance OCR moyenne {confianceMoyenne} ({chrono.ElapsedMilliseconds} ms)");

            if (confianceMoyenne > meilleureConfiance)
            {
                meilleureConfiance = confianceMoyenne;
                meilleureOrientation = orientation;
                meilleurTexte = resultat.ToString();
            }
        }

        Console.WriteLine(
            $"{nomDocument}: orientation {meilleureOrientation}° retenue par confiance OCR ({meilleureConfiance})");

        return (meilleurTexte, meilleureConfiance);
    }

    // Analyse l'OSD page par page sans appliquer de rotation.
    // Une page sous le seuil de confiance hérite de l'orientation la plus
    // confiante du document ; une page au-dessus conserve la sienne.
    private int[] DeciderOrientationsParPage(
        DetectOrientationTesseract detecteurOrientation,
        string nomDocument,
        List<byte[]> pages)
    {
        // Un PDF qui ne rend aucune page n'a pas de « page la plus confiante » à désigner.
        if (pages.Count == 0)
        {
            return [];
        }

        var detections = new (int Orientation, float Confiance)[pages.Count];

        for (var i = 0; i < pages.Count; i++)
        {
            var nomPage = NomPage(nomDocument, i);
            var octetsPourOsd = _options.DetourerAvantOsd
                ? DetourerZoneUtile.ApplyDetourage(pages[i])
                : pages[i];

            using var pixPourOsd = Pix.LoadFromMemory(octetsPourOsd);
            pixPourOsd.XRes = RenduPdf.Dpi;
            pixPourOsd.YRes = RenduPdf.Dpi;

            var orientationDegree = 0;
            var confiance = 0f;
            try
            {
                orientationDegree = detecteurOrientation.DetectOrientation(pixPourOsd, out confiance);
            }
            catch (TesseractException ex)
            {
                Console.WriteLine($"{nomPage}: échec détection d'orientation ({ex.Message})");
            }

            Console.WriteLine($"{nomPage}: orientation : {orientationDegree}° avec une confiance de {confiance}");
            // La confiance compte même quand l'angle détecté est 0° : c'est une détection tout
            // aussi valide qu'un quart de tour, une page confiante à 0° doit pouvoir servir de
            // référence à une page voisine moins sûre d'elle.
            detections[i] = (orientationDegree, confiance);
        }

        var meilleureDetection = detections[0];
        for (var i = 1; i < pages.Count; i++)
        {
            if (detections[i].Confiance > meilleureDetection.Confiance)
            {
                meilleureDetection = detections[i];
            }
        }

        var orientationsRetenues = new int[pages.Count];
        for (var i = 0; i < pages.Count; i++)
        {
            var nomPage = NomPage(nomDocument, i);
            var detection = detections[i];

            if (detection.Confiance >= SeuilConfianceOsd)
            {
                orientationsRetenues[i] = detection.Orientation;
            }
            else if (meilleureDetection.Confiance >= SeuilConfianceOsd)
            {
                orientationsRetenues[i] = meilleureDetection.Orientation;
                Console.WriteLine(
                    $"{nomPage}: confiance propre {detection.Confiance} sous le seuil de {SeuilConfianceOsd}, emprunte {meilleureDetection.Orientation}° au reste du document (confiance {meilleureDetection.Confiance})");
            }
            else
            {
                orientationsRetenues[i] = 0;
                Console.WriteLine(
                    $"{nomPage}: orientation ignorée, aucune page du document n'atteint le seuil de {SeuilConfianceOsd}");
            }
        }

        return orientationsRetenues;
    }

    /// <remarks>
    /// Le try/finally couvre le deskew autant que l'OCR : un Pix porte une image pleine page en
    /// mémoire non managée, et le benchmark enchaîne les documents après une exception, donc une
    /// fuite ici se cumulerait sur tout le run et fausserait ses relevés mémoire.
    /// </remarks>
    private (string Texte, float Confiance) TraiterPage(byte[] pngPage, int orientation, string nomPage)
    {
        using var img = Pix.LoadFromMemory(pngPage);
        img.XRes = RenduPdf.Dpi;
        img.YRes = RenduPdf.Dpi;

        Pix? pixTourne = null;
        Pix? pixDeskewed = null;
        try
        {
            pixTourne = orientation != 0 ? PreprocessImage.ApplyRotation(img, orientation) : null;
            var pixAvantDeskew = pixTourne ?? img;

            if (_options.Deskew)
            {
                pixDeskewed = pixAvantDeskew.Deskew(out var scew);
                Console.WriteLine(
                    $"{nomPage}: orientation {orientation}°, deskew {scew.Angle}° avec une confiance de {scew.Confidence}");
            }

            using var page = _moteur.Process(pixDeskewed ?? pixAvantDeskew);
            return (page.GetText(), page.GetMeanConfidence());
        }
        finally
        {
            pixDeskewed?.Dispose();
            pixTourne?.Dispose();
        }
    }

    private static string NomPage(string nomDocument, int index) => $"{nomDocument}-p{index}.png";

    public void Dispose()
    {
        _orientationEngine?.Dispose();
        _moteur.Dispose();
        GC.SuppressFinalize(this);
    }
}
