namespace OcrComparison.Benchmark;

public sealed record ResultatDocument
{
    public required string Moteur { get; init; }
    public required string Id { get; init; }
    public required string TypeAttendu { get; init; }
    public required string Regime { get; init; }
    public required string Split { get; init; }
    public required int Pages { get; init; }
    public required int NbMotsReference { get; init; }
    public required int NbMotsExtrait { get; init; }
    public required double ScoreLevenshtein { get; init; }
    public required double ScoreJaccard { get; init; }
    public required Statut Statut { get; init; }
    public string? MessageErreur { get; init; }
}

public enum Statut
{
    Ok,
    ReferenceAbsente,
    TexteVide,
    Erreur
}