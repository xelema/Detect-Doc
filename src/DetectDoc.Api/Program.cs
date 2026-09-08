using DetectDoc.Core.Classification;
using DetectDoc.Core.Engines;
using DetectDoc.Infrastructure.Classification;
using DetectDoc.Infrastructure.Engines;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Text.Json;
using UglyToad.PdfPig.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
var repertoireModelesPpOcr = builder.Configuration["PpOcr:ModelsDirectory"]
    ?? Path.Combine(AppContext.BaseDirectory, "models");
var reconnaissancesParallelesPpOcr = int.TryParse(
    builder.Configuration["PpOcr:ReconnaissancesParalleles"],
    out var valeurConfiguree)
    && valeurConfiguree > 0
    ? valeurConfiguree
    : 1;
var accepterCertificatFlowiseInvalide = builder.Configuration.GetValue<bool>("Flowise:AllowInvalidTls");
if (accepterCertificatFlowiseInvalide && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("Flowise:AllowInvalidTls est réservé à Development.");
}

builder.Services.AddSingleton<PdfPigNativeTextEngine>();
builder.Services.AddSingleton(_ => new PpOcrEngine(
    "tiny",
    repertoireModelesPpOcr,
    reconnaissancesParallelesPpOcr));
builder.Services.AddSingleton(services => new ExtractionAutomatique(
    services.GetRequiredService<PdfPigNativeTextEngine>(),
    services.GetRequiredService<PpOcrEngine>()));
builder.Services.AddSingleton(new FlowiseOptions(
    builder.Configuration["Flowise:BaseUrl"],
    builder.Configuration["Flowise:FlowId"]));
builder.Services.AddHttpClient<IClassifieurDocument, FlowiseDocumentClassifier>(client =>
        client.Timeout = TimeSpan.FromSeconds(60))
    .ConfigurePrimaryHttpMessageHandler(() => accepterCertificatFlowiseInvalide
        ? new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        }
        : new HttpClientHandler());

var app = builder.Build();

app.MapGet("/health", () => TypedResults.Ok(new { statut = "ok" }))
    .WithSummary("Vérifie que le service répond.");

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
        return Results.Problem(title: "Requête invalide.", detail: "Un fichier et une liste de types sont requis.", statusCode: 400);
    }
    string[]? typesDocumentaires;
    try
    {
        typesDocumentaires = JsonSerializer.Deserialize<string[]>(types);
    }
    catch (JsonException) when (!types.TrimStart().StartsWith('['))
    {
        typesDocumentaires = types.Split(',', StringSplitOptions.TrimEntries);
    }
    catch (JsonException)
    {
        return Results.Problem(title: "Requête invalide.", detail: "Le champ types doit être un tableau JSON ou une liste séparée par des virgules.", statusCode: 400);
    }

    var typesValides = typesDocumentaires?
        .Select(type => type.Trim())
        .Where(type => type.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .ToArray();
    if (typesValides is null || typesValides.Length == 0 || typesValides.Length != typesDocumentaires!.Length)
    {
        return Results.Problem(title: "Requête invalide.", detail: "Les types doivent être non vides et uniques.", statusCode: 400);
    }

    try
    {
        using var flux = file.OpenReadStream();
        var extraction = Stopwatch.StartNew();
        IReadOnlyList<ResultatExtractionPage> pages;
        string moteurEffectif;
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
        extraction.Stop();
        var texte = string.Join(Environment.NewLine, pages.Select(page => page.Texte));
        if (string.IsNullOrWhiteSpace(texte))
        {
            return Results.Problem(title: "Texte inexploitable.", detail: "L'extraction ne retourne aucun texte.", statusCode: 422);
        }

        var classification = Stopwatch.StartNew();
        var resultat = await classifieur.ClasserAsync(texte, typesValides, cancellationToken);
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
    }
    catch (ArgumentException exception)
    {
        return Results.Problem(title: "Requête invalide.", detail: exception.Message, statusCode: 400);
    }
    catch (InvalidDataException exception)
    {
        return Results.Problem(title: "Réponse Flowise invalide.", detail: exception.Message, statusCode: 502);
    }
    catch (HttpRequestException exception)
    {
        return Results.Problem(title: "Flowise indisponible.", detail: exception.Message, statusCode: 503);
    }
    catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Problem(
            title: "Flowise a dépassé le délai d'appel.",
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
    catch (InvalidOperationException exception)
    {
        return Results.Problem(title: "Service indisponible.", detail: exception.Message, statusCode: 503);
    }
})
    .DisableAntiforgery()
    .WithSummary("Extrait puis classe un PDF avec le moteur choisi.")
    .Produces<ResultatClassificationDocumentHttp>()
    .ProducesProblem(400)
    .ProducesProblem(422)
    .ProducesProblem(502)
    .ProducesProblem(503);

if (app.Environment.IsDevelopment())
{
    app.MapPost("/api/dev/classify", async Task<IResult> (
        RequeteClassificationHttp? requete,
        IClassifieurDocument classifieur,
        CancellationToken cancellationToken) =>
    {
        var types = requete?.Types?
            .Select(type => type.Trim())
            .Where(type => type.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (string.IsNullOrWhiteSpace(requete?.Texte)
            || types is null
            || types.Length == 0
            || types.Length != requete.Types!.Length)
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
        catch (InvalidDataException exception)
        {
            return Results.Problem(
                title: "Réponse Flowise invalide.",
                detail: exception.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }
        catch (HttpRequestException exception)
        {
            return Results.Problem(
                title: "Flowise indisponible.",
                detail: exception.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(
                title: "Flowise non configuré.",
                detail: exception.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
        .WithSummary("Classifie un texte avec Flowise.")
        .Produces<ResultatClassification>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status502BadGateway)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

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
        catch (InvalidDataException exception)
        {
            return Results.Problem(
                title: "PDF invalide.",
                detail: exception.Message,
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
        catch (PdfDocumentFormatException)
        {
            return Results.Problem(
                title: "PDF invalide.",
                detail: "Le fichier ne contient pas un PDF lisible.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
        catch (ArgumentException exception)
        {
            return Results.Problem(
                title: "Moteur inconnu.",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(
                title: "Moteur indisponible.",
                detail: exception.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
        .DisableAntiforgery()
        .WithSummary("Extrait le texte d'un PDF avec le moteur choisi.")
        .Produces<IEnumerable<ResultatExtractionPageHttp>>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    app.MapOpenApi();
    app.UseSwaggerUI(options =>
        options.SwaggerEndpoint("../openapi/v1.json", "Detect-Doc v1"));
}

app.Run();

// Rend le point d'entrée accessible aux tests HTTP avec WebApplicationFactory.
public partial class Program { }

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
