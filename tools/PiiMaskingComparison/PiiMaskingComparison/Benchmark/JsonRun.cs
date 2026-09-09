using System.Text.Json;
using System.Text.Json.Serialization;

namespace PiiMaskingComparison.Benchmark;

/// <summary>
/// Options de sérialisation communes aux fichiers produits par un run.
/// </summary>
/// <remarks>
/// Les métadonnées et le JSONL de résultats décrivent la même exécution : ils doivent suivre
/// la même convention de nommage, sans quoi un script d'analyse en aval devrait en connaître
/// deux. Les catégories sortent en chaîne, un entier ne se relit pas.
/// </remarks>
public static class JsonRun
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Même convention, mise en forme pour un fichier destiné à être lu à l'œil.</summary>
    public static readonly JsonSerializerOptions OptionsIndentees = new(Options) { WriteIndented = true };
}
