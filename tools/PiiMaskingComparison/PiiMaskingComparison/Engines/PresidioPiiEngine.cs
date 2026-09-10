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
    private readonly Demarrage _demarrage;

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
        _demarrage = Lire<Demarrage>();
        if (_demarrage.Erreur is not null)
        {
            throw Echouer($"Chargement du modèle impossible : {_demarrage.Erreur}");
        }
    }

    public string Name => "presidio";
    public string Version => "1";

    public IReadOnlyDictionary<string, object> Parametres => new Dictionary<string, object>
    {
        ["modele"] = _demarrage.Modele,
        ["langue"] = "fr",
        ["entites"] = _demarrage.Entites,
        ["chargement_ms"] = Math.Round(_demarrage.ChargementMs)
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
            .Select(d => new Detection(Enum.Parse<CategoriePii>(d.Categorie), d.Regle, NatureRegle.Modele, d.Start, d.Length))
            .ToList();
    }

    private T Lire<T>()
    {
        var ligne = _processus.StandardOutput.ReadLine();
        if (ligne is null)
        {
            throw Echouer("Processus Python interrompu.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(ligne)
                   ?? throw Echouer($"Réponse Python vide : {ligne}");
        }
        catch (JsonException)
        {
            throw Echouer($"Réponse Python illisible : {ligne}");
        }
    }

    /// <summary>
    /// Le message d'erreur utile est dans la trace Python, sur stderr. La lire exige que le
    /// processus soit fini, sans quoi l'appel attendrait indéfiniment un flux resté ouvert.
    /// </summary>
    private InvalidOperationException Echouer(string message)
    {
        _processus.StandardInput.Close();
        _processus.WaitForExit(5000);
        var trace = _processus.StandardError.ReadToEnd().TrimEnd();
        if (trace.Length > 600)
        {
            trace = string.Concat("...", trace.AsSpan(trace.Length - 600));
        }

        return new InvalidOperationException($"{message} {trace}");
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

    private sealed record Demarrage(
        [property: JsonPropertyName("chargement_ms")] double ChargementMs,
        [property: JsonPropertyName("modele")] string Modele,
        [property: JsonPropertyName("entites")] string Entites,
        [property: JsonPropertyName("erreur")] string? Erreur);

    private sealed record Reponse(
        [property: JsonPropertyName("detections")] IReadOnlyList<DetectionPython> Detections,
        [property: JsonPropertyName("erreur")] string? Erreur);

    private sealed record DetectionPython(
        [property: JsonPropertyName("categorie")] string Categorie,
        [property: JsonPropertyName("start")] int Start,
        [property: JsonPropertyName("length")] int Length,
        [property: JsonPropertyName("regle")] string Regle);
}
