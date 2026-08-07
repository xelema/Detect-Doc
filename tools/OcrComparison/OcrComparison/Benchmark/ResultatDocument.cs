namespace OcrComparison.Benchmark;

public sealed record ResultatDocument
{
    public required string Moteur { get; init; }
    public required string Id { get; init; }
    public required string TypeAttendu { get; init; }
    public required double ScoreLevenshtein { get; init; }
    public required double ScoreJaccard { get; init; }
    public required Statut Statut { get; init; }
    public string? MessageErreur { get; init; }
    public float? ConfianceOcr { get; init; } // uniquement Tesseract, nul sinon (ex. PdfPig)
    public bool RepliDeclenche { get; init; } // passé par les quatre orientations, D49
    public required string Regime { get; init; }
    public required string Split { get; init; }
    public required int Pages { get; init; }
    public required int NbMotsReference { get; init; }
    public required int NbMotsExtrait { get; init; }
    public string? TexteReference { get; init; }
    public string? TexteExtrait { get; init; }
    public required double DureeMs { get; init; } // temps écoulé
    public required double CpuMs {  get; init; } // temps processus consommé
    public required long PicMemoireOctets { get; init; } // pic du processus
    public required long MemoireOctets { get; init; } // mémoire au moment de la lecture
}

public enum Statut
{
    Ok,
    ReferenceAbsente,
    TexteVide,
    Erreur
}