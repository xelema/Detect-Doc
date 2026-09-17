using System.Net;
using DetectDoc.Api;
using DetectDoc.Api.Endpoints;
using DetectDoc.Infrastructure.Pii;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddDetectDoc(builder.Configuration, builder.Environment);

// Convention ValiAPI : Traefik termine le TLS en recette et sans X-Forwarded-Proto
// honoré, l'OpenAPI annonce http://, bloqué en contenu mixte par Firefox. Les proxies
// de confiance se déclarent par les variables ReverseProxy__*.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = builder.Configuration.GetValue<int?>("ReverseProxy:ForwardLimit") ?? 1;

    if (builder.Configuration.GetValue<bool>("ReverseProxy:TrustAllProxies"))
    {
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    }

    foreach (var knownProxy in builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
    {
        if (IPAddress.TryParse(knownProxy, out var address))
        {
            options.KnownProxies.Add(address);
        }
    }

    foreach (var knownNetwork in builder.Configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [])
    {
        var parts = knownNetwork.Split('/', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2
            && IPAddress.TryParse(parts[0], out var prefix)
            && int.TryParse(parts[1], out var prefixLength))
        {
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(prefix, prefixLength));
        }
    }
});

var app = builder.Build();

app.UseForwardedHeaders();

// Le statut reflète l'état Presidio : un budget de relances épuisé laisse la classification en
// 503 permanente et la supervision doit le voir sans résoudre le détecteur (ce qui lancerait
// le sous-processus).
app.MapGet("/health", (PresidioHealth presidio) => presidio.Unrecoverable
        ? Results.Json(new { statut = "degraded" }, statusCode: StatusCodes.Status503ServiceUnavailable)
        : Results.Ok(new { statut = "ok" }))
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
