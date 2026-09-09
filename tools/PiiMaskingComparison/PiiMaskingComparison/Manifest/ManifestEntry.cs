using System.Text.Json;
using System.Text.Json.Serialization;

namespace PiiMaskingComparison.Manifest;

/// <remarks>
/// Le mapping des noms JSON est explicite : le manifest est produit par le CorpusGenerator,
/// un outil Python. Son contrat de nommage lui appartient et ne doit pas dépendre de la
/// convention de sérialisation choisie pour les fichiers de sortie du benchmark.
/// </remarks>
/// <param name="PiiInjectees">
/// Valeurs synthétiques injectées par le générateur, clé métier vers valeur. Les éléments sont
/// laissés en <see cref="JsonElement"/> parce que le générateur écrit tantôt une chaîne
/// (`nom_complet`), tantôt un tableau (`noms_participants`).
/// </param>
public record ManifestEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type_attendu")] string TypeAttendu,
    [property: JsonPropertyName("texte_reference")] string? TexteReference,
    [property: JsonPropertyName("split")] string Split,
    [property: JsonPropertyName("pii_injectees")] IReadOnlyDictionary<string, JsonElement>? PiiInjectees
);
