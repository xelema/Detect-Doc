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

    public TesseractOcrEngine(OptionsPretraitement? optionsPretraitement)
    {
        _options = optionsPretraitement ?? new OptionsPretraitement();
        _detecteurOrientation = _options.Redresser ? new DetectOrientationTesseract(_orientationEngine) : null;
    }

    public string Extract(string pdfPath) => ExtraireAvecConfiance(pdfPath).Texte;

    /// <summary>
    /// Même pipeline que <see cref="Extract"/>, mais expose aussi la confiance OCR moyenne du
    /// document (moyenne des <see cref="Page.GetMeanConfidence"/> de chaque page), pour décider
    /// d'un repli sans avoir besoin d'un texte de référence : sur un document réel, il n'y en a
    /// pas, contrairement au jaccard utilisé pendant le développement sur le corpus du POC.
    /// </summary>
    public (string Texte, float ConfianceMoyenne) ExtraireAvecConfiance(string pdfPath)
    {
        var resultat = new StringBuilder();
        var confiances = new List<float>();
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

        // Une orientation par page, pas une pour tout le document : un recto-verso retourné
        // tête-bêche (au lieu de tourné comme une page de livre) donne des pages à 180° l'une de
        // l'autre, une seule orientation partagée serait fausse sur l'une des deux.
        var orientationsParPage = _detecteurOrientation != null
            ? DeciderOrientationsParPage(_detecteurOrientation, nomDocument, pages)
            : new int[pages.Count];

        for (var i = 0; i < pages.Count; i++)
        {
            var nomPage = NomPage(nomDocument, i);
            using var img = Pix.LoadFromMemory(pages[i]);
            img.XRes = Dpi;
            img.YRes = Dpi;

            var orientation = orientationsParPage[i];
            var pixTourne = orientation != 0 ? PreprocessImage.ApplyRotation(img, orientation) : null;
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
                confiances.Add(ProcessImageToText(resultat, pixAProcess));

                pixAProcess.Save(Path.Combine("debug-images-after", nomPage), ImageFormat.Png);
            }
            finally
            {
                pixDeskewed?.Dispose();
                pixTourne?.Dispose();
            }
        }

        var confianceMoyenne = confiances.Count > 0 ? confiances.Average() : 0f;
        return (resultat.ToString(), confianceMoyenne);
    }

    // Analyse l'OSD page par page sans appliquer de rotation.
    // Une page sous le seuil de confiance hérite de l'orientation la plus
    // confiante du document ; une page au-dessus conserve la sienne.
    private int[] DeciderOrientationsParPage(
        DetectOrientationTesseract detecteurOrientation,
        string nomDocument,
        List<byte[]> pages)
    {
        var detections = new (int Orientation, float Confiance)[pages.Count];

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

    private static readonly int[] OrientationsPossibles = [0, 90, 180, 270];

    /// <summary>
    /// Repli pour un document dont le score en aval s'avère mauvais malgré la pipeline normale :
    /// OCRise le document dans les quatre orientations et garde celle dont la confiance moyenne
    /// de l'OCR (<see cref="Page.GetMeanConfidence"/>) est la plus haute. Coûte quatre OCR
    /// complets au lieu d'un, donc réservé à ce repli, jamais au chemin par défaut.
    /// </summary>
    public string ExtractMeilleureOrientation(string pdfPath)
    {
        var documentBytes = File.ReadAllBytes(pdfPath);
        var nomDocument = Path.GetFileNameWithoutExtension(pdfPath);

        var pages = new List<byte[]>();
        foreach (var imagePage in Conversion.ToImages(documentBytes, options: new RenderOptions(Dpi: Dpi)))
        {
            using (imagePage)
            using (var pngPage = imagePage.Encode(SKEncodedImageFormat.Png, 100))
            {
                pages.Add(pngPage.ToArray());
            }
        }

        string meilleurTexte = string.Empty;
        List<(Pix Image, string NomPage)>? meilleuresImages = null;
        var meilleureConfiance = float.MinValue;
        var meilleureOrientation = 0;

        foreach (var orientation in OrientationsPossibles)
        {
            var chrono = System.Diagnostics.Stopwatch.StartNew();
            var resultat = new StringBuilder();
            var confiancesPages = new List<float>();
            var imagesFinales = new List<(Pix, string)>();

            for (var i = 0; i < pages.Count; i++)
            {
                var (texte, confiance, imageFinale) = TraiterPage(pages[i], orientation);
                resultat.Append(texte).Append(' ');
                confiancesPages.Add(confiance);
                imagesFinales.Add((imageFinale, NomPage(nomDocument, i)));
            }

            var confianceMoyenne = confiancesPages.Count > 0 ? confiancesPages.Average() : 0f;
            Console.WriteLine(
                $"{nomDocument}: orientation {orientation}° essayée, confiance OCR moyenne {confianceMoyenne} ({chrono.ElapsedMilliseconds} ms)");

            if (confianceMoyenne > meilleureConfiance)
            {
                meilleureConfiance = confianceMoyenne;
                meilleureOrientation = orientation;
                meilleurTexte = resultat.ToString();

                if (meilleuresImages != null)
                {
                    foreach (var (image, _) in meilleuresImages) image.Dispose();
                }

                meilleuresImages = imagesFinales;
            }
            else
            {
                foreach (var (image, _) in imagesFinales) image.Dispose();
            }
        }

        Console.WriteLine(
            $"{nomDocument}: orientation {meilleureOrientation}° retenue par confiance OCR ({meilleureConfiance})");

        // L'image finale, juste avant l'envoi à l'OCR, dans le même dossier que la pipeline
        // normale : un run peut mélanger des documents traités par les deux chemins.
        Directory.CreateDirectory("debug-images-after");
        foreach (var (image, nomPage) in meilleuresImages!)
        {
            image.Save(Path.Combine("debug-images-after", nomPage), ImageFormat.Png);
            image.Dispose();
        }

        return meilleurTexte;
    }

    /// <summary>
    /// Redresse, déskew si activé, OCRise une page, et rend un clone de l'image telle qu'envoyée
    /// à l'OCR : appelant responsable de disposer ce clone.
    /// </summary>
    private (string Texte, float Confiance, Pix ImageFinale) TraiterPage(byte[] pngPage, int orientation)
    {
        using var img = Pix.LoadFromMemory(pngPage);
        img.XRes = Dpi;
        img.YRes = Dpi;

        var pixTourne = orientation != 0 ? PreprocessImage.ApplyRotation(img, orientation) : null;
        var pixAvantDeskew = pixTourne ?? img;

        var pixDeskewed = _options.Deskew ? pixAvantDeskew.Deskew(out _) : null;
        var pixAProcess = pixDeskewed ?? pixAvantDeskew;

        using var page = _moteur.Process(pixAProcess);
        var texte = page.GetText();
        var confiance = page.GetMeanConfidence();
        var imageFinale = pixAProcess.Clone();

        pixDeskewed?.Dispose();
        pixTourne?.Dispose();

        return (texte, confiance, imageFinale);
    }

    private static string NomPage(string nomDocument, int index) => $"{nomDocument}-p{index}.png";

    private float ProcessImageToText(StringBuilder resultat, Pix img)
    {
        using var page = _moteur.Process(img);
        resultat.Append(page.GetText()).Append(' ');
        return page.GetMeanConfidence();
    }

    public void Dispose()
    {
        _orientationEngine.Dispose();
        _moteur.Dispose();
        GC.SuppressFinalize(this);
    }
}
