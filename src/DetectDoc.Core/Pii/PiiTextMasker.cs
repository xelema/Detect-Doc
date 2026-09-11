using System.Text;

namespace DetectDoc.Core.Pii;

public sealed class PiiTextMasker(IPiiDetector detector)
{
    public async Task<string> MaskAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        // Aligné sur la garde du détecteur (ArgumentException.ThrowIfNullOrWhiteSpace) : un texte
        // uniquement composé d'espaces n'a rien à masquer, il ne doit pas lever d'exception.
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var detections = await detector.DetectAsync(text, cancellationToken);
        foreach (var detection in detections)
        {
            if (detection.Start < 0 || detection.Length <= 0 || detection.Start > text.Length - detection.Length)
            {
                throw new InvalidDataException("Le détecteur PII a retourné une plage invalide.");
            }
        }

        var structuredDetections = detections.Where(detection => detection.Kind != PiiDetectionKind.Model).ToArray();
        // Une détection modèle qui chevauche une règle d'une AUTRE catégorie est un faux positif probable
        // et on l'écarte. Une détection modèle qui chevauche une règle de la MÊME catégorie est conservée :
        // la fusion ci-dessous étend alors le masque à toute son étendue au lieu de la tronquer.
        var ranges = detections
            .Where(detection => detection.Kind != PiiDetectionKind.Model
                || !structuredDetections.Any(structured =>
                    structured.Category != detection.Category && Overlaps(detection, structured)))
            .GroupBy(detection => (detection.Start, detection.Length))
            .Select(group => group.OrderBy(detection => detection.Kind).First())
            .OrderBy(detection => detection.Start)
            .ThenByDescending(detection => detection.Length)
            .Select(detection => new MaskedRange(
                detection.Start,
                detection.Start + detection.Length,
                detection.Category))
            .ToList();

        var mergedRanges = new List<MaskedRange>(ranges.Count);
        foreach (var range in ranges)
        {
            if (mergedRanges.Count == 0 || range.Start >= mergedRanges[^1].End)
            {
                mergedRanges.Add(range);
                continue;
            }

            var previous = mergedRanges[^1];
            var category = previous.Category == range.Category || previous.End >= range.End
                ? previous.Category
                : PiiCategory.Unknown;
            mergedRanges[^1] = new MaskedRange(previous.Start, Math.Max(previous.End, range.End), category);
        }

        var result = new StringBuilder(text.Length);
        var cursor = 0;
        foreach (var range in mergedRanges)
        {
            result.Append(text, cursor, range.Start - cursor);
            result.Append(GetMarker(range.Category));
            cursor = range.End;
        }

        result.Append(text, cursor, text.Length - cursor);
        return result.ToString();
    }

    private static bool Overlaps(PiiDetection left, PiiDetection right) =>
        left.Start < right.Start + right.Length && right.Start < left.Start + left.Length;

    private static string GetMarker(PiiCategory category) => category switch
    {
        PiiCategory.Name => "[PERSON]",
        PiiCategory.Organization => "[ORGANIZATION]",
        PiiCategory.Email => "[EMAIL]",
        PiiCategory.Phone => "[PHONE]",
        PiiCategory.Address => "[ADDRESS]",
        PiiCategory.Iban => "[IBAN]",
        PiiCategory.Bic => "[BIC]",
        PiiCategory.Siret => "[SIRET]",
        PiiCategory.ProfessionalIdentifier => "[PROFESSIONAL_ID]",
        PiiCategory.DocumentNumber => "[DOCUMENT_ID]",
        PiiCategory.SocialNetwork => "[SOCIAL_NETWORK]",
        _ => "[PII]"
    };

    private readonly record struct MaskedRange(int Start, int End, PiiCategory Category);
}
