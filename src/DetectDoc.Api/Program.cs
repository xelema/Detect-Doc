using DetectDoc.Core.Engines;
using DetectDoc.Infrastructure.Engines;
using UglyToad.PdfPig.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddSingleton<ITextExtractionEngine, PdfPigNativeTextEngine>();

var app = builder.Build();

app.MapGet("/health", () => TypedResults.Ok(new { statut = "ok" }))
    .WithSummary("Vérifie que le service répond.");

if (app.Environment.IsDevelopment())
{
    app.MapPost("/api/dev/extract/{engine}", IResult (
        string engine,
        IFormFile? file,
        ITextExtractionEngine extracteur,
        CancellationToken cancellationToken) =>
    {
        if (!string.Equals(engine, "pdfpig", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem(
                title: "Moteur inconnu.",
                detail: "Le moteur doit être pdfpig.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (file is null || file.Length == 0)
        {
            return Results.Problem(
                title: "Fichier invalide.",
                detail: "Un fichier PDF non vide est requis.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
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
    })
        .DisableAntiforgery()
        .WithSummary("Extrait le texte d'un PDF avec PdfPig.")
        .Produces<IEnumerable<ResultatExtractionPageHttp>>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

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
