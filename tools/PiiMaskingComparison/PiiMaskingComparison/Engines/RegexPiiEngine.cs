using System.Text.RegularExpressions;

namespace PiiMaskingComparison.Engines;

/// <summary>
/// Baseline de référence : trois motifs sur les formes structurées, rien d'autre. Elle sert de
/// plancher aux moteurs suivants, pas de proposition d'implémentation pour l'API.
/// </summary>
public sealed partial class RegexPiiEngine : IPiiDetectionEngine
{
    public string Name => "regex";
    public string Version => "1";

    public IReadOnlyDictionary<string, object> Parametres { get; } = new Dictionary<string, object>
    {
        ["categories"] = "email, iban, telephone",
        ["timeout_ms"] = TimeoutMs
    };

    public IReadOnlyList<Detection> Detecter(string texte)
    {
        var detections = new List<Detection>();
        Ajouter(detections, Email(), texte, CategoriePii.Email, nameof(Email));
        Ajouter(detections, Iban(), texte, CategoriePii.Iban, nameof(Iban));
        Ajouter(detections, TelephoneFr(), texte, CategoriePii.Telephone, nameof(TelephoneFr));
        return detections;
    }

    private static void Ajouter(List<Detection> detections, Regex motif, string texte, CategoriePii categorie, string regle)
    {
        foreach (var correspondance in motif.Matches(texte))
        {
            var m = (Match)correspondance;
            detections.Add(new Detection(categorie, m.Index, m.Length, regle));
        }
    }

    private const int TimeoutMs = 2000;

    [GeneratedRegex(@"[\p{L}\p{N}._%+-]+@[\p{L}\p{N}.-]+\.\p{L}{2,}", RegexOptions.None, TimeoutMs)]
    private static partial Regex Email();

    // Les documents impriment l'IBAN par groupes de quatre, le manifest le porte sans espace :
    // le motif tolère les deux, la longueur exacte par pays n'est pas vérifiée à ce stade.
    [GeneratedRegex(@"\b[A-Z]{2}[0-9]{2}(?: ?[A-Z0-9]{4}){3,7} ?[A-Z0-9]{0,4}\b", RegexOptions.None, TimeoutMs)]
    private static partial Regex Iban();

    [GeneratedRegex(@"\b0[1-9](?:[ .-]?[0-9]{2}){4}\b", RegexOptions.None, TimeoutMs)]
    private static partial Regex TelephoneFr();
}
