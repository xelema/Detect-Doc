using System.Diagnostics;
using System.Text.Json;
using DetectDoc.Core.Classification;
using DetectDoc.Core.Engines;
using DetectDoc.Infrastructure.Engines;
using Microsoft.AspNetCore.Mvc;

namespace DetectDoc.Api.Endpoints;

public static class ClassificationEndpoints
{
    public static void MapClassificationEndpoints(this WebApplication app)
    {
        app.MapPost("/api/documents/classify", async Task<IResult> (
            IFormFile? file,
            [FromForm] string? types,
            [FromForm] string? engine,
            PdfPigNativeTextEngine pdfPig,
            IServiceProvider services,
            IClassifieurDocument classifieur,
            CancellationToken cancellationToken) =>
        {
            var total = Stopwatch.StartNew();
            if (file is null || file.Length == 0 || string.IsNullOrWhiteSpace(types))
            {
                return Results.Problem(
                    title: "Requête invalide.",
                    detail: "Un fichier et une liste de types sont requis.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            string[]? typesDemandes;
            try
            {
                typesDemandes = JsonSerializer.Deserialize<string[]>(types);
            }
            catch (JsonException) when (!types.TrimStart().StartsWith('['))
            {
                // Swagger poste la liste séparée par des virgules plutôt qu'un tableau JSON.
                typesDemandes = types.Split(',', StringSplitOptions.TrimEntries)
                    .Select(type => type.Trim('"'))
                    .ToArray();
            }
            catch (JsonException)
            {
                return Results.Problem(
                    title: "Requête invalide.",
                    detail: "Le champ types doit être un tableau JSON ou une liste séparée par des virgules.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var typesValides = DocumentTypes.Normalize(typesDemandes);
            if (typesValides is null)
            {
                return Results.Problem(
                    title: "Requête invalide.",
                    detail: "Les types doivent être non vides et uniques.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            // L'extraction et l'appel à Flowise échouent pour des raisons distinctes : un PDF
            // illisible est une erreur de l'appelant, une réponse Flowise absente est une panne
            // amont. Les deux blocs restent séparés pour que chaque cause garde son statut.
            IReadOnlyList<ResultatExtractionPage> pages;
            string moteurEffectif;
            var extraction = Stopwatch.StartNew();
            try
            {
                using var flux = file.OpenReadStream();
                switch (engine?.ToLowerInvariant())
                {
                    case null or "" or "auto":
                        var extractionAutomatique = services.GetRequiredService<ExtractionAutomatique>()
                            .Extract(flux, cancellationToken);
                        pages = extractionAutomatique.Pages;
                        moteurEffectif = extractionAutomatique.Moteur;
                        break;
                    case "pdfpig":
                        pages = pdfPig.Extract(flux, cancellationToken);
                        moteurEffectif = "pdfpig";
                        break;
                    case "ppocr-tiny":
                        pages = services.GetRequiredService<PpOcrEngine>().Extract(flux, cancellationToken);
                        moteurEffectif = "ppocr-tiny";
                        break;
                    default:
                        throw new ArgumentException("Le moteur doit être auto, pdfpig ou ppocr-tiny.", nameof(engine));
                }
            }
            catch (Exception exception) when (ProblemesHttp.PourExtraction(exception) is { } probleme)
            {
                return probleme;
            }

            extraction.Stop();

            var texte = string.Join(Environment.NewLine, pages.Select(page => page.Texte));
            if (string.IsNullOrWhiteSpace(texte))
            {
                return Results.Problem(
                    title: "Texte inexploitable.",
                    detail: "L'extraction ne retourne aucun texte.",
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            var classification = Stopwatch.StartNew();
            ResultatClassification resultat;
            try
            {
                resultat = await classifieur.ClasserAsync(texte, typesValides, cancellationToken);
            }
            catch (Exception exception) when (ProblemesHttp.PourFlowise(exception, cancellationToken) is { } probleme)
            {
                return probleme;
            }

            classification.Stop();
            total.Stop();

            return Results.Ok(new ResultatClassificationDocumentHttp(
                resultat.Type,
                resultat.Confiance,
                moteurEffectif,
                pages.Count,
                extraction.ElapsedMilliseconds,
                classification.ElapsedMilliseconds,
                total.ElapsedMilliseconds,
                resultat.ExecutionId));
        })
            .DisableAntiforgery()
            .WithSummary("Extrait puis classe un PDF avec le moteur choisi.")
            .Produces<ResultatClassificationDocumentHttp>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);
    }
}
