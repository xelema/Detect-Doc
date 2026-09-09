using System.Net.Http.Json;
using System.Text.Json;
using DetectDoc.Core.Classification;

namespace DetectDoc.Infrastructure.Classification;

public sealed record FlowiseOptions(string? BaseUrl, string? FlowId);

public sealed class FlowiseDocumentClassifier(HttpClient httpClient, FlowiseOptions options)
    : IClassifieurDocument
{
    public async Task<ResultatClassification> ClasserAsync(
        string texte,
        IReadOnlyCollection<string> types,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(texte);
        ArgumentNullException.ThrowIfNull(types);
        if (types.Count == 0 || types.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Au moins un type non vide est requis.", nameof(types));
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl) || string.IsNullOrWhiteSpace(options.FlowId))
        {
            throw new InvalidOperationException("La configuration Flowise:BaseUrl et Flowise:FlowId est requise.");
        }

        var url = $"{options.BaseUrl.TrimEnd('/')}/api/v1/prediction/{Uri.EscapeDataString(options.FlowId)}";
        using var response = await httpClient.PostAsJsonAsync(
            url,
            new { form = new { texte, types = string.Join(", ", types) } },
            cancellationToken);
        response.EnsureSuccessStatusCode();

        ReponseFlowise? enveloppe;
        ResultatClassification? resultat;
        try
        {
            enveloppe = await response.Content.ReadFromJsonAsync<ReponseFlowise>(cancellationToken);
            if (string.IsNullOrWhiteSpace(enveloppe?.Text))
            {
                throw new InvalidDataException("La réponse Flowise ne contient pas de champ text.");
            }

            resultat = JsonSerializer.Deserialize<ResultatClassification>(
                enveloppe.Text,
                JsonSerializerOptions.Web);
        }
        catch (JsonException exception)
        {
            // Le flux peut répondre en langage naturel au lieu du JSON attendu : c'est une panne
            // amont, pas une erreur de l'appelant, et le contrat HTTP la traduit en 502.
            throw new InvalidDataException("La réponse Flowise n'est pas un JSON exploitable.", exception);
        }
        if (resultat is null
            || string.IsNullOrWhiteSpace(resultat.Type)
            || !double.IsFinite(resultat.Confiance)
            || resultat.Confiance is < 0 or > 1
            || !types.Contains(resultat.Type, StringComparer.Ordinal))
        {
            throw new InvalidDataException("La réponse de classification Flowise est invalide.");
        }

        return resultat with { ExecutionId = enveloppe.ExecutionId };
    }

    private sealed record ReponseFlowise(string? Text, string? ExecutionId);
}
