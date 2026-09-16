using DetectDoc.Core.Classification;
using DetectDoc.Core.Engines;
using DetectDoc.Core.Pii;
using Xunit;

namespace DetectDoc.Tests;

public class DocumentClassificationTests
{
    [Fact]
    public async Task ExecuteAsync_RetourneLeResultatEtLesMesures()
    {
        var selecteur = new SelecteurFictif(_ => new ExtractionResult(
            [new PageExtractionResult(1, "Relevé bancaire", null), new PageExtractionResult(2, "Suite", 0.9f)],
            "pdfpig"));
        var classifieur = new ClassifieurFictif((_, _) => new ClassificationResult("RIB", 0.42, "run-1"));
        var pipeline = CreerPipeline(selecteur, classifieur);
        var types = new[] { new TypeDocumentaire("RIB", null, null) };

        var outcome = await pipeline.ExecuteAsync(new MemoryStream(), types, "auto");

        var success = Assert.IsType<DocumentClassificationOutcome.Success>(outcome);
        Assert.Equal("RIB", success.Result.Type);
        Assert.Equal(0.42, success.Result.Confiance);
        Assert.Equal("pdfpig", success.Result.Moteur);
        Assert.Equal(2, success.Result.PagesTraitees);
        Assert.Equal("run-1", success.Result.ExecutionId);
        Assert.True(success.Result.TotalMs >= success.Result.ClassificationMs);
        Assert.Equal("Relevé bancaire" + Environment.NewLine + "Suite", classifieur.DernierTexte);
        Assert.Same(types, classifieur.DerniersTypes);
        Assert.Equal("auto", selecteur.MoteurDemande);
    }

    [Fact]
    public async Task ExecuteAsync_MasqueLeTexteAvantClassification()
    {
        var selecteur = new SelecteurFictif(_ => new ExtractionResult(
            [new PageExtractionResult(1, "Contact : Jean Dupont", null)], "pdfpig"));
        var classifieur = new ClassifieurFictif((_, _) => new ClassificationResult("RIB", 0.9));
        var masqueur = new PiiTextMasker(new DetecteurFictif(_ =>
            [new PiiDetection(PiiCategory.Name, "NomApresLibelle", PiiDetectionKind.Label, 10, 11)]));
        var pipeline = CreerPipeline(selecteur, classifieur, masqueur);

        var outcome = await pipeline.ExecuteAsync(new MemoryStream(), [new TypeDocumentaire("RIB", null, null)]);

        Assert.IsType<DocumentClassificationOutcome.Success>(outcome);
        Assert.Equal("Contact : [PERSON]", classifieur.DernierTexte);
    }

    [Fact]
    public async Task ExecuteAsync_RejetteUnTexteVideSansMasquerNiClasser()
    {
        var selecteur = new SelecteurFictif(_ => new ExtractionResult(
            [new PageExtractionResult(1, "   ", null)], "pdfpig"));
        var classifieur = new ClassifieurFictif((_, _) => new ClassificationResult("RIB", 0.9));
        var masqueurResolu = false;
        var pipeline = new DocumentClassification(
            selecteur,
            () =>
            {
                masqueurResolu = true;
                return new PiiTextMasker(new DetecteurFictif(_ => []));
            },
            classifieur);

        var outcome = await pipeline.ExecuteAsync(new MemoryStream(), [new TypeDocumentaire("RIB", null, null)]);

        var rejet = Assert.IsType<DocumentClassificationOutcome.Rejected>(outcome);
        Assert.Equal(ClassificationRejection.EmptyText, rejet.Reason);
        Assert.False(masqueurResolu);
        Assert.Equal(0, classifieur.Appels);
    }

    [Fact]
    public async Task ExecuteAsync_RetourneUneFauteExtractionSansMasquerNiClasser()
    {
        var panne = new InvalidDataException("PDF invalide.");
        var selecteur = new SelecteurFictif(_ => throw panne);
        var classifieur = new ClassifieurFictif((_, _) => new ClassificationResult("RIB", 0.9));
        var pipeline = CreerPipeline(selecteur, classifieur);

        var outcome = await pipeline.ExecuteAsync(new MemoryStream(), [new TypeDocumentaire("RIB", null, null)]);

        var faute = Assert.IsType<DocumentClassificationOutcome.Faulted>(outcome);
        Assert.Equal(ClassificationStage.Extraction, faute.Stage);
        Assert.Same(panne, faute.Exception);
        Assert.Equal(0, classifieur.Appels);
    }

