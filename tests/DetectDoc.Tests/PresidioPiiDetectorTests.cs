using System.Net;
using System.Net.Http.Json;
using DetectDoc.Infrastructure.Pii;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DetectDoc.Tests;

// Les tests mutent PII_PYTHON et PII_PYTHON_SCRIPT : les variables sont lues au démarrage du
// sous-processus, et xUnit n'exécute jamais en parallèle deux tests d'une même classe.
public class PresidioPiiDetectorTests
{
    // Stub de protocole : chaque instance meurt juste après le handshake, sauf si le compteur
    // persistant pointé par PII_STUB_ETAT indique un redémarrage, auquel cas elle sert la boucle.
    private const string StubQuittePuisSert = """
        import json, os, sys

        vivant = False
        etat = os.environ.get("PII_STUB_ETAT")
        if etat:
            compte = 0
            if os.path.exists(etat):
                with open(etat, encoding="ascii") as f:
                    compte = int(f.read().strip() or "0")
            with open(etat, "w", encoding="ascii") as f:
                f.write(str(compte + 1))
            vivant = compte >= 1

        print(json.dumps({"modele": "stub"}), flush=True)
        if not vivant:
            sys.exit(0)
        for ligne in sys.stdin:
            requete = json.loads(ligne)
            print(json.dumps({"id": requete["id"], "detections": []}), flush=True)
        """;

    private const string StubNourritStderr = """
        import json, sys

        sys.stderr.write("DEBUT-MARQUEUR\n")
        sys.stderr.write("X" * (64 * 1024) + "\n")
        sys.stderr.write("FIN-MARQUEUR\n")
        sys.stderr.flush()
        print(json.dumps({"erreur": "chargement impossible"}), flush=True)
        """;

    private const string StubNeLitPlus = """
        import json, time

        print(json.dumps({"modele": "stub"}), flush=True)
        time.sleep(3600)
        """;

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

    [Fact]
    public void Constructeur_SansHandshakeDansLeDelai_LeveUneIndisponibilite()
    {
        // Aucun processus n'écrit sa première ligne stdout dans la milliseconde qui suit
        // Process.Start : la lecture du handshake doit être bornée, sans quoi un Python qui
        // pend gèlerait la résolution du singleton.
        using var restore = PiiPython(@"C:\Windows\System32\Robocopy.exe");
        Assert.Throws<InvalidOperationException>(() => new PresidioPiiDetector(TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public async Task RelanceUnEnfantMortPuisRepond()
    {
        // Le stub meurt dès le handshake à son premier démarrage puis sert le protocole :
        // l'appel qui suit la mort doit relancer l'enfant et obtenir une réponse.
        var etat = Path.Combine(Path.GetTempPath(), $"presidio-stub-etat-{Guid.NewGuid():N}.txt");
        using var restore = PiiPythonStub(StubQuittePuisSert, etat);
        var sante = new PresidioHealth();
        using var detecteur = new PresidioPiiDetector(
            detectionTimeout: TimeSpan.FromSeconds(10),
            health: sante);
        await Task.Delay(1500);

        var detections = await detecteur.DetectAsync("Contact : Jean Dupont");

        Assert.Empty(detections);
        Assert.False(sante.Unrecoverable);
    }

    [Fact]
    public async Task ResteEnFauteQuandLeBudgetDeRelancesEstEpuise()
    {
        // Sans PII_STUB_ETAT le stub meurt après chaque handshake : trois relances au plus,
        // puis le détecteur reste en faute au lieu de boucler sur des démarrages coûteux.
        using var restore = PiiPythonStub(StubQuittePuisSert);
        var sante = new PresidioHealth();
        using var detecteur = new PresidioPiiDetector(
            detectionTimeout: TimeSpan.FromMilliseconds(500),
            health: sante);
        await Task.Delay(1500);

        for (var appel = 0; appel < 6; appel++)
        {
            await Assert.ThrowsAnyAsync<Exception>(() => detecteur.DetectAsync("Contact : Jean Dupont"));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => detecteur.DetectAsync("Contact : Jean Dupont"));
        Assert.True(sante.Unrecoverable);
    }

    [Fact]
    public void GardeLaFinDuStderrPourLeDiagnosticDeDemarrage()
    {
        // Le stub inonde stderr avant son handshake d'erreur : la rétention bornée doit garder
        // la fin du flux, pas le début, et ne jamais laisser le tube plein geler l'enfant.
        using var restore = PiiPythonStub(StubNourritStderr);

        var exception = Assert.Throws<InvalidOperationException>(() => new PresidioPiiDetector());
        var detail = exception.InnerException!.Message;
        Assert.Contains("FIN-MARQUEUR", detail);
        Assert.DoesNotContain("DEBUT-MARQUEUR", detail);
        Assert.True(detail.Length < 32 * 1024);
    }

    [Fact]
    public async Task BorneLEcritureQuandLEnfantNeLitPlus()
    {
        // Un enfant qui ne lit plus son entrée finit par remplir le tube : l'écriture sous le
        // verrou d'envoi doit expirer sur le délai de détection au lieu de pendre sans borne.
        using var restore = PiiPythonStub(StubNeLitPlus);
        using var detecteur = new PresidioPiiDetector(
            detectionTimeout: TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<TimeoutException>(
            () => detecteur.DetectAsync(new string('x', 5_000_000)));
    }

    private static WebApplicationFactory<Program> CreerApplication() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureLogging(logging => logging.ClearProviders());
            });

    private static IDisposable PiiPythonStub(string contenu, string? etat = null)
    {
        var script = Path.Combine(Path.GetTempPath(), $"presidio-stub-{Guid.NewGuid():N}.py");
        File.WriteAllText(script, contenu);
        return new Nettoyage(PiiPython("python", script, etat), script, etat);
    }

    private static IDisposable PiiPython(string chemin, string? script = null, string? etat = null)
    {
        var precedents = new Dictionary<string, string?>
        {
            ["PII_PYTHON"] = Environment.GetEnvironmentVariable("PII_PYTHON"),
            ["PII_PYTHON_SCRIPT"] = Environment.GetEnvironmentVariable("PII_PYTHON_SCRIPT"),
            ["PII_STUB_ETAT"] = Environment.GetEnvironmentVariable("PII_STUB_ETAT"),
        };
        Environment.SetEnvironmentVariable("PII_PYTHON", chemin);
        Environment.SetEnvironmentVariable("PII_PYTHON_SCRIPT", script);
        Environment.SetEnvironmentVariable("PII_STUB_ETAT", etat);
        return new Restauration(precedents);
    }

    private sealed class Restauration(IReadOnlyDictionary<string, string?> precedents) : IDisposable
    {
        public void Dispose()
        {
            foreach (var (cle, valeur) in precedents)
            {
                Environment.SetEnvironmentVariable(cle, valeur);
            }
        }
    }

    private sealed class Nettoyage(IDisposable interne, string script, string? etat) : IDisposable
    {
        public void Dispose()
        {
            interne.Dispose();
            foreach (var fichier in new[] { script, etat })
            {
                try
                {
                    if (fichier is not null)
                    {
                        File.Delete(fichier);
                    }
                }
                catch (Exception)
                {
                    // Fichier temporaire : son effacement est du meilleur effort.
                }
            }
        }
    }
}
