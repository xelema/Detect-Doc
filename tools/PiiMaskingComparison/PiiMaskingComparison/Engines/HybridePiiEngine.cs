namespace PiiMaskingComparison.Engines;

/// <summary>
/// Union des plages de deux moteurs, sans arbitrage : les règles couvrent les identifiants
/// structurés, le NER couvre les noms, et aucun paramètre nouveau n'est introduit ici.
/// </summary>
public sealed class HybridePiiEngine(IPiiDetectionEngine regles, IPiiDetectionEngine modele) : IPiiDetectionEngine
{
    public string Name => $"hybride-{modele.Name}";
    public string Version => $"{regles.Name}{regles.Version}+{modele.Name}{modele.Version}";

    public IReadOnlyDictionary<string, object> Parametres =>
        regles.Parametres
            .Concat(modele.Parametres.Select(p => new KeyValuePair<string, object>($"{modele.Name}_{p.Key}", p.Value)))
            .ToDictionary(p => p.Key, p => p.Value);

    public IReadOnlyList<Detection> Detecter(string texte) =>
        [.. regles.Detecter(texte), .. modele.Detecter(texte)];

    public void Dispose()
    {
        regles.Dispose();
        modele.Dispose();
    }
}
