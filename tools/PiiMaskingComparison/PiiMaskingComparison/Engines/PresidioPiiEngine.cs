using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PiiMaskingComparison.Engines;

/// <summary>
/// Référence reconnue : Presidio et son moteur spaCy français, pilotés par un processus Python
/// tenu ouvert pour tout le run. Un appel par document rechargerait le modèle à chaque fois.
/// </summary>
public sealed class PresidioPiiEngine : IPiiDetectionEngine, IDisposable
{
    private readonly Process _processus;
    private readonly double _chargementMs;

    public PresidioPiiEngine()
    {
        var python = Environment.GetEnvironmentVariable("PII_PYTHON") ?? "python3";
        var script = Path.Combine(AppContext.BaseDirectory, "Presidio", "detecteur.py");

        _processus = Process.Start(new ProcessStartInfo(python, ["-u", script])
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // Sans ce constructeur, `Encoding.UTF8` fait écrire un BOM en tête du flux et le
            // premier document arrive illisible côté Python.
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardInputEncoding = new UTF8Encoding(false)
        }) ?? throw new InvalidOperationException($"Processus Python non démarré : {python} {script}.");

        // Le script signale la fin du chargement du modèle avant d'attendre un document : sans
        // cette poignée de main, les secondes de chargement tomberaient dans la mesure du
        // premier document.
        _chargementMs = Lire<Demarrage>().ChargementMs;
    }

    public string Name => "presidio";
    public string Version => "1";

    public IReadOnlyDictionary<string, object> Parametres => new Dictionary<string, object>
    {
        ["modele"] = "fr_core_news_sm",
        ["langue"] = "fr",
        ["entites"] = "PERSON, ORGANIZATION, LOCATION, EMAIL_ADDRESS, PHONE_NUMBER, IBAN_CODE",
        ["chargement_ms"] = Math.Round(_chargementMs)
    };

    public IReadOnlyList<Detection> Detecter(string texte)
    {
        _processus.StandardInput.WriteLine(JsonSerializer.Serialize(new { texte }));
        _processus.StandardInput.Flush();

        var reponse = Lire<Reponse>();
        if (reponse.Erreur is not null)
        {
            throw new InvalidOperationException($"Presidio : {reponse.Erreur}");
        }

        return reponse.Detections
            .Select(d => new Detection(Enum.Parse<CategoriePii>(d.Categorie), d.Start, d.Length, d.Regle))
            .ToList();
    }

    private T Lire<T>()
    {
        var ligne = _processus.StandardOutput.ReadLine();
        if (ligne is null)
        {
            var erreur = _processus.StandardError.ReadToEnd();
            throw new InvalidOperationException($"Processus Python interrompu. {erreur}");
        }

        return JsonSerializer.Deserialize<T>(ligne)
               ?? throw new InvalidOperationException($"Réponse Python illisible : {ligne}");
    }

    public void Dispose()
    {
        if (!_processus.HasExited)
        {
            _processus.StandardInput.Close();
            _processus.WaitForExit(5000);
        }

        _processus.Dispose();
    }

    private sealed record Demarrage([property: JsonPropertyName("chargement_ms")] double ChargementMs);

    private sealed record Reponse(
        [property: JsonPropertyName("detections")] IReadOnlyList<DetectionPython> Detections,
        [property: JsonPropertyName("erreur")] string? Erreur);

    private sealed record DetectionPython(
        [property: JsonPropertyName("categorie")] string Categorie,
        [property: JsonPropertyName("start")] int Start,
        [property: JsonPropertyName("length")] int Length,
        [property: JsonPropertyName("regle")] string Regle);
}
