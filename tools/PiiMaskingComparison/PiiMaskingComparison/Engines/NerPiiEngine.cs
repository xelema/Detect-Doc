using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PiiMaskingComparison.Engines;

/// <summary>
/// Moteur NER piloté par un processus Python tenu ouvert pour tout le run : un appel par
/// document rechargerait le modèle à chaque fois. Le backend est le nom du moteur côté Python.
/// </summary>
public sealed class NerPiiEngine : IPiiDetectionEngine, IDisposable
{
    private readonly string _backend;
    private readonly Process _processus;
    private readonly Dictionary<string, JsonElement> _demarrage;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<Reponse>> _attentes = new();
    private readonly Lock _envoi = new();
    private int _prochainId;

    public NerPiiEngine(string backend)
    {
        _backend = backend;
        var python = Environment.GetEnvironmentVariable("PII_PYTHON") ?? "python3";
        var script = Path.Combine(AppContext.BaseDirectory, "Ner", "detecteur.py");

        _processus = Process.Start(new ProcessStartInfo(python, ["-u", script, backend])
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // Sans ce constructeur, `Encoding.UTF8` fait écrire un BOM en tête du flux et le
            // premier document arrive illisible côté Python.
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardInputEncoding = new UTF8Encoding(false)
        }) ?? throw new InvalidOperationException($"Processus Python non démarré : {python} {script} {backend}.");

        // Le script signale la fin du chargement du modèle avant d'attendre un document : sans
        // cette poignée de main, les secondes de chargement tomberaient dans la mesure du
        // premier document.
        _demarrage = Lire<Dictionary<string, JsonElement>>();
        if (_demarrage.TryGetValue("erreur", out var erreur))
        {
            throw Echouer($"Chargement du modèle impossible : {erreur}");
        }

        // Les réponses reviennent dans le désordre dès que le script a plusieurs travailleurs :
        // une boucle de lecture unique les redistribue à l'appelant qui attend cet identifiant.
        Task.Run(Distribuer);
    }

    public string Name => _backend;
    public string Version => "1";

    /// <summary>
    /// Republiés tels que le script les déclare, sans liste de champs connue d'avance : un
    /// réglage propre à un moteur doit se retrouver dans le run, sinon deux runs de
    /// configurations différentes deviennent indiscernables.
    /// </summary>
    public IReadOnlyDictionary<string, object> Parametres =>
        _demarrage.ToDictionary(p => p.Key, p => Lisible(p.Value));

    private static object Lisible(JsonElement valeur) => valeur.ValueKind switch
    {
        JsonValueKind.Number => Math.Round(valeur.GetDouble(), 3),
        JsonValueKind.True or JsonValueKind.False => valeur.GetBoolean(),
        _ => valeur.ToString()
    };

    // Rapporté par le processus Python lui-même : les compteurs .NET ne voient pas l'espace
    // d'un enfant, et publieraient quelques mégaoctets pour un modèle qui en occupe des milliers.
    public long? PicMemoireMoteurOctets { get; private set; }

    public IReadOnlyList<Detection> Detecter(string texte)
    {
        var id = Interlocked.Increment(ref _prochainId);
        var attente = new TaskCompletionSource<Reponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _attentes[id] = attente;

        lock (_envoi)
        {
            _processus.StandardInput.WriteLine(JsonSerializer.Serialize(new { id, texte }));
            _processus.StandardInput.Flush();
        }

        var reponse = attente.Task.GetAwaiter().GetResult();
        if (reponse.Erreur is not null)
        {
            throw new InvalidOperationException($"{_backend} : {reponse.Erreur}");
        }

        PicMemoireMoteurOctets = Math.Max(PicMemoireMoteurOctets ?? 0, reponse.PicMemoireOctets ?? 0);

        return reponse.Detections
            .Select(d => new Detection(Enum.Parse<CategoriePii>(d.Categorie), d.Regle, NatureRegle.Modele, d.Start, d.Length))
            .ToList();
    }

    private void Distribuer()
    {
        while (_processus.StandardOutput.ReadLine() is { } ligne)
        {
            var reponse = JsonSerializer.Deserialize<Reponse>(ligne);
            if (reponse is not null && _attentes.TryRemove(reponse.Id, out var attente))
            {
                attente.SetResult(reponse);
            }
        }

        // Flux fermé : personne ne recevra plus rien, il vaut mieux réveiller les appelants que
        // les laisser attendre indéfiniment.
        foreach (var attente in _attentes.Values)
        {
            attente.TrySetException(new InvalidOperationException($"Processus {_backend} interrompu."));
        }
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

    private sealed record Reponse(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("detections")] IReadOnlyList<DetectionPython> Detections,
        [property: JsonPropertyName("erreur")] string? Erreur,
        [property: JsonPropertyName("pic_memoire_octets")] long? PicMemoireOctets);

    private sealed record DetectionPython(
        [property: JsonPropertyName("categorie")] string Categorie,
        [property: JsonPropertyName("start")] int Start,
        [property: JsonPropertyName("length")] int Length,
        [property: JsonPropertyName("regle")] string Regle);
}
