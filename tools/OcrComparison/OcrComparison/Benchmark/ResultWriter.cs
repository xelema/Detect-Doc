using System.Text.Json;
using System.Text.Json.Serialization;

namespace OcrComparison.Benchmark;

public sealed class ResultWriter(string chemin) : IDisposable
{
    private readonly StreamWriter _fichier = new(chemin){ AutoFlush = true };
    
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter() }
    };

    public void Ecrire(ResultatDocument resultat)
    {
        var jsonLine = JsonSerializer.Serialize(resultat, Options);
        _fichier.WriteLine(jsonLine);
    }
    
    public void Dispose()
    {
        _fichier.Dispose();
    }

}