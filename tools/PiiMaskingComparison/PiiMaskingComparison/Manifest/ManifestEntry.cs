using System.Text.Json;
using System.Text.Json.Serialization;

namespace PiiMaskingComparison.Manifest;

// Les noms JSON sont explicites : le manifest appartient au CorpusGenerator, un outil Python, et
// son contrat de nommage ne doit pas suivre la convention des fichiers de sortie du benchmark.
// `pii_injectees` reste en JsonElement, le générateur y écrit tantôt une chaîne, tantôt un tableau.
public record ManifestEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type_attendu")] string TypeAttendu,
    [property: JsonPropertyName("texte_reference")] string? TexteReference,
    [property: JsonPropertyName("split")] string Split,
    [property: JsonPropertyName("pii_injectees")] IReadOnlyDictionary<string, JsonElement>? PiiInjectees
);
