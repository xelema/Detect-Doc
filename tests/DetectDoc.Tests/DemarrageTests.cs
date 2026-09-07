using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DetectDoc.Tests;

public class DemarrageTests
{
    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    public async Task ExposeLesRoutesSelonEnvironnement(string environnement, bool documentationExposee)
    {
        await using var application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment(environnement));
        using var client = application.CreateClient();

        using var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("application/json", health.Content.Headers.ContentType?.MediaType);
        using var etat = JsonDocument.Parse(await health.Content.ReadAsStringAsync());
        Assert.Equal("ok", etat.RootElement.GetProperty("statut").GetString());

        using var openApi = await client.GetAsync("/openapi/v1.json");
        using var swagger = await client.GetAsync("/swagger/index.html");
        var statutDocumentation = documentationExposee ? HttpStatusCode.OK : HttpStatusCode.NotFound;
        Assert.Equal(statutDocumentation, openApi.StatusCode);
        Assert.Equal(statutDocumentation, swagger.StatusCode);

        if (documentationExposee)
        {
            using var contrat = JsonDocument.Parse(await openApi.Content.ReadAsStringAsync());
            Assert.True(contrat.RootElement.GetProperty("paths").GetProperty("/health")
                .GetProperty("get").GetProperty("responses").TryGetProperty("200", out _));
            Assert.Equal("text/html", swagger.Content.Headers.ContentType?.MediaType);
            Assert.Contains("id=\"swagger-ui\"", await swagger.Content.ReadAsStringAsync());
        }
    }
}
