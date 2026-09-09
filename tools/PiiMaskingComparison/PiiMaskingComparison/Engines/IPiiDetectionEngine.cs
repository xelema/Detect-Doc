namespace PiiMaskingComparison.Engines;

public interface IPiiDetectionEngine
{
    string Name { get; }
    string Version { get; }
    IReadOnlyDictionary<string, object> Parametres { get; }

    /// <summary>Plages sensibles trouvées dans le texte, dans n'importe quel ordre.</summary>
    IReadOnlyList<Detection> Detecter(string texte);
}

/// <param name="Start">Indice UTF-16 dans la chaîne reçue, sans normalisation préalable.</param>
/// <param name="Regle">
/// Nom de la règle qui a produit la plage. Sert au diagnostic d'un faux positif : sans lui, une
/// détection isolée dans le jsonl ne dit pas quelle partie du moteur reprendre.
/// </param>
public readonly record struct Detection(CategoriePii Categorie, int Start, int Length, string Regle);

public enum CategoriePii
{
    Autre,
    Email,
    Iban,
    Telephone
}
