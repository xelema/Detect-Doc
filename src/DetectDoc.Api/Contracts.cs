namespace DetectDoc.Api;

using DetectDoc.Core.Classification;
using Microsoft.AspNetCore.Mvc;

public sealed record ResultatExtractionPageHttp(
    int NumeroPage,
    string Texte,
    float? ConfianceOcr);

public sealed record ClassificationHttpRequest(string Texte, string[] Types);

public sealed record ResultatClassificationLotDocumentHttp(
    string NomFichier,
    DocumentClassificationResult? Resultat,
    ProblemDetails? Erreur);
