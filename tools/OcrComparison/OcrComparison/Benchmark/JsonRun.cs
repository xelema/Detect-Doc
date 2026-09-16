using System.Text.Json;
using System.Text.Json.Serialization;

namespace OcrComparison.Benchmark;

/// <summary>
/// Options de sérialisation communes aux fichiers produits par un run.
/// </summary>
/// <remarks>
/// Les métadonnées et le JSONL de résultats décrivent la même exécution : ils doivent suivre
/// la même convention de nommage, sans quoi un script d'analyse en aval devrait en connaître
/// deux. Un domicile unique évite aussi qu'un enum ajouté plus tard sorte en entier d'un côté
/// et en chaîne de l'autre.
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
