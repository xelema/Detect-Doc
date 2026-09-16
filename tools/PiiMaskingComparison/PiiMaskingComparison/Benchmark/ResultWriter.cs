using System.Text.Json;

namespace PiiMaskingComparison.Benchmark;

public sealed class ResultWriter(string chemin) : IDisposable
{
    private readonly StreamWriter _fichier = new(chemin) { AutoFlush = true };

    public void Ecrire(ResultatDocument resultat)
    {
        _fichier.WriteLine(JsonSerializer.Serialize(resultat, JsonRun.Options));
    }

    public void Dispose()
    {
        _fichier.Dispose();
    }
}
