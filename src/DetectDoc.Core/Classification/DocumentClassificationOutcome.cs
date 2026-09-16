namespace DetectDoc.Core.Classification;

public abstract record DocumentClassificationOutcome
{
    private DocumentClassificationOutcome() { }

    public sealed record Success(DocumentClassificationResult Result) : DocumentClassificationOutcome;

    // Rejet décidé par le pipeline lui-même, sans faute d'un adapteur.
    public sealed record Rejected(ClassificationRejection Reason) : DocumentClassificationOutcome;

    // Exception levée par une étape : la couche HTTP la traduit selon l'étape.
    public sealed record Faulted(ClassificationStage Stage, Exception Exception) : DocumentClassificationOutcome;
}

public sealed record DocumentClassificationResult(
    string Type,
    double Confiance,
    string Moteur,
    int PagesTraitees,
    long ExtractionMs,
    long PiiMaskingMs,
    long ClassificationMs,
    long TotalMs,
    string? ExecutionId);

public enum ClassificationStage
{
    Extraction,
    PiiMasking,
    Classification
}

public enum ClassificationRejection
{
    EmptyText
}
