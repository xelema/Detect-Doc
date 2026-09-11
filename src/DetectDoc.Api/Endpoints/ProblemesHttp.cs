using UglyToad.PdfPig.Core;
using Microsoft.AspNetCore.Mvc;

namespace DetectDoc.Api.Endpoints;

// Les routes de classification et d'extraction rencontrent les mêmes exceptions : le mapping vit
// ici pour qu'une route ajoutée ou modifiée garde le contrat HTTP des autres. Chaque fabrique
// retourne null quand elle ne reconnaît pas l'exception, ce qui la laisse remonter au framework.
internal static class HttpProblems
{
    // Échecs des moteurs d'extraction : ils décrivent une entrée que l'appelant contrôle, sauf le
    // moteur dont les modèles manquent, qui est un défaut de déploiement.
    public static ProblemDetails? ForExtraction(Exception exception) => exception switch
    {
        InvalidDataException => Create(
            StatusCodes.Status422UnprocessableEntity,
            "PDF invalide.",
            exception.Message),
        // PdfPig accepte l'en-tête puis échoue sur la structure : la signature validée en amont ne
        // garantit pas un document ouvrable.
        PdfDocumentFormatException => Create(
            StatusCodes.Status422UnprocessableEntity,
            "PDF invalide.",
            "Le fichier ne contient pas un PDF lisible."),
        ArgumentException => Create(StatusCodes.Status400BadRequest, "Moteur inconnu.", exception.Message),
        InvalidOperationException => Create(
            StatusCodes.Status503ServiceUnavailable,
            "Moteur indisponible.",
            exception.Message),
        _ => null
    };

    // Échecs de l'appel à Flowise. Une annulation demandée par le client n'en fait pas partie :
    // elle remonte telle quelle plutôt que de se faire passer pour un dépassement de délai.
    public static ProblemDetails? ForFlowise(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        InvalidDataException => Create(
            StatusCodes.Status502BadGateway,
            "Réponse Flowise invalide.",
            exception.Message),
        HttpRequestException => Create(
            StatusCodes.Status503ServiceUnavailable,
            "Flowise indisponible.",
            exception.Message),
        TaskCanceledException when !cancellationToken.IsCancellationRequested => Create(
            StatusCodes.Status504GatewayTimeout,
            "Flowise a dépassé le délai d'appel."),
        InvalidOperationException => Create(
            StatusCodes.Status503ServiceUnavailable,
            "Flowise non configuré.",
            exception.Message),
        _ => null
    };

    // Échecs du masquage et de la détection PII : sous-processus Presidio absent, non démarré ou
    // interrompu. Une exception d'un autre type est un bug et doit remonter tel quel au framework.
    public static ProblemDetails? ForPii(
        Exception exception,
        CancellationToken cancellationToken,
        string detail) => exception switch
    {
        OperationCanceledException when cancellationToken.IsCancellationRequested => null,
        InvalidDataException or InvalidOperationException or IOException or TimeoutException => Create(
            StatusCodes.Status503ServiceUnavailable,
            "Traitement PII indisponible.",
            detail),
        _ => null
    };

    private static ProblemDetails Create(int status, string title, string? detail = null) => new()
    {
        Status = status,
        Title = title,
        Detail = detail
    };
}
