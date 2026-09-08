using System.Diagnostics;
using System.Text;
using RapidOcrNet;
using SkiaSharp;

namespace OcrComparison.Engines;

/// <summary>
/// PP-OCRv6 exécuté en ONNX dans le processus, via RapidOcrNet.
/// </summary>
public sealed class PpOcrEngine : ITextExtractionEngine
{
    // Tesseract est mono-thread. ONNX Runtime prendrait tous les cœurs par défaut, ce qui rendrait
    // les temps écoulés des deux moteurs incomparables : un thread ici, pour que l'écart mesuré
    // soit celui des algorithmes et pas celui du parallélisme.
    private const int NbThreadsOnnx = 1;

    private static readonly string VersionAssembly =
        typeof(RapidOcr).Assembly.GetName().Version?.ToString() ?? string.Empty;

    private readonly RapidOcr _ocr = new();
    private readonly RapidOcrOptions _optionsOcr;
    private readonly string _taille;

    public PpOcrEngine(string taille, int reconnaissancesParalleles = 1)
    {
        _taille = taille;
        _optionsOcr = RapidOcrOptions.PPOCRv6 with
        {
            RecMaxDegreeOfParallelism = reconnaissancesParalleles
        };
        _ocr.InitModels(JeuDeModeles(taille), NbThreadsOnnx);

        Parametres = new Dictionary<string, object>
        {
            ["taille"] = taille,
            ["dpi"] = RenduPdf.Dpi,
            ["threads_onnx"] = NbThreadsOnnx,
            ["reconnaissances_paralleles"] = reconnaissancesParalleles,
            ["classifieur_orientation"] = _optionsOcr.DoAngle,
            ["seuil_texte"] = _optionsOcr.TextScore,
            ["limit_side_len"] = _optionsOcr.LimitSideLen
        };
    }

    /// <remarks>
    /// Les jeux prédéfinis portent des chemins relatifs, donc résolus depuis le répertoire courant :
    /// un run lancé d'ailleurs que du dossier du binaire ne trouverait rien. On les rebase sur
    /// <see cref="AppContext.BaseDirectory"/> en gardant le reste du jeu, dont les constantes de
    /// normalisation de la détection, qui ne sont pas les mêmes en v5 et en v6.
    /// </remarks>
    private static RapidOcrModelSet JeuDeModeles(string taille)
    {
        var jeu = taille switch
        {
            "tiny" => RapidOcrModelSet.PPOCRv6Tiny,
            "small" => RapidOcrModelSet.PPOCRv6Small,
            "medium" => RapidOcrModelSet.PPOCRv6Medium,
            _ => throw new ArgumentException($"Taille PP-OCRv6 inconnue : {taille}.", nameof(taille))
        };

        return jeu with
        {
            DetModelPath = Absolu(jeu.DetModelPath),
            ClsModelPath = Absolu(jeu.ClsModelPath),
            RecModelPath = Absolu(jeu.RecModelPath),
            KeysPath = Absolu(jeu.KeysPath)
        };
    }

    private static string Absolu(string cheminRelatif) =>
        Path.Combine(AppContext.BaseDirectory, cheminRelatif);

    public string Name => $"PP-OCRv6-{_taille}";
    public string Version => VersionAssembly;
    public IReadOnlyDictionary<string, object> Parametres { get; }

    public ResultatExtraction Extract(string cheminDocument)
    {
        var texte = new StringBuilder();
        var confiances = new List<float>();
        var pageNumber = 0;

        var horodatageRendu = Stopwatch.GetTimestamp();
        var listePngs = RenduPdf.EnPngs(cheminDocument);
        var dureeRenduPdf = Stopwatch.GetElapsedTime(horodatageRendu);
        var dureeDecodagePng = TimeSpan.Zero;
        var dureeDetect = TimeSpan.Zero;

        foreach (var png in listePngs)
        {
            pageNumber++;
            Console.WriteLine("Extraction du texte de : " + cheminDocument + " (page " + pageNumber + "/" + listePngs.Count + ")");

            var horodatageDecodage = Stopwatch.GetTimestamp();
            using var image = SKBitmap.Decode(png);

            dureeDecodagePng += Stopwatch.GetElapsedTime(horodatageDecodage);

            var horodatageDetect = Stopwatch.GetTimestamp();
            var resultat = _ocr.Detect(image, _optionsOcr);
            dureeDetect += Stopwatch.GetElapsedTime(horodatageDetect);

            foreach (var bloc in resultat.TextBlocks)
            {
                texte.Append(bloc.Text).Append(' ');

                if (bloc.CharScores is { Length: > 0 })
                {
                    confiances.AddRange(bloc.CharScores);
                }
            }
        }

        return new ResultatExtraction(
            texte.ToString(),
            confiances.Count > 0 ? confiances.Average() : 0f,
            RepliDeclenche: false,
            DureeRenduPdfMs: dureeRenduPdf.TotalMilliseconds,
            DureeDecodagePngMs: dureeDecodagePng.TotalMilliseconds,
            DureeDetectMs: dureeDetect.TotalMilliseconds);
    }

    public void Dispose() => _ocr.Dispose();
}
