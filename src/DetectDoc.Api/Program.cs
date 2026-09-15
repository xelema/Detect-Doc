using DetectDoc.Api;
using DetectDoc.Api.Endpoints;
using DetectDoc.Infrastructure.Pii;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddDetectDoc(builder.Configuration, builder.Environment);

var app = builder.Build();

// Le statut reflète l'état Presidio : un budget de relances épuisé laisse la classification en
// 503 permanente et la supervision doit le voir sans résoudre le détecteur (ce qui lancerait
// le sous-processus).
app.MapGet("/health", (PresidioHealth presidio) => presidio.Unrecoverable
        ? Results.Json(new { statut = "degraded" }, statusCode: StatusCodes.Status503ServiceUnavailable)
        : Results.Ok(new { statut = "ok" }))
    .WithSummary("Vérifie que le service répond.");
app.MapClassificationEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapDevEndpoints();
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
        options.SwaggerEndpoint("../openapi/v1.json", "Detect-Doc v1"));
}

app.Run();

// Rend le point d'entrée accessible aux tests HTTP avec WebApplicationFactory.
public partial class Program { }
