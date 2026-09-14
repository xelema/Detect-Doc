using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DetectDoc.Tests;

// Les tests mutent PII_PYTHON : la variable est lue à la résolution du détecteur, et xUnit
// n'exécute jamais en parallèle deux tests d'une même classe.
public class PresidioPiiDetectorTests
{
    [Fact]
    public async Task Detect_SansExecutablePython_Retourne503()
    {
        using var restore = PiiPython(@"C:\chemin\qui\nexiste\pas\python.exe");
        await using var application = CreerApplication();
        using var client = application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/dev/pii/detect",
            new { text = "Contact : Jean Dupont" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Detect_SortieDeDemarrageNonJson_Retourne503()
    {
        // robocopy affiche son usage sur stdout quelle que soit la ligne de commande reçue :
        // la première ligne du handshake n'est donc jamais du JSON.
        using var restore = PiiPython(@"C:\Windows\System32\Robocopy.exe");
        await using var application = CreerApplication();
        using var client = application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/dev/pii/detect",
            new { text = "Contact : Jean Dupont" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static WebApplicationFactory<Program> CreerApplication() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureLogging(logging => logging.ClearProviders());
            });

    private static IDisposable PiiPython(string chemin)
    {
        var precedent = Environment.GetEnvironmentVariable("PII_PYTHON");
        Environment.SetEnvironmentVariable("PII_PYTHON", chemin);
        return new Restauration(precedent);
    }

    private sealed class Restauration(string? precedent) : IDisposable
    {
        public void Dispose() => Environment.SetEnvironmentVariable("PII_PYTHON", precedent);
    }
}
