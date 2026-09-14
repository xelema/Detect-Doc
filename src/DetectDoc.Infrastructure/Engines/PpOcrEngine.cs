using DetectDoc.Core.Engines;
using PDFtoImage;
using PDFtoImage.Exceptions;
using RapidOcrNet;

namespace DetectDoc.Infrastructure.Engines;

public sealed class PpOcrEngine : ITextExtractionEngine, IDisposable
{
    private const int Dpi = 300;
    private const int OnnxThreadCount = 1;

    private readonly RapidOcr _ocr = new();
    private readonly RapidOcrOptions _optionsOcr;
    private readonly string _taille;
    // La thread-safety de RapidOcrNet n'est pas démontrée : les inférences sont sérialisées
    // tant que le test de charge n'a pas tranché.
    private readonly Lock _verrouOcr = new();

    public PpOcrEngine(string taille, string repertoireModeles, int reconnaissancesParalleles = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taille);
        ArgumentException.ThrowIfNullOrWhiteSpace(repertoireModeles);
        ArgumentOutOfRangeException.ThrowIfLessThan(reconnaissancesParalleles, 1);

        _taille = taille;
        _optionsOcr = RapidOcrOptions.PPOCRv6 with
        {
            RecMaxDegreeOfParallelism = reconnaissancesParalleles
        };
        _ocr.InitModels(ModelSet(taille, repertoireModeles), OnnxThreadCount);
    }

    public string Name => $"ppocr-{_taille}";

    public IReadOnlyList<PageExtractionResult> Extract(Stream fluxDocument, CancellationToken cancellationToken = default)
    {
        PdfStream.Validate(fluxDocument);
        cancellationToken.ThrowIfCancellationRequested();

        var pages = new List<PageExtractionResult>();
        try
        {
            // ToImages produit les rendus à la demande : le PDF n'est parsé qu'une fois et une
            // page rendue est libérée avant la suivante, le document entier ne tient pas en mémoire.
            foreach (var image in Conversion.ToImages(
                fluxDocument,
                leaveOpen: true,
                password: null,
                options: new RenderOptions(Dpi: Dpi)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (image)
                {
                    OcrResult resultat;
                    lock (_verrouOcr)
                    {
                        resultat = _ocr.Detect(image, _optionsOcr);
                    }
                    var blocs = resultat.TextBlocks;
                    var confiances = blocs
                        .Where(bloc => bloc.CharScores is { Length: > 0 })
                        .SelectMany(bloc => bloc.CharScores!)
                        .ToArray();

                    pages.Add(new PageExtractionResult(
                        pages.Count + 1,
                        string.Join(" ", blocs.Select(bloc => bloc.Text)),
                        confiances.Length > 0 ? confiances.Average() : null));
                }
            }
        }
        catch (PdfException exception)
        {
            // Un PDF que la signature ouvre peut tout de même échouer au rendu PDFium : l'erreur
            // est celle du document, elle remonte comme les autres PDF invalides (422).
            throw new InvalidDataException("Le PDF n'a pas pu être rendu en image.", exception);
        }

        return pages;
    }

    private static RapidOcrModelSet ModelSet(string taille, string repertoireModeles)
    {
        var jeu = taille switch
        {
            "tiny" => RapidOcrModelSet.PPOCRv6Tiny,
            "small" => RapidOcrModelSet.PPOCRv6Small,
            "medium" => RapidOcrModelSet.PPOCRv6Medium,
            _ => throw new ArgumentException($"Taille PP-OCRv6 inconnue : {taille}.", nameof(taille))
        };

        var repertoireV6 = Path.Combine(repertoireModeles, "v6");
        var modeleDetecteur = Path.Combine(repertoireV6, Path.GetFileName(jeu.DetModelPath));
        var modeleReconnaissance = Path.Combine(repertoireV6, Path.GetFileName(jeu.RecModelPath));
        var dictionnaire = Path.Combine(repertoireV6, Path.GetFileName(jeu.KeysPath));
        // Le classifieur d'orientation reste celui livré par RapidOcrNet, à côté du binaire.
        var modeleOrientation = Path.Combine(AppContext.BaseDirectory, jeu.ClsModelPath);
        foreach (var chemin in new[] { modeleDetecteur, modeleOrientation, modeleReconnaissance, dictionnaire })
        {
            if (!File.Exists(chemin))
            {
                throw new InvalidOperationException($"Le modèle PP-OCR requis est introuvable : {chemin}");
            }
        }

        return jeu with
        {
            DetModelPath = modeleDetecteur,
            ClsModelPath = modeleOrientation,
            RecModelPath = modeleReconnaissance,
            KeysPath = dictionnaire
        };
    }

    public void Dispose() => _ocr.Dispose();
}
