using DetectDoc.Core.Classification;
using DetectDoc.Core.Engines;
using DetectDoc.Infrastructure.Classification;
using DetectDoc.Infrastructure.Engines;

namespace DetectDoc.Api;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDetectDoc(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var repertoireModeles = configuration["PpOcr:ModelsDirectory"]
            ?? Path.Combine(AppContext.BaseDirectory, "models");
        var reconnaissancesParalleles = configuration.GetValue("PpOcr:ReconnaissancesParalleles", 1);

        var accepterCertificatInvalide = configuration.GetValue<bool>("Flowise:AllowInvalidTls");
        if (accepterCertificatInvalide && !environment.IsDevelopment())
        {
            throw new InvalidOperationException("Flowise:AllowInvalidTls est réservé à Development.");
        }

        services.AddSingleton<PdfPigNativeTextEngine>();
        // Le constructeur charge les modèles ONNX : la résolution reste paresseuse pour que
        // l'API démarre sans modèle et que leur absence se traduise par une erreur de requête.
        services.AddSingleton(_ => new PpOcrEngine("tiny", repertoireModeles, reconnaissancesParalleles));
        services.AddSingleton(fournisseur => new ExtractionAutomatique(
            fournisseur.GetRequiredService<PdfPigNativeTextEngine>(),
            () => fournisseur.GetRequiredService<PpOcrEngine>()));
        services.AddSingleton(new FlowiseOptions(
            configuration["Flowise:BaseUrl"],
            configuration["Flowise:FlowId"]));
        services.AddHttpClient<IClassifieurDocument, FlowiseDocumentClassifier>(client =>
                client.Timeout = TimeSpan.FromSeconds(60))
            .ConfigurePrimaryHttpMessageHandler(() => accepterCertificatInvalide
                ? new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                }
                : new HttpClientHandler());

        return services;
    }
}
