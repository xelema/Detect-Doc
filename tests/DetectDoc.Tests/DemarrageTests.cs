using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DetectDoc.Core.Classification;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig.Writer;
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

    [Fact]
    public async Task ExtraitUnPdfAvecPdfPigEnDevelopment()
    {
        await using var application = CreerApplication("Development");
        using var client = application.CreateClient();
        using var contenu = CreerPdfMultipart("Texte extrait depuis la route");

        using var response = await client.PostAsync("/api/dev/extract/pdfpig", contenu);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var resultat = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var page = Assert.Single(resultat.RootElement.EnumerateArray());
        Assert.Equal(1, page.GetProperty("numeroPage").GetInt32());
        Assert.Contains("Texte extrait depuis la route", page.GetProperty("texte").GetString());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("confianceOcr").ValueKind);

        using var openApi = await client.GetAsync("/openapi/v1.json");
        using var contrat = JsonDocument.Parse(await openApi.Content.ReadAsStringAsync());
        Assert.True(contrat.RootElement.GetProperty("paths")
            .TryGetProperty("/api/dev/extract/{engine}", out _));
    }

    [Fact]
    public async Task RefuseUnMoteurInconnu()
    {
        await using var application = CreerApplication("Development");
        using var client = application.CreateClient();
        using var contenu = CreerPdfMultipart("contenu");

        using var response = await client.PostAsync("/api/dev/extract/inconnu", contenu);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task SignalePpOcrQuandSesModelesSontAbsents()
    {
        await using var application = CreerApplication("Development");
        using var client = application.CreateClient();
        using var contenu = CreerPdfMultipart("contenu");

        using var response = await client.PostAsync("/api/dev/extract/ppocr-tiny", contenu);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefuseUnFichierAbsentOuVide(bool ajouterFichierVide)
    {
        await using var application = CreerApplication("Development");
        using var client = application.CreateClient();
        using var contenu = new MultipartFormDataContent();
        if (ajouterFichierVide)
        {
            contenu.Add(new ByteArrayContent([]), "file", "document.pdf");
        }

        using var response = await client.PostAsync("/api/dev/extract/pdfpig", contenu);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RetourneUnProblemDetailsPourUnPdfInvalide()
    {
        await using var application = CreerApplication("Development");
        using var client = application.CreateClient();
        using var contenu = new MultipartFormDataContent();
        contenu.Add(new ByteArrayContent("pas un PDF"u8.ToArray()), "file", "document.pdf");

        using var response = await client.PostAsync("/api/dev/extract/pdfpig", contenu);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var probleme = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("PDF invalide.", probleme.RootElement.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData(typeof(TaskCanceledException), HttpStatusCode.GatewayTimeout)]
    [InlineData(typeof(InvalidDataException), HttpStatusCode.BadGateway)]
    public async Task SignaleLesEchecsFlowise(Type typeException, HttpStatusCode statutAttendu)
    {
        var exception = (Exception)Activator.CreateInstance(typeException)!;
        await using var application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                    services.AddSingleton<IClassifieurDocument>(new ClassifieurEnEchec(exception)));
                builder.ConfigureLogging(logging => logging.ClearProviders());
            });
        using var client = application.CreateClient();
        using var contenu = CreerPdfMultipart("contenu");
        contenu.Add(new StringContent("[\"RIB\"]"), "types");
        contenu.Add(new StringContent("pdfpig"), "engine");

        using var response = await client.PostAsync("/api/documents/classify", contenu);

        Assert.Equal(statutAttendu, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task RefuseUnPdfSansTexteAvantDAppelerFlowise()
    {
        await using var application = CreerApplication("Development");
        using var client = application.CreateClient();
        using var contenu = CreerPdfMultipart(string.Empty);
        contenu.Add(new StringContent("[\"RIB\"]"), "types");
        contenu.Add(new StringContent("pdfpig"), "engine");

        using var response = await client.PostAsync("/api/documents/classify", contenu);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task NExposePasLaRouteDExtractionEnProduction()
    {
        await using var application = CreerApplication("Production");
        using var client = application.CreateClient();
        using var contenu = CreerPdfMultipart("contenu");

        using var response = await client.PostAsync("/api/dev/extract/pdfpig", contenu);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreerApplication(string environnement) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environnement);
                builder.ConfigureLogging(logging => logging.ClearProviders());
            });

    private static MultipartFormDataContent CreerPdfMultipart(string texte)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        if (texte.Length > 0)
        {
            page.AddText(
                texte,
                12,
                new UglyToad.PdfPig.Core.PdfPoint(50, 700),
                builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica));
        }

        var fichier = new ByteArrayContent(builder.Build());
        fichier.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var contenu = new MultipartFormDataContent();
        contenu.Add(fichier, "file", "document.pdf");
        return contenu;
    }

    private sealed class ClassifieurEnEchec(Exception exception) : IClassifieurDocument
    {
        public Task<ResultatClassification> ClasserAsync(
            string texte,
            IReadOnlyCollection<string> types,
            CancellationToken cancellationToken = default) => Task.FromException<ResultatClassification>(exception);
    }
}
