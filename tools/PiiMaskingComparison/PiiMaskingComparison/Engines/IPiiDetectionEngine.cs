namespace PiiMaskingComparison.Engines;

public interface IPiiDetectionEngine : IDisposable
{
    string Name { get; }
    string Version { get; }
    IReadOnlyDictionary<string, object> Parametres { get; }

    IReadOnlyList<Detection> Detecter(string texte);
}

public readonly record struct Detection(CategoriePii Categorie, int Start, int Length, string Regle);

public enum CategoriePii
{
    // Clé du manifest qu'aucune catégorie ne couvre encore : un run qui en compte demande une
    // reprise du mapping.
    Inconnue,
    Nom,
    Organisme,
    Email,
    Telephone,
    Adresse,
    Iban,
    Bic,
    Siret,
    IdentifiantProfessionnel,
    NumeroDocument,
    ReseauSocial
}
