using System.Text.Json;
using System.Text.Json.Serialization;

namespace PiiMaskingComparison.Benchmark;

/// <summary>Options de sérialisation communes aux fichiers produits par un run.</summary>
public static class JsonRun
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter() }
    };

    public static readonly JsonSerializerOptions OptionsIndentees = new(Options) { WriteIndented = true };
}
