using System.Text.Json;
using DetectDoc.Core.Classification;
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
            DocumentClassification classification,
            CancellationToken cancellationToken) =>
        {
            if (file is null || file.Length == 0)
            {
                return Results.Problem(
                    title: "Requête invalide.",
                    detail: "Un fichier et une liste de types sont requis.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var (typesValides, erreurTypes) = ParseTypes(types);
            if (erreurTypes is not null)
            {
                return erreurTypes;
            }

            await using var flux = file.OpenReadStream();
            var outcome = await classification.ExecuteAsync(
                flux, typesValides!, engine, cancellationToken);
            return outcome is DocumentClassificationOutcome.Success success
                ? Results.Ok(success.Result)
                : Results.Problem(HttpProblems.ForOutcome(outcome, cancellationToken));
        })
            .DisableAntiforgery()
            .WithSummary("Extrait, masque les PII puis classe un PDF avec le moteur choisi.")
            .Produces<DocumentClassificationResult>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        app.MapPost("/api/documents/classify-batch", async Task<IResult> (
            [FromForm] IFormFileCollection? files,
            [FromForm] string? types,
            [FromForm] string? engine,
            DocumentClassification classification,
            CancellationToken cancellationToken) =>
        {
            if (files is null || files.Count == 0)
            {
                return Results.Problem(
                    title: "Requête invalide.",
                    detail: "Au moins un fichier et une liste de types sont requis.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var (typesValides, erreurTypes) = ParseTypes(types);
            if (erreurTypes is not null)
            {
                return erreurTypes;
            }

            var resultats = new List<ResultatClassificationLotDocumentHttp>(files.Count);
            foreach (var file in files)
            {
                if (file.Length == 0)
                {
                    resultats.Add(new ResultatClassificationLotDocumentHttp(
                        file.FileName,
                        null,
                        new ProblemDetails
                        {
                            Status = StatusCodes.Status400BadRequest,
                            Title = "Fichier vide.",
                            Detail = "Le fichier ne contient aucune donnée."
                        }));
                    continue;
                }

                await using var flux = file.OpenReadStream();
                var outcome = await classification.ExecuteAsync(
                    flux, typesValides!, engine, cancellationToken);
                resultats.Add(outcome is DocumentClassificationOutcome.Success success
                    ? new ResultatClassificationLotDocumentHttp(file.FileName, success.Result, null)
                    : new ResultatClassificationLotDocumentHttp(
                        file.FileName,
                        null,
                        HttpProblems.ForOutcome(outcome, cancellationToken)));
            }

            return Results.Ok(resultats);
        })
            .DisableAntiforgery()
            .WithSummary("Extrait, masque les PII puis classe plusieurs PDF indépendamment.")
            .Produces<ResultatClassificationLotDocumentHttp[]>()
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static (IReadOnlyCollection<TypeDocumentaire>? Types, IResult? Erreur) ParseTypes(string? types)
    {
        if (string.IsNullOrWhiteSpace(types))
        {
            return (null, Results.Problem(
                title: "Requête invalide.",
                detail: "Un fichier et une liste de types sont requis.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        // La forme se choisit au premier caractère, jamais via le filtre d'un catch : un JSON
        // valide qui n'est pas un tableau (objet, scalaire) doit être une 400, pas une liste
        // virgules dont les éléments deviendraient des identifiants absurdes envoyés au flow.
        var formeJson = types.TrimStart() is ['[', ..] or ['{', ..];

        List<TypeDocumentaire?>? typesDemandes;
        try
        {
            using var document = JsonDocument.Parse(types);
            if (document.RootElement.ValueKind is not JsonValueKind.Array)
            {
                return (null, TypesInvalides());
            }

            var tableau = document.RootElement;
            // Deux formes admises : `["RIB","SEPA"]` ou
            // `[{"id":"RIB","libelle":"…","description":"…"}]`, les deux derniers champs étant facultatifs.
            typesDemandes = tableau.EnumerateArray().All(element => element.ValueKind == JsonValueKind.String)
                ? tableau.EnumerateArray()
                    .Select(element => (TypeDocumentaire?)new TypeDocumentaire(element.GetString() ?? "", null, null))
                    .ToList()
                : tableau.Deserialize<List<TypeDocumentaire?>>(JsonSerializerOptions.Web);
        }
        catch (JsonException) when (!formeJson)
        {
            // Swagger poste la liste séparée par des virgules plutôt qu'un tableau JSON.
            typesDemandes = types.Split(',', StringSplitOptions.TrimEntries)
                .Select(type => (TypeDocumentaire?)new TypeDocumentaire(type.Trim('"'), null, null))
                .ToList();
        }
        catch (JsonException)
        {
            return (null, TypesInvalides());
        }

        var typesValides = typesDemandes is null ? null : DocumentTypes.Normalize(typesDemandes);
        return typesValides is null
            ? (null, Results.Problem(
                title: "Requête invalide.",
                detail: "Les types doivent porter un id non vide et unique.",
                statusCode: StatusCodes.Status400BadRequest))
            : (typesValides, null);
    }

    private static IResult TypesInvalides() => Results.Problem(
        title: "Requête invalide.",
        detail: "Le champ types doit être un tableau JSON d'identifiants ou d'objets, ou une liste séparée par des virgules.",
        statusCode: StatusCodes.Status400BadRequest);
}
