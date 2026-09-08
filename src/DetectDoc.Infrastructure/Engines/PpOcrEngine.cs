using DetectDoc.Core.Engines;
using PDFtoImage;
using RapidOcrNet;
using UglyToad.PdfPig;

namespace DetectDoc.Infrastructure.Engines;

public sealed class PpOcrEngine : ITextExtractionEngine
{
    private const int Dpi = 300;
    private const int NombreThreadsOnnx = 1;

    private static readonly string VersionAssembly =
        typeof(RapidOcr).Assembly.GetName().Version?.ToString() ?? string.Empty;

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
        _ocr.InitModels(JeuDeModeles(taille, repertoireModeles), NombreThreadsOnnx);
        Parametres = new Dictionary<string, object>
        {
            ["taille"] = taille,
            ["dpi"] = Dpi,
            ["threads_onnx"] = NombreThreadsOnnx,
            ["reconnaissances_paralleles"] = reconnaissancesParalleles,
            ["classifieur_orientation"] = _optionsOcr.DoAngle
        };
    }

    public string Name => $"PP-OCRv6-{_taille}";
    public string Version => VersionAssembly;
    public IReadOnlyDictionary<string, object> Parametres { get; }

    public IReadOnlyList<ResultatExtractionPage> Extract(Stream fluxDocument, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fluxDocument);
        if (!fluxDocument.CanRead || !fluxDocument.CanSeek)
        {
            throw new ArgumentException("Le flux doit être lisible et seekable.", nameof(fluxDocument));
        }

        cancellationToken.ThrowIfCancellationRequested();
        ValiderSignature(fluxDocument);

        int nombrePages;
        using (var document = PdfDocument.Open(fluxDocument))
        {
            nombrePages = document.NumberOfPages;
        }

        var pages = new List<ResultatExtractionPage>(nombrePages);
        for (var numeroPage = 0; numeroPage < nombrePages; numeroPage++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            fluxDocument.Position = 0;

            using var image = Conversion.ToImage(
                fluxDocument,
                numeroPage,
                leaveOpen: true,
                password: null,
                options: new RenderOptions(Dpi: Dpi));
            var resultat = _ocr.Detect(image, _optionsOcr);
            var blocs = resultat.TextBlocks;
            var confiances = blocs
                .Where(bloc => bloc.CharScores is { Length: > 0 })
                .SelectMany(bloc => bloc.CharScores!)
                .ToArray();
            var texte = string.Join(" ", blocs.Select(bloc => bloc.Text));

            pages.Add(new ResultatExtractionPage(
                numeroPage + 1,
                texte,
                confiances.Length > 0 ? confiances.Average() : null));
        }

        return pages;
    }

    private static RapidOcrModelSet JeuDeModeles(string taille, string repertoireModeles)
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

    private static void ValiderSignature(Stream fluxDocument)
    {
        Span<byte> signature = stackalloc byte[5];
        fluxDocument.Position = 0;
        var estPdf = fluxDocument.ReadAtLeast(signature, signature.Length, throwOnEndOfStream: false) == signature.Length
            && signature.SequenceEqual("%PDF-"u8);
        fluxDocument.Position = 0;

        if (!estPdf)
        {
            throw new InvalidDataException("Le flux ne porte pas une signature PDF valide.");
        }
    }

    public void Dispose() => _ocr.Dispose();
}
