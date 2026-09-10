using DetectDoc.Core.Engines;
using PDFtoImage;
using RapidOcrNet;
using UglyToad.PdfPig;

namespace DetectDoc.Infrastructure.Engines;

public sealed class PpOcrEngine : ITextExtractionEngine, IDisposable
{
    private const int Dpi = 300;
    private const int OnnxThreadCount = 1;

    private readonly RapidOcr _ocr = new();
    private readonly RapidOcrOptions _optionsOcr;
    private readonly string _taille;

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

        int nombrePages;
        using (var document = PdfDocument.Open(fluxDocument))
        {
            nombrePages = document.NumberOfPages;
        }

        var pages = new List<PageExtractionResult>(nombrePages);
        for (var numeroPage = 0; numeroPage < nombrePages; numeroPage++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            fluxDocument.Position = 0;

            // Une page rendue est libérée avant la suivante : le PDF entier ne tient jamais en mémoire.
            using var image = Conversion.ToImage(
                fluxDocument,
                numeroPage,
                leaveOpen: true,
                password: null,
                options: new RenderOptions(Dpi: Dpi));
            var blocs = _ocr.Detect(image, _optionsOcr).TextBlocks;
            var confiances = blocs
                .Where(bloc => bloc.CharScores is { Length: > 0 })
                .SelectMany(bloc => bloc.CharScores!)
                .ToArray();

            pages.Add(new PageExtractionResult(
                numeroPage + 1,
                string.Join(" ", blocs.Select(bloc => bloc.Text)),
                confiances.Length > 0 ? confiances.Average() : null));
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
