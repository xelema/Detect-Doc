using System.Diagnostics;
using System.Text.Json;
using DetectDoc.Core.Classification;
using DetectDoc.Core.Engines;
using DetectDoc.Core.Pii;
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
            IDocumentClassifier classifieur,
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

            var resultat = await ClassifyDocumentAsync(
                file,
                typesValides!,
                engine,
                pdfPig,
                services,
                classifieur,
                cancellationToken);
            return resultat.Erreur is null
                ? Results.Ok(resultat.Resultat)
                : Results.Problem(resultat.Erreur);
        })
            .DisableAntiforgery()
            .WithSummary("Extrait, masque les PII puis classe un PDF avec le moteur choisi.")
            .Produces<ResultatClassificationDocumentHttp>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        app.MapPost("/api/documents/classify-batch", async Task<IResult> (
            [FromForm] IFormFileCollection? files,
            [FromForm] string? types,
            [FromForm] string? engine,
            PdfPigNativeTextEngine pdfPig,
            IServiceProvider services,
            IDocumentClassifier classifieur,
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
                var resultat = await ClassifyDocumentAsync(
                    file,
                    typesValides!,
                    engine,
                    pdfPig,
                    services,
                    classifieur,
                    cancellationToken);
                resultats.Add(new ResultatClassificationLotDocumentHttp(
                    file.FileName,
                    resultat.Resultat,
                    resultat.Erreur));
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

        List<TypeDocumentaire?>? typesDemandes;
        try
        {
            using var document = JsonDocument.Parse(types);
            // Deux formes admises : `["RIB","SEPA"]` ou
            // `[{"id":"RIB","libelle":"…","description":"…"}]`, les deux derniers champs étant facultatifs.
            typesDemandes = document.RootElement is { ValueKind: JsonValueKind.Array } tableau
                && tableau.EnumerateArray().All(element => element.ValueKind == JsonValueKind.String)
                ? tableau.EnumerateArray()
                    .Select(element => (TypeDocumentaire?)new TypeDocumentaire(element.GetString() ?? "", null, null))
                    .ToList()
                : document.RootElement.Deserialize<List<TypeDocumentaire?>>(JsonSerializerOptions.Web);
        }
        catch (JsonException) when (!types.TrimStart().StartsWith('['))
        {
            // Swagger poste la liste séparée par des virgules plutôt qu'un tableau JSON.
            typesDemandes = types.Split(',', StringSplitOptions.TrimEntries)
                .Select(type => (TypeDocumentaire?)new TypeDocumentaire(type.Trim('"'), null, null))
                .ToList();
        }
        catch (JsonException)
        {
            return (null, Results.Problem(
                title: "Requête invalide.",
                detail: "Le champ types doit être un tableau JSON d'identifiants ou d'objets, ou une liste séparée par des virgules.",
                statusCode: StatusCodes.Status400BadRequest));
        }

        var typesValides = typesDemandes is null ? null : DocumentTypes.Normalize(typesDemandes);
        return typesValides is null
            ? (null, Results.Problem(
                title: "Requête invalide.",
                detail: "Les types doivent porter un id non vide et unique.",
                statusCode: StatusCodes.Status400BadRequest))
            : (typesValides, null);
    }

    private static async Task<ClassificationOutcome> ClassifyDocumentAsync(
        IFormFile file,
        IReadOnlyCollection<TypeDocumentaire> types,
        string? engine,
        PdfPigNativeTextEngine pdfPig,
        IServiceProvider services,
        IDocumentClassifier classifieur,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return new(null, new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Fichier vide.",
                Detail = "Le fichier ne contient aucune donnée."
            });
        }

        var total = Stopwatch.StartNew();
        IReadOnlyList<PageExtractionResult> pages;
        string moteurEffectif;
        var extraction = Stopwatch.StartNew();
        try
        {
            await using var flux = file.OpenReadStream();
            switch (engine?.ToLowerInvariant())
            {
                case null or "" or "auto":
                    var extractionAutomatique = services.GetRequiredService<AutomaticExtraction>()
                        .Extract(flux, cancellationToken);
                    pages = extractionAutomatique.Pages;
                    moteurEffectif = extractionAutomatique.Moteur;
                    break;
                case "pdfpig":
                    pages = pdfPig.Extract(flux, cancellationToken);
                    moteurEffectif = pdfPig.Name;
                    break;
                case "ppocr-tiny":
                    var ppOcr = services.GetRequiredService<PpOcrEngine>();
                    pages = ppOcr.Extract(flux, cancellationToken);
                    moteurEffectif = ppOcr.Name;
                    break;
                default:
                    throw new ArgumentException("Le moteur doit être auto, pdfpig ou ppocr-tiny.", nameof(engine));
            }
        }
        catch (Exception exception) when (HttpProblems.ForExtraction(exception) is { } probleme)
        {
            return new(null, probleme);
        }

        extraction.Stop();
        var texte = string.Join(Environment.NewLine, pages.Select(page => page.Texte));
        if (string.IsNullOrWhiteSpace(texte))
        {
            return new(null, new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Texte inexploitable.",
                Detail = "L'extraction ne retourne aucun texte."
            });
        }

        var piiMasking = Stopwatch.StartNew();
        string maskedText;
        try
        {
            var piiTextMasker = services.GetRequiredService<PiiTextMasker>();
            maskedText = await piiTextMasker.MaskAsync(texte, cancellationToken);
        }
        catch (Exception exception) when (HttpProblems.ForPii(
            exception, cancellationToken, "La détection et le masquage des données personnelles n'ont pas abouti.") is { } probleme)
        {
            return new(null, probleme);
        }

        piiMasking.Stop();
        var classification = Stopwatch.StartNew();
        ClassificationResult resultat;
        try
        {
            resultat = await classifieur.ClassifyAsync(maskedText, types, cancellationToken);
        }
        catch (Exception exception) when (HttpProblems.ForFlowise(exception, cancellationToken) is { } probleme)
        {
            return new(null, probleme);
        }

        classification.Stop();
        total.Stop();
        return new(new ResultatClassificationDocumentHttp(
            resultat.Type,
            resultat.Confiance,
            moteurEffectif,
            pages.Count,
            extraction.ElapsedMilliseconds,
            piiMasking.ElapsedMilliseconds,
            classification.ElapsedMilliseconds,
            total.ElapsedMilliseconds,
            resultat.ExecutionId), null);
    }

    private sealed record ClassificationOutcome(
        ResultatClassificationDocumentHttp? Resultat,
        ProblemDetails? Erreur);
}
