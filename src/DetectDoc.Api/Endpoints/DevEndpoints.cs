using DetectDoc.Core.Classification;
using DetectDoc.Core.Engines;
using DetectDoc.Core.Pii;
using DetectDoc.Infrastructure.Engines;

namespace DetectDoc.Api.Endpoints;

// Routes de diagnostic exposées en Development uniquement : elles servent aux essais
// moteur par moteur, sans passer par la chaîne complète de classification.
public static class DevEndpoints
{
    public static void MapDevEndpoints(this WebApplication app)
    {
        app.MapPost("/api/dev/pii/mask", async Task<IResult> (
            PiiDetectionHttpRequest? request,
            IServiceProvider services,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request?.Text))
            {
                return InvalidPiiRequest();
            }

            try
            {
                // Résolution dans le try : construire le détecteur lance Presidio, et un échec
                // doit rester une 503 de la route, pas une erreur de résolution hors du contrat.
                var masker = services.GetRequiredService<PiiTextMasker>();
                return Results.Ok(new PiiMaskingHttpResponse(
                    await masker.MaskAsync(request.Text, cancellationToken)));
            }
            catch (Exception exception) when (HttpProblems.ForPii(
                exception, cancellationToken, "Le masquage des données personnelles n'a pas abouti.") is { } probleme)
            {
                return Results.Problem(probleme);
            }
        })
            .WithSummary("Masque les données personnelles dans un texte.")
            .Produces<PiiMaskingHttpResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        app.MapPost("/api/dev/pii/detect", async Task<IResult> (
            PiiDetectionHttpRequest? request,
            IServiceProvider services,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request?.Text))
            {
                return InvalidPiiRequest();
            }

            try
            {
                var detector = services.GetRequiredService<IPiiDetector>();
                var detections = await detector.DetectAsync(request.Text, cancellationToken);
                // Le Select est matérialisé dans le try : sans ToList, un Substring sur une
                // plage invalide lèverait pendant l'écriture de la réponse, après le 200.
                return Results.Ok(detections.Select(detection => new PiiDetectionHttpResponse(
                    detection.Category.ToString(),
                    detection.Rule,
                    detection.Kind.ToString(),
                    detection.Start,
                    detection.Length,
                    request.Text.Substring(detection.Start, detection.Length))).ToList());
            }
            catch (Exception exception) when (HttpProblems.ForPii(
                exception, cancellationToken, "La détection des données personnelles n'a pas abouti.") is { } probleme)
            {
                return Results.Problem(probleme);
            }
        })
            .WithSummary("Détecte les données personnelles dans un texte.")
            .Produces<IEnumerable<PiiDetectionHttpResponse>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        app.MapPost("/api/dev/classify", async Task<IResult> (
            ClassificationHttpRequest? requete,
            IDocumentClassifier classifieur,
            CancellationToken cancellationToken) =>
        {
            var types = DocumentTypes.Normalize(requete?.Types?
                .Select(id => id is null ? null : new TypeDocumentaire(id, null, null))
                .ToList());
            if (string.IsNullOrWhiteSpace(requete?.Text) || types is null)
            {
                return Results.Problem(
                    title: "Requête invalide.",
                    detail: "Un texte et une liste de types non vides et sans doublon sont requis.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                return Results.Ok(await classifieur.ClassifyAsync(requete.Text, types, cancellationToken));
            }
            catch (Exception exception) when (HttpProblems.ForFlowise(exception, cancellationToken) is { } probleme)
            {
                return Results.Problem(probleme);
            }
        })
            .WithSummary("Classifie un texte avec Flowise.")
            .Produces<ClassificationResult>()
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
            catch (Exception exception) when (HttpProblems.ForExtraction(exception) is { } probleme)
            {
                return Results.Problem(probleme);
            }
        })
            .DisableAntiforgery()
            .WithSummary("Extrait le texte d'un PDF avec le moteur choisi.")
            .Produces<IEnumerable<ResultatExtractionPageHttp>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static IResult InvalidPiiRequest() => Results.Problem(
        title: "Requête invalide.",
        detail: "Un texte non vide est requis.",
        statusCode: StatusCodes.Status400BadRequest);
}

public sealed record PiiDetectionHttpRequest(string? Text);

public sealed record PiiMaskingHttpResponse(string Text);

public sealed record PiiDetectionHttpResponse(
    string Category,
    string Rule,
    string Kind,
    int Start,
    int Length,
    string Value);
