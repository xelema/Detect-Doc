namespace DetectDoc.Core.Pii;

public interface IPiiDetector
{
    Task<IReadOnlyList<PiiDetection>> DetectAsync(
        string text,
        CancellationToken cancellationToken = default);
}

public readonly record struct PiiDetection(
    PiiCategory Category,
    string Rule,
    PiiDetectionKind Kind,
    int Start,
    int Length);

public enum PiiDetectionKind
{
    Specification,
    Label,
    Vocabulary,
    Model
}

public enum PiiCategory
{
    Unknown,
    Name,
    Organization,
    Email,
    Phone,
    Address,
    Iban,
    Bic,
    Siret,
    ProfessionalIdentifier,
    DocumentNumber,
    SocialNetwork
}
