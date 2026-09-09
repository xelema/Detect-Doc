using DetectDoc.Core.Classification;
using DetectDoc.Core.Engines;
using DetectDoc.Infrastructure.Engines;

namespace DetectDoc.Api.Endpoints;

// Routes de diagnostic exposées en Development uniquement : elles servent aux essais
// moteur par moteur, sans passer par la chaîne complète de classification.
public static class DevEndpoints
{
    public static void MapDevEndpoints(this WebApplication app)
    {
        app.MapPost("/api/dev/classify", async Task<IResult> (
            RequeteClassificationHttp? requete,
            IClassifieurDocument classifieur,
            CancellationToken cancellationToken) =>
        {
            var types = DocumentTypes.Normalize(requete?.Types);
            if (string.IsNullOrWhiteSpace(requete?.Texte) || types is null)
            {
                return Results.Problem(
                    title: "Requête invalide.",
                    detail: "Un texte et une liste de types non vides et sans doublon sont requis.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                return Results.Ok(await classifieur.ClasserAsync(requete.Texte, types, cancellationToken));
            }
            catch (Exception exception) when (ProblemesHttp.PourFlowise(exception, cancellationToken) is { } probleme)
            {
                return probleme;
            }
        })
            .WithSummary("Classifie un texte avec Flowise.")
            .Produces<ResultatClassification>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        app.MapPost("/api/dev/extract/{engine}", IResult (
            string engine,
            IFormFile? file,
            PdfPigNativeTextEngine pdfPig,
            IServiceProvider services,
            CancellationToken cancellationToken) =>
        {
            if (file is null || file.Length == 0)
            {
                return Results.Problem(
                    title: "Fichier invalide.",
                    detail: "Un fichier PDF non vide est requis.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                ITextExtractionEngine extracteur = engine.ToLowerInvariant() switch
                {
                    "pdfpig" => pdfPig,
                    "ppocr-tiny" => services.GetRequiredService<PpOcrEngine>(),
                    _ => throw new ArgumentException("Le moteur doit être pdfpig ou ppocr-tiny.", nameof(engine))
                };
                using var flux = file.OpenReadStream();
                var pages = extracteur.Extract(flux, cancellationToken)
                    .Select(page => new ResultatExtractionPageHttp(
                        page.NumeroPage,
                        page.Texte,
                        page.Confiance));

                return Results.Ok(pages);
            }
            catch (Exception exception) when (ProblemesHttp.PourExtraction(exception) is { } probleme)
            {
                return probleme;
            }
        })
            .DisableAntiforgery()
            .WithSummary("Extrait le texte d'un PDF avec le moteur choisi.")
            .Produces<IEnumerable<ResultatExtractionPageHttp>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }
}
