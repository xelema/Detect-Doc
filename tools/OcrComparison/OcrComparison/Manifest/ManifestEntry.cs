using System.Text.Json.Serialization;

namespace OcrComparison.Manifest;

/// <remarks>
/// Le mapping des noms JSON est explicite : le manifest est produit par le CorpusGenerator,
/// un outil Python. Son contrat de nommage lui appartient et ne doit pas dépendre de la
/// convention de sérialisation choisie pour les fichiers de sortie du benchmark.
/// </remarks>
public record ManifestEntry(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("fichier")] string Fichier,
        [property: JsonPropertyName("type_attendu")] string TypeAttendu,
        [property: JsonPropertyName("texte_reference")] string TexteReference,
        [property: JsonPropertyName("regime")] string Regime,
        [property: JsonPropertyName("split")] string Split,
        [property: JsonPropertyName("pages")] int Pages
    );