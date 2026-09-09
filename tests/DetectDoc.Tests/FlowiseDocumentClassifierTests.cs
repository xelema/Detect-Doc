using System.Net;
using System.Text;
using System.Text.Json;
using DetectDoc.Infrastructure.Classification;
using Xunit;

namespace DetectDoc.Tests;

public class FlowiseDocumentClassifierTests
{
    [Fact]
    public async Task ClasserAsync_EnvoieLesTypesEtRetourneLeJsonDuChampText()
    {
        using var client = new HttpClient(new FlowiseHandler());
        var classifieur = new FlowiseDocumentClassifier(
            client,
            new FlowiseOptions("https://flowise.test", "flow-test"));

        var resultat = await classifieur.ClasserAsync(
            "RELEVÉ D’IDENTITÉ BANCAIRE",
            ["RIB", "FEUILLE_EMAR"]);

        Assert.Equal("RIB", resultat.Type);
        Assert.Equal(0.98, resultat.Confiance);
    }

    [Theory]
    [InlineData("pas du json")]
    [InlineData("""{"text":"Ce document ressemble a un RIB."}""")]
    public async Task ClasserAsync_SignaleUneReponseFlowiseNonJson(string corps)
    {
        using var client = new HttpClient(new ReponseFixeHandler(corps));
        var classifieur = new FlowiseDocumentClassifier(
            client,
            new FlowiseOptions("https://flowise.test", "flow-test"));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => classifieur.ClasserAsync("un texte", ["RIB"]));
    }

    private sealed class ReponseFixeHandler(string corps) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(corps, Encoding.UTF8, "application/json")
            });
    }

    private sealed class FlowiseHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://flowise.test/api/v1/prediction/flow-test", request.RequestUri?.ToString());

            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var form = body.RootElement.GetProperty("form");
            Assert.Equal("RELEVÉ D’IDENTITÉ BANCAIRE", form.GetProperty("texte").GetString());
            Assert.Equal("RIB, FEUILLE_EMAR", form.GetProperty("types").GetString());

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"text":"{\"type\":\"RIB\",\"confiance\":0.98}"}""",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
