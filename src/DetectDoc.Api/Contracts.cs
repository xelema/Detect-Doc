namespace DetectDoc.Api;

using Microsoft.AspNetCore.Mvc;

public sealed record ResultatExtractionPageHttp(
    int NumeroPage,
    string Texte,
    float? ConfianceOcr);

public sealed record ClassificationHttpRequest(string Texte, string[] Types);

public sealed record ResultatClassificationDocumentHttp(
    string Type,
    double Confiance,
    string Moteur,
    int PagesTraitees,
    long ExtractionMs,
    long PiiMaskingMs,
    long ClassificationMs,
    long TotalMs,
    string? ExecutionId);

public sealed record ResultatClassificationLotDocumentHttp(
    string NomFichier,
    ResultatClassificationDocumentHttp? Resultat,
    ProblemDetails? Erreur);
