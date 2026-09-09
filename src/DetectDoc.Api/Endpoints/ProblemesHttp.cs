using UglyToad.PdfPig.Core;

namespace DetectDoc.Api.Endpoints;

// Les routes de classification et d'extraction rencontrent les mêmes exceptions : le mapping vit
// ici pour qu'une route ajoutée ou modifiée garde le contrat HTTP des autres. Chaque fabrique
// retourne null quand elle ne reconnaît pas l'exception, ce qui la laisse remonter au framework.
internal static class HttpProblems
{
    // Échecs des moteurs d'extraction : ils décrivent une entrée que l'appelant contrôle, sauf le
    // moteur dont les modèles manquent, qui est un défaut de déploiement.
    public static IResult? ForExtraction(Exception exception) => exception switch
    {
        InvalidDataException => Results.Problem(
            title: "PDF invalide.",
            detail: exception.Message,
            statusCode: StatusCodes.Status422UnprocessableEntity),
        // PdfPig accepte l'en-tête puis échoue sur la structure : la signature validée en amont ne
        // garantit pas un document ouvrable.
        PdfDocumentFormatException => Results.Problem(
            title: "PDF invalide.",
            detail: "Le fichier ne contient pas un PDF lisible.",
            statusCode: StatusCodes.Status422UnprocessableEntity),
        ArgumentException => Results.Problem(
            title: "Moteur inconnu.",
            detail: exception.Message,
            statusCode: StatusCodes.Status400BadRequest),
        InvalidOperationException => Results.Problem(
            title: "Moteur indisponible.",
            detail: exception.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => null
    };

    // Échecs de l'appel à Flowise. Une annulation demandée par le client n'en fait pas partie :
    // elle remonte telle quelle plutôt que de se faire passer pour un dépassement de délai.
    public static IResult? ForFlowise(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        InvalidDataException => Results.Problem(
            title: "Réponse Flowise invalide.",
            detail: exception.Message,
            statusCode: StatusCodes.Status502BadGateway),
        HttpRequestException => Results.Problem(
            title: "Flowise indisponible.",
            detail: exception.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable),
        TaskCanceledException when !cancellationToken.IsCancellationRequested => Results.Problem(
            title: "Flowise a dépassé le délai d'appel.",
            statusCode: StatusCodes.Status504GatewayTimeout),
        InvalidOperationException => Results.Problem(
            title: "Flowise non configuré.",
            detail: exception.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => null
    };
}
