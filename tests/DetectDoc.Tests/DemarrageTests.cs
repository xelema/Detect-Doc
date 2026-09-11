using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DetectDoc.Core.Classification;
using DetectDoc.Core.Pii;
using DetectDoc.Infrastructure.Classification;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    [InlineData("[\"RIB\"]", typeof(TaskCanceledException), HttpStatusCode.GatewayTimeout)]
    [InlineData("DEVIS,FACTURE,RIB,FEUILLE_EMAR", typeof(InvalidDataException), HttpStatusCode.BadGateway)]
    public async Task SignaleLesEchecsFlowise(string types, Type typeException, HttpStatusCode statutAttendu)
    {
        var exception = (Exception)Activator.CreateInstance(typeException)!;
        await using var application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IDocumentClassifier>(new ClassifieurEnEchec(exception));
                    services.AddSingleton<IPiiDetector>(new PiiDetectorVide());
                });
                builder.ConfigureLogging(logging => logging.ClearProviders());
            });
        using var client = application.CreateClient();
        using var contenu = CreerPdfMultipart("contenu");
        contenu.Add(new StringContent(types), "types");
        contenu.Add(new StringContent("pdfpig"), "engine");

        using var response = await client.PostAsync("/api/documents/classify", contenu);

        Assert.Equal(statutAttendu, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("[\"RIB\", null]")]
    public async Task RefuseUnTypeNullDansLaListe(string types)
    {
        await using var application = CreerApplication("Development");
        using var client = application.CreateClient();
        using var contenu = CreerPdfMultipart("contenu");
        contenu.Add(new StringContent(types), "types");
        contenu.Add(new StringContent("pdfpig"), "engine");

        using var response = await client.PostAsync("/api/documents/classify", contenu);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task SignaleUnPdfInvalideSansLImputerAFlowise()
    {
        var classifieur = new ClassifieurDeTest((_, _) => new ClassificationResult("RIB", 0.9));
        await using var application = CreerApplication("Development", classifieur);
        using var client = application.CreateClient();
        using var contenu = new MultipartFormDataContent();
        contenu.Add(new ByteArrayContent("pas un PDF"u8.ToArray()), "file", "document.pdf");
        contenu.Add(new StringContent("[\"RIB\"]"), "types");
        contenu.Add(new StringContent("pdfpig"), "engine");

        using var response = await client.PostAsync("/api/documents/classify", contenu);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var probleme = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("PDF invalide.", probleme.RootElement.GetProperty("title").GetString());
        Assert.Equal(0, classifieur.Appels);
    }

    [Fact]
    public async Task SignaleUnPdfTronqueSurLaRouteDeClassification()
    {
        var classifieur = new ClassifieurDeTest((_, _) => new ClassificationResult("RIB", 0.9));
        await using var application = CreerApplication("Development", classifieur);
        using var client = application.CreateClient();
        using var contenu = new MultipartFormDataContent();
        // L'en-tête reste intact : seule la structure du document est coupée.
        contenu.Add(new ByteArrayContent(PdfDeTest.Construire("contenu")[..64]), "file", "document.pdf");
        contenu.Add(new StringContent("[\"RIB\"]"), "types");
        contenu.Add(new StringContent("pdfpig"), "engine");

        using var response = await client.PostAsync("/api/documents/classify", contenu);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var probleme = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("PDF invalide.", probleme.RootElement.GetProperty("title").GetString());
        Assert.Equal(0, classifieur.Appels);
    }

    [Fact]
    public async Task ClassifieEnAutoSansLesModelesOcr()
    {
        var classifieur = new ClassifieurDeTest((_, _) => new ClassificationResult("RIB", 0.9));
        await using var application = CreerApplication("Development", classifieur);
        using var client = application.CreateClient();
        using var contenu = CreerPdfMultipart("Texte natif lisible");
        contenu.Add(new StringContent("[\"RIB\"]"), "types");
        contenu.Add(new StringContent("auto"), "engine");

        using var response = await client.PostAsync("/api/documents/classify", contenu);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var resultat = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("pdfpig", resultat.RootElement.GetProperty("moteur").GetString());
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("pdfpig")]
    public async Task RendLeMemeIdentifiantDeMoteurQuelQueSoitLeModeDeSelection(string engine)
    {
        var classifieur = new ClassifieurDeTest((_, _) => new ClassificationResult("RIB", 0.9));
        await using var application = CreerApplication("Development", classifieur);
        using var client = application.CreateClient();
        using var contenu = CreerPdfMultipart("Texte natif lisible");
        contenu.Add(new StringContent("[\"RIB\"]"), "types");
        contenu.Add(new StringContent(engine), "engine");

        using var response = await client.PostAsync("/api/documents/classify", contenu);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var resultat = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("pdfpig", resultat.RootElement.GetProperty("moteur").GetString());
    }

    [Fact]
    public async Task RefuseUnPdfSansTexteAvantDAppelerFlowise()
    {
        var classifieur = new ClassifieurDeTest((_, _) => new ClassificationResult("RIB", 0.5));
        await using var application = CreerApplication("Development", classifieur);
        using var client = application.CreateClient();
        using var contenu = CreerPdfMultipart(string.Empty);
        contenu.Add(new StringContent("[\"RIB\"]"), "types");
        contenu.Add(new StringContent("pdfpig"), "engine");

        using var response = await client.PostAsync("/api/documents/classify", contenu);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(0, classifieur.Appels);
    }

    [Fact]
    public async Task ClassifieUnPdfAvecUnScoreFaibleEtDesTypesDynamiques()
    {
        var classifieur = new ClassifieurDeTest((_, _) => new ClassificationResult("DEVIS", 0.12, "run-123"));
        await using var application = CreerApplication("Development", classifieur);
        using var client = application.CreateClient();
        using var contenu = CreerPdfMultipart("Demande de devis");
        contenu.Add(new StringContent("\"CV\",\"OFFRE_EMPLOI\",\"RIB\",\"FACTURE\""), "types");
        contenu.Add(new StringContent("pdfpig"), "engine");

        using var response = await client.PostAsync("/api/documents/classify", contenu);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var resultat = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("DEVIS", resultat.RootElement.GetProperty("type").GetString());
        Assert.Equal(0.12, resultat.RootElement.GetProperty("confiance").GetDouble());
        Assert.Equal("run-123", resultat.RootElement.GetProperty("executionId").GetString());
        Assert.Equal(["CV", "OFFRE_EMPLOI", "RIB", "FACTURE"], classifieur.DerniersTypes);
    }

    [Fact]
    public async Task MasqueLesPiiAvantFlowiseEtMesureLaDuree()
    {
        var flowise = new FlowiseCaptureHandler();
        using var flowiseClient = new HttpClient(flowise);
        var classifieur = new FlowiseDocumentClassifier(
            flowiseClient,
            new FlowiseOptions("https://flowise.test", "flow-test"));
        await using var application = CreerApplication(
            "Development",
            classifieur,
            new PiiDetectorDeTest());
        using var client = application.CreateClient();
        using var contenu = CreerPdfMultipart("Contact : Jean Dupont");
        contenu.Add(new StringContent("[\"RIB\"]"), "types");
        contenu.Add(new StringContent("pdfpig"), "engine");

        using var response = await client.PostAsync("/api/documents/classify", contenu);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, flowise.Appels);
        Assert.DoesNotContain("Jean Dupont", flowise.DernierCorps);
        using var payload = JsonDocument.Parse(flowise.DernierCorps!);
        Assert.Equal(
            "Contact : [PERSON]",
            payload.RootElement.GetProperty("form").GetProperty("texte").GetString());
        using var resultat = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(resultat.RootElement.GetProperty("piiMaskingMs").GetInt64() >= 0);
    }

    [Fact]
    public async Task NAppellePasFlowiseQuandLaDetectionPiiEchoue()
    {
        var flowise = new FlowiseCaptureHandler();
        using var flowiseClient = new HttpClient(flowise);
        var classifieur = new FlowiseDocumentClassifier(
            flowiseClient,
            new FlowiseOptions("https://flowise.test", "flow-test"));
        await using var application = CreerApplication(
            "Development",
            classifieur,
            new PiiDetectorEnEchec());
        using var client = application.CreateClient();
        using var contenu = CreerPdfMultipart("Contact : Jean Dupont");
        contenu.Add(new StringContent("[\"RIB\"]"), "types");
        contenu.Add(new StringContent("pdfpig"), "engine");

        using var response = await client.PostAsync("/api/documents/classify", contenu);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, flowise.Appels);
        Assert.DoesNotContain("Jean Dupont", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ReprendApresUneErreurFlowiseEtAccepteDeuxRequetes()
    {
        var classifieur = new ClassifieurDeTest((appel, _) => appel == 1
            ? throw new HttpRequestException("Flowise indisponible.")
            : new ClassificationResult("RIB", 0.9));
        await using var application = CreerApplication("Development", classifieur);
        using var client = application.CreateClient();

        using var premierContenu = CreerPdfMultipart("premier document");
        premierContenu.Add(new StringContent("[\"RIB\"]"), "types");
        premierContenu.Add(new StringContent("pdfpig"), "engine");
        using var premier = await client.PostAsync("/api/documents/classify", premierContenu);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, premier.StatusCode);

        var second = EnvoyerClassificationAsync(client, "second document");
        var troisieme = EnvoyerClassificationAsync(client, "troisième document");
        var reponses = await Task.WhenAll(second, troisieme);

        Assert.All(reponses, response =>
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            response.Dispose();
        });
        Assert.Equal(3, classifieur.Appels);
    }

    [Fact]
    public async Task NExposePasLesRoutesDeDiagnosticEnProduction()
    {
        await using var application = CreerApplication("Production");
        using var client = application.CreateClient();
        using var contenu = CreerPdfMultipart("contenu");

        using var response = await client.PostAsync("/api/dev/extract/pdfpig", contenu);
        using var piiResponse = await client.PostAsJsonAsync(
            "/api/dev/pii/detect",
            new { text = "Jean Dupont" });
        using var maskingResponse = await client.PostAsJsonAsync(
            "/api/dev/pii/mask",
            new { text = "Jean Dupont" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, piiResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, maskingResponse.StatusCode);
    }

    [Fact]
    public async Task DetecteLesPiiEnDevelopment()
    {
        await using var application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureServices(services => services.AddSingleton<IPiiDetector>(new PiiDetectorDeTest()));
                builder.ConfigureLogging(logging => logging.ClearProviders());
            });
        using var client = application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/dev/pii/detect",
            new { text = "Contact : Jean Dupont" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var detection = Assert.Single(result.RootElement.EnumerateArray());
        Assert.Equal("Name", detection.GetProperty("category").GetString());
        Assert.Equal("Jean Dupont", detection.GetProperty("value").GetString());
    }

    [Fact]
    public async Task MasqueLesPiiSansDupliquerLesDetections()
    {
        await using var application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureServices(services =>
                    services.AddSingleton<IPiiDetector>(new PiiDetectorAvecChevauchements()));
                builder.ConfigureLogging(logging => logging.ClearProviders());
            });
        using var client = application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/dev/pii/mask",
            new
            {
                text = "Contact : Jean Dupont, IBAN FR76 3000 6000 0112 3456 7890 189, téléphone : 06 12 34 56 78."
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "Contact : [PERSON], IBAN [IBAN], téléphone : [PHONE].",
            result.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task EtendLeMasqueQuandLeModeleDepasseUneReglePourLaMemeCategorie()
    {
        await using var application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureServices(services =>
                    services.AddSingleton<IPiiDetector>(new PiiDetectorAvecModelePlusLarge()));
                builder.ConfigureLogging(logging => logging.ClearProviders());
            });
        using var client = application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/dev/pii/mask",
            new { text = "Contact : Jean Dupont Martin." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "Contact : [PERSON].",
            result.RootElement.GetProperty("text").GetString());
    }

    private static WebApplicationFactory<Program> CreerApplication(
        string environnement,
        IDocumentClassifier? classifieur = null,
        IPiiDetector? piiDetector = null) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environnement);
                builder.ConfigureServices(services =>
                    services.AddSingleton(piiDetector ?? new PiiDetectorVide()));
                if (classifieur is not null)
                {
                    builder.ConfigureServices(services => services.AddSingleton(classifieur));
                }
                builder.ConfigureLogging(logging => logging.ClearProviders());
            });

    private static async Task<HttpResponseMessage> EnvoyerClassificationAsync(HttpClient client, string texte)
    {
        using var contenu = CreerPdfMultipart(texte);
        contenu.Add(new StringContent("[\"RIB\"]"), "types");
        contenu.Add(new StringContent("pdfpig"), "engine");
        return await client.PostAsync("/api/documents/classify", contenu);
    }

    private static MultipartFormDataContent CreerPdfMultipart(string texte)
    {
        var fichier = new ByteArrayContent(PdfDeTest.Construire(texte));
        fichier.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var contenu = new MultipartFormDataContent();
        contenu.Add(fichier, "file", "document.pdf");
        return contenu;
    }

    private sealed class ClassifieurEnEchec(Exception exception) : IDocumentClassifier
    {
        public Task<ClassificationResult> ClassifyAsync(
            string texte,
            IReadOnlyCollection<string> types,
            CancellationToken cancellationToken = default) => Task.FromException<ClassificationResult>(exception);
    }

    private sealed class PiiDetectorDeTest : IPiiDetector
    {
        public Task<IReadOnlyList<PiiDetection>> DetectAsync(
            string text,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PiiDetection>>(
                [new PiiDetection(PiiCategory.Name, "PERSON", PiiDetectionKind.Model, 10, 11)]);
    }

    private sealed class PiiDetectorVide : IPiiDetector
    {
        public Task<IReadOnlyList<PiiDetection>> DetectAsync(
            string text,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PiiDetection>>([]);
    }

    private sealed class PiiDetectorEnEchec : IPiiDetector
    {
        public Task<IReadOnlyList<PiiDetection>> DetectAsync(
            string text,
            CancellationToken cancellationToken = default) =>
            Task.FromException<IReadOnlyList<PiiDetection>>(
                new InvalidOperationException("Détecteur indisponible."));
    }

    private sealed class PiiDetectorAvecChevauchements : IPiiDetector
    {
        public Task<IReadOnlyList<PiiDetection>> DetectAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            var nameStart = text.IndexOf("Jean Dupont", StringComparison.Ordinal);
            var ibanStart = text.IndexOf("FR76", StringComparison.Ordinal);
            var addressStart = text.IndexOf("IBAN FR76", StringComparison.Ordinal);
            var phoneStart = text.IndexOf("06 12 34 56 78", StringComparison.Ordinal);
            return Task.FromResult<IReadOnlyList<PiiDetection>>(
            [
                new(PiiCategory.Name, "NomApresLibelle", PiiDetectionKind.Label, nameStart, 11),
                new(PiiCategory.Name, "PERSON", PiiDetectionKind.Model, nameStart, 11),
                new(PiiCategory.Iban, "Iban", PiiDetectionKind.Specification, ibanStart, 33),
                new(PiiCategory.Iban, "IBAN_CODE", PiiDetectionKind.Model, ibanStart, 33),
                new(PiiCategory.Address, "LOCATION", PiiDetectionKind.Model, addressStart, 9),
                new(PiiCategory.Phone, "Telephone", PiiDetectionKind.Specification, phoneStart, 14)
            ]);
        }
    }

    private sealed class PiiDetectorAvecModelePlusLarge : IPiiDetector
    {
        public Task<IReadOnlyList<PiiDetection>> DetectAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            var nameStart = text.IndexOf("Jean Dupont Martin", StringComparison.Ordinal);
            return Task.FromResult<IReadOnlyList<PiiDetection>>(
            [
                new(PiiCategory.Name, "NomApresLibelle", PiiDetectionKind.Label, nameStart, "Jean Dupont".Length),
                new(PiiCategory.Name, "PERSON", PiiDetectionKind.Model, nameStart, "Jean Dupont Martin".Length)
            ]);
        }
    }

    private sealed class FlowiseCaptureHandler : HttpMessageHandler
    {
        public int Appels { get; private set; }

        public string? DernierCorps { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Appels++;
            DernierCorps = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    text = """{"type":"RIB","confiance":0.9}"""
                })
            };
        }
    }

    private sealed class ClassifieurDeTest(
        Func<int, IReadOnlyCollection<string>, ClassificationResult> resultFactory) : IDocumentClassifier
    {
        private int _appels;

        public int Appels => _appels;

        public IReadOnlyCollection<string>? DerniersTypes { get; private set; }

        public Task<ClassificationResult> ClassifyAsync(
            string texte,
            IReadOnlyCollection<string> types,
            CancellationToken cancellationToken = default)
        {
            DerniersTypes = types;
            // Les tests envoient des requêtes concurrentes sur la même instance : sans incrément
            // atomique, un appel se perd et le compteur devient intermittent.
            return Task.FromResult(resultFactory(Interlocked.Increment(ref _appels), types));
        }
    }
}
