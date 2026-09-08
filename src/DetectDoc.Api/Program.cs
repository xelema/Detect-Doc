using DetectDoc.Core.Classification;
using DetectDoc.Core.Engines;
using DetectDoc.Infrastructure.Classification;
using DetectDoc.Infrastructure.Engines;
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
builder.Services.AddSingleton<PdfPigNativeTextEngine>();
builder.Services.AddSingleton(_ => new PpOcrEngine(
    "tiny",
    repertoireModelesPpOcr,
    reconnaissancesParallelesPpOcr));
builder.Services.AddSingleton(new FlowiseOptions(
    builder.Configuration["Flowise:BaseUrl"],
    builder.Configuration["Flowise:FlowId"]));
builder.Services.AddHttpClient<IClassifieurDocument, FlowiseDocumentClassifier>(client =>
    client.Timeout = TimeSpan.FromSeconds(60));

var app = builder.Build();

app.MapGet("/health", () => TypedResults.Ok(new { statut = "ok" }))
    .WithSummary("Vérifie que le service répond.");

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
