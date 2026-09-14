using System.Diagnostics;
using DetectDoc.Core.Engines;
using DetectDoc.Core.Pii;

namespace DetectDoc.Core.Classification;

// Le use case du service : extraire, masquer les PII, classer, mesurer chaque étape.
// Ne possède pas le flux reçu : il appartient à l'appelant.
public sealed class DocumentClassification(
    IExtractionSelector selector,
    Func<PiiTextMasker> masker,
    IDocumentClassifier classifier)
{
    public async Task<DocumentClassificationOutcome> ExecuteAsync(
        Stream document,
        IReadOnlyCollection<TypeDocumentaire> types,
        string? engine = null,
        CancellationToken cancellationToken = default)
    {
        var total = Stopwatch.StartNew();

        ExtractionResult extraction;
        var extractionDuree = Stopwatch.StartNew();
        try
        {
            extraction = selector.Extract(engine, document, cancellationToken);
        }
        catch (Exception exception) when (Capturable(exception, cancellationToken))
        {
            return new DocumentClassificationOutcome.Faulted(ClassificationStage.Extraction, exception);
        }

        extractionDuree.Stop();
        var texte = string.Join(Environment.NewLine, extraction.Pages.Select(page => page.Texte));
        if (string.IsNullOrWhiteSpace(texte))
        {
            return new DocumentClassificationOutcome.Rejected(ClassificationRejection.EmptyText);
        }

        var masquageDuree = Stopwatch.StartNew();
        string texteMasque;
        try
        {
            // Le masqueur est résolu ici et non au constructeur : sa construction démarre le
            // sous-processus Presidio, et un échec à cette étape reste une faute PII, pas un 500.
            texteMasque = await masker().MaskAsync(texte, cancellationToken);
        }
        catch (Exception exception) when (Capturable(exception, cancellationToken))
        {
            return new DocumentClassificationOutcome.Faulted(ClassificationStage.PiiMasking, exception);
        }

        masquageDuree.Stop();
        var classificationDuree = Stopwatch.StartNew();
        ClassificationResult resultat;
        try
        {
            resultat = await classifier.ClassifyAsync(texteMasque, types, cancellationToken);
        }
        catch (Exception exception) when (Capturable(exception, cancellationToken))
        {
            return new DocumentClassificationOutcome.Faulted(ClassificationStage.Classification, exception);
        }

        classificationDuree.Stop();
        total.Stop();
        return new DocumentClassificationOutcome.Success(new DocumentClassificationResult(
            resultat.Type,
            resultat.Confiance,
            extraction.Moteur,
            extraction.Pages.Count,
            extractionDuree.ElapsedMilliseconds,
            masquageDuree.ElapsedMilliseconds,
            classificationDuree.ElapsedMilliseconds,
            total.ElapsedMilliseconds,
            resultat.ExecutionId));
    }

    // Une annulation demandée par l'appelant n'est pas une faute d'étape : elle remonte telle quelle.
    private static bool Capturable(Exception exception, CancellationToken cancellationToken) =>
        exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested;
}
