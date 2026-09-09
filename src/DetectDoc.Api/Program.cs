using DetectDoc.Api;
using DetectDoc.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddDetectDoc(builder.Configuration, builder.Environment);

var app = builder.Build();

app.MapGet("/health", () => TypedResults.Ok(new { statut = "ok" }))
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
