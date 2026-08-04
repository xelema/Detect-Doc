namespace OcrComparison.Manifest;
using System.Text.Json.Serialization;

public record ManifestEntry(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("fichier")] string Fichier,
        [property: JsonPropertyName("type_attendu")] string TypeAttendu,
        [property: JsonPropertyName("texte_reference")] string TextReference,
        [property: JsonPropertyName("regime")] string Regime
    );