namespace DetectDoc.Api;

public sealed record ResultatExtractionPageHttp(
    int NumeroPage,
    string Texte,
    float? ConfianceOcr);

public sealed record RequeteClassificationHttp(string Texte, string[] Types);

public sealed record ResultatClassificationDocumentHttp(
    string Type,
    double Confiance,
    string Moteur,
    int PagesTraitees,
    long ExtractionMs,
    long ClassificationMs,
    long TotalMs,
    string? ExecutionId);
