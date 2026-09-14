using System.Text.RegularExpressions;
using DetectDoc.Core.Pii;

namespace DetectDoc.Infrastructure.Pii;

public sealed class RegexPiiDetector : IPiiDetector
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
    private const string SameLine = @"[^\S\n]+";
    private const string Word = @"\p{Lu}[\p{L}'’-]+";
    private const string Suffix = @"(?:BTP|Industrie|Compétences|Formations?|Conseil)\b";
    private const string Name = $@"(?:M\.|Mme|Mlle|Dr\.?)?[^\S\n]*{Word}(?:{SameLine}{Word}){{1,2}}";

    private static readonly (PiiCategory Category, string Rule, PiiDetectionKind Kind, Regex Pattern)[] Rules =
    [
        (PiiCategory.Email, "Email", PiiDetectionKind.Specification,
            Pattern(@"[\p{L}\p{N}._%+-]+@[\p{L}\p{N}.-]+(?:\r?\n[\p{L}\p{N}.-]+)?\.\p{L}{2,}")),
        (PiiCategory.Iban, "Iban", PiiDetectionKind.Specification,
            Pattern(@"\b[A-Z]{2}[0-9]{2}(?: ?[A-Z0-9]{4}){3,7} ?[A-Z0-9]{0,4}\b")),
        (PiiCategory.Phone, "TelephoneFr", PiiDetectionKind.Specification,
            Pattern(@"\b0[1-9](?:[ .-]?[0-9]{2}){4}\b")),
        (PiiCategory.Phone, "TelephoneInternational", PiiDetectionKind.Specification,
            Pattern(@"\+[0-9]{1,3}(?:[ .-]?[0-9]){8,11}\b")),
        (PiiCategory.Bic, "BicApresLibelle", PiiDetectionKind.Label,
            Pattern(@"\bBIC\b[^\p{L}\p{N}]{0,10}([A-Z]{6}[A-Z0-9]{2}(?:[A-Z0-9]{3})?)\b")),
        (PiiCategory.Siret, "SiretApresLibelle", PiiDetectionKind.Label,
            Pattern(@"\bSIRET\b[^\p{N}]{0,5}([0-9]{3} ?[0-9]{3} ?[0-9]{3} ?[0-9]{5})\b")),
        (PiiCategory.ProfessionalIdentifier, "RppsApresLibelle", PiiDetectionKind.Label,
            Pattern(@"\bRPPS\b[^\p{N}]{0,5}([0-9]{11})\b")),
        (PiiCategory.ProfessionalIdentifier, "AdeliApresLibelle", PiiDetectionKind.Label,
            Pattern(@"\bADELI\b[^\p{N}]{0,5}([0-9]{9})\b")),
        (PiiCategory.DocumentNumber, "RumApresLibelle", PiiDetectionKind.Label,
            Pattern(@"\bRUM\b[^\p{L}\p{N}]{0,5}([A-Z]{2,6}[0-9]{6,12})\b")),
        (PiiCategory.Address, "AdressePostale", PiiDetectionKind.Specification,
            Pattern(@"\b[0-9]{1,4}(?: ?(?i:bis|ter))?\s+(?i:rue|avenue|boulevard|chemin|impasse|place|all[ée]e|route|quai|cours|voie)\b.{0,60}?\b[0-9]{5}\b[ \t]*\p{Lu}[\p{L}'’-]*(?:[- ]\p{L}[\p{L}'’-]+){0,3}", RegexOptions.Singleline)),
        (PiiCategory.Name, "NomApresLibelle", PiiDetectionKind.Label,
            Pattern($@"(?i:titulaire du compte|titulaire|formateur|formatrice|stagiaire|contact|auteur de l'offre d'emploi|rencontrez)\s*:?\s*({Name})")),
        (PiiCategory.Name, "NomApresAttentionDe", PiiDetectionKind.Label,
            Pattern($@"(?i:à l['’]attention de)\s+({Name})")),
        (PiiCategory.Name, "NomApresCivilite", PiiDetectionKind.Label,
            Pattern($@"(?:M\.|Mme|Mlle|Dr\.?){SameLine}({Word}(?:{SameLine}{Word}){{0,2}})")),
        (PiiCategory.Name, "NomDansListeParticipants", PiiDetectionKind.Label,
            Pattern(@"(?<=(?i:participants?)\s*:[^\n]{0,300})\p{Lu}[\p{L}'’-]+\s+\p{Lu}[\p{L}'’-]+")),
        (PiiCategory.Organization, "OrganismeAvecPrefixe", PiiDetectionKind.Vocabulary,
            Pattern($"(?i:centre de formation|institut|académie|campus|groupe|cabinet){SameLine}{Word}(?:{SameLine}{Word})?")),
        (PiiCategory.Organization, "OrganismeAvecSuffixe", PiiDetectionKind.Vocabulary,
            Pattern($"{Word}{SameLine}{Suffix}(?:{SameLine}et{SameLine}{Suffix})?")),
        (PiiCategory.SocialNetwork, "ProfilLinkedIn", PiiDetectionKind.Specification,
            Pattern(@"\bin/[\p{L}\p{N}-]{3,60}\b"))
    ];

    public Task<IReadOnlyList<PiiDetection>> DetectAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return Task.FromResult(Detect(text));
    }

    private static IReadOnlyList<PiiDetection> Detect(string text)
    {
        var detections = new List<PiiDetection>();
        foreach (var (category, rule, kind, pattern) in Rules)
        {
            foreach (Match match in pattern.Matches(text))
            {
                var range = match.Groups.Count > 1 ? match.Groups[1] : match.Groups[0];
                detections.Add(new PiiDetection(category, rule, kind, range.Index, range.Length));
            }
        }

        return detections;
    }

    private static Regex Pattern(string pattern, RegexOptions options = RegexOptions.None) =>
        new(pattern, options | RegexOptions.CultureInvariant, Timeout);
}
