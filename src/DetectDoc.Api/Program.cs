using DetectDoc.Api;
using DetectDoc.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddDetectDoc(builder.Configuration, builder.Environment);

var app = builder.Build();

app.MapGet("/health", () => TypedResults.Ok(new { statut = "ok" }))
    .WithSummary("Vérifie que le service répond.");
app.MapClassificationEndpoints();

// La surface de diagnostic est exposée en Development comme en Recette : routes
// /api/dev/*, OpenAPI, Swagger UI et page d'exception détaillée.
if (app.Environment.EnvironmentName is "Development" or "Recette")
{
    app.MapDevEndpoints();
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
        options.SwaggerEndpoint("../openapi/v1.json", "Detect-Doc v1"));
    // En Development le framework l'a déjà inscrite en tête de pipeline ; l'appel
    // explicite sert à Recette.
    app.UseDeveloperExceptionPage();
}

app.Run();

// Rend le point d'entrée accessible aux tests HTTP avec WebApplicationFactory.
public partial class Program { }
