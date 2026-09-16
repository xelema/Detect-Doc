namespace PiiMaskingComparison.Engines;

public interface IPiiDetectionEngine : IDisposable
{
    string Name { get; }
    string Version { get; }
    IReadOnlyDictionary<string, object> Parametres { get; }

    IReadOnlyList<Detection> Detecter(string texte);

    /// <summary>
    /// Pic mémoire du processus qui porte réellement le modèle, quand ce n'est pas celui du
    /// benchmark. Les compteurs mémoire de .NET ignorent un processus enfant, où vivent le
    /// runtime Python et les poids : sans cette valeur, un moteur NER paraîtrait gratuit.
    /// </summary>
    long? PicMemoireMoteurOctets => null;
}

public readonly record struct Detection(
    CategoriePii Categorie, string Regle, NatureRegle Nature, int Start, int Length);

/// <summary>
/// Ce sur quoi une règle s'appuie, donc ce qui survivra ou non à un changement de corpus.
/// </summary>
public enum NatureRegle
{
    /// <summary>Forme normée, indépendante du document : IBAN, SIRET, adresse de courriel.</summary>
    Specification,

    /// <summary>Valeur annoncée par un libellé du document, plus ou moins standard selon le champ.</summary>
    Libelle,

    /// <summary>Liste de mots relevée dans le corpus. Ce que le générateur produit, pas ce qu'écrit un client.</summary>
    Vocabulaire,

    /// <summary>Modèle appris hors du projet, qui ne doit rien au corpus.</summary>
    Modele
}

/// <remarks>
/// Une catégorie par nature de valeur, pas par rôle dans le document : le manifest distingue
/// `email` et `email_emetteur`, la protection à mesurer est la même.
/// </remarks>
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