    [Fact]
    public async Task ExecuteAsync_RetourneUneFautePiiSansClasser()
    {
        var panne = new InvalidOperationException("Presidio indisponible.");
        var selecteur = new SelecteurFictif(_ => new ExtractionResult(
            [new PageExtractionResult(1, "texte", null)], "pdfpig"));
        var classifieur = new ClassifieurFictif((_, _) => new ClassificationResult("RIB", 0.9));
        var masqueur = new PiiTextMasker(new DetecteurFictif(_ => throw panne));
        var pipeline = CreerPipeline(selecteur, classifieur, masqueur);

        var outcome = await pipeline.ExecuteAsync(new MemoryStream(), [new TypeDocumentaire("RIB", null, null)]);

        var faute = Assert.IsType<DocumentClassificationOutcome.Faulted>(outcome);
        Assert.Equal(ClassificationStage.PiiMasking, faute.Stage);
        Assert.Same(panne, faute.Exception);
        Assert.Equal(0, classifieur.Appels);
    }

    [Fact]
    public async Task ExecuteAsync_RetourneUneFauteClassification()
    {
        var panne = new HttpRequestException("Flowise indisponible.");
        var selecteur = new SelecteurFictif(_ => new ExtractionResult(
            [new PageExtractionResult(1, "texte", null)], "pdfpig"));
        var classifieur = new ClassifieurFictif((_, _) => throw panne);
        var pipeline = CreerPipeline(selecteur, classifieur);

        var outcome = await pipeline.ExecuteAsync(new MemoryStream(), [new TypeDocumentaire("RIB", null, null)]);

        var faute = Assert.IsType<DocumentClassificationOutcome.Faulted>(outcome);
        Assert.Equal(ClassificationStage.Classification, faute.Stage);
        Assert.Same(panne, faute.Exception);
    }

    [Fact]
    public async Task ExecuteAsync_PropageLAnnulationDemandeeParLAppelant()
    {
        var selecteur = new SelecteurFictif(_ => new ExtractionResult(
            [new PageExtractionResult(1, "texte", null)], "pdfpig"));
        var classifieur = new ClassifieurFictif((_, _) =>
            throw new TaskCanceledException("délai interne"));
        var pipeline = CreerPipeline(selecteur, classifieur);
        var annulation = new CancellationToken(canceled: true);

        // Annulation de l'appelant : l'exception remonte sans devenir une faute de l'outcome.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pipeline.ExecuteAsync(
            new MemoryStream(), [new TypeDocumentaire("RIB", null, null)], cancellationToken: annulation));
    }

    [Fact]
    public async Task ExecuteAsync_RetourneUneFautePourUnTimeoutFournisseur()
    {
        var selecteur = new SelecteurFictif(_ => new ExtractionResult(
            [new PageExtractionResult(1, "texte", null)], "pdfpig"));
        var classifieur = new ClassifieurFictif((_, _) =>
            throw new TaskCanceledException("timeout HttpClient"));
        var pipeline = CreerPipeline(selecteur, classifieur);

        var outcome = await pipeline.ExecuteAsync(
            new MemoryStream(), [new TypeDocumentaire("RIB", null, null)]);

        var faute = Assert.IsType<DocumentClassificationOutcome.Faulted>(outcome);
        Assert.Equal(ClassificationStage.Classification, faute.Stage);
    }

    private static DocumentClassification CreerPipeline(
        IExtractionSelector selecteur,
        IDocumentClassifier classifieur,
        PiiTextMasker? masqueur = null) =>
        new(selecteur, () => masqueur ?? new PiiTextMasker(new DetecteurFictif(_ => [])), classifieur);

    private sealed class SelecteurFictif(Func<string?, ExtractionResult> impl) : IExtractionSelector
    {
        public string? MoteurDemande { get; private set; }

        public ExtractionResult Extract(
            string? engine,
            Stream document,
            CancellationToken cancellationToken = default)
        {
            MoteurDemande = engine;
            return impl(engine);
        }
    }

    private sealed class ClassifieurFictif(
        Func<string, IReadOnlyCollection<TypeDocumentaire>, ClassificationResult> impl) : IDocumentClassifier
    {
        public int Appels { get; private set; }
        public string? DernierTexte { get; private set; }
        public IReadOnlyCollection<TypeDocumentaire>? DerniersTypes { get; private set; }

        public Task<ClassificationResult> ClassifyAsync(
            string texte,
            IReadOnlyCollection<TypeDocumentaire> types,
            CancellationToken cancellationToken = default)
        {
            Appels++;
            DernierTexte = texte;
            DerniersTypes = types;
            return Task.FromResult(impl(texte, types));
        }
    }

    private sealed class DetecteurFictif(Func<string, IReadOnlyList<PiiDetection>> impl) : IPiiDetector
    {
        public Task<IReadOnlyList<PiiDetection>> DetectAsync(
            string text,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(impl(text));
    }
}
