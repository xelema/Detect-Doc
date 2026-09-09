using PiiMaskingComparison.Engines;

namespace PiiMaskingComparison.Benchmark;

public sealed record ResultatDocument
{
    public required string Moteur { get; init; }
    public required string Id { get; init; }
    public required string TypeAttendu { get; init; }
    public required string Split { get; init; }
    public required Statut Statut { get; init; }
    public string? MessageErreur { get; init; }

    /// <summary>Occurrences attendues du document, chacune couverte ou non par le moteur.</summary>
    public required IReadOnlyList<OccurrencePii> Occurrences { get; init; }

    /// <summary>Détections qui ne recouvrent aucune occurrence attendue.</summary>
    public required IReadOnlyList<DetectionFausse> FauxPositifs { get; init; }

    /// <summary>Valeurs du manifest introuvables dans le texte, donc hors de portée de la mesure.</summary>
    public required IReadOnlyList<ValeurNonLocalisee> ValeursNonLocalisees { get; init; }

    public required int NbDetections { get; init; }
    public required double DureeMs { get; init; }
    public required long OctetsAlloues { get; init; }
}

/// <param name="Couverte">
/// Vrai seulement si toute la plage est masquée. Une occurrence à moitié couverte laisse fuiter
/// de quoi identifier la personne : elle compte comme manquée.
/// </param>
public readonly record struct OccurrencePii(
    CategoriePii Categorie, string Cle, string Valeur, int Start, int Length, bool Couverte);

public readonly record struct DetectionFausse(CategoriePii Categorie, string Regle, string Texte);

public readonly record struct ValeurNonLocalisee(CategoriePii Categorie, string Cle, string Valeur);

public enum Statut
{
    Ok,
    ReferenceAbsente,
    Erreur
}
