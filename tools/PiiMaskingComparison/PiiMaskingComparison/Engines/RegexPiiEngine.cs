using System.Text.RegularExpressions;

namespace PiiMaskingComparison.Engines;

/// <summary>
/// Baseline de référence : des formes qui se suffisent à elles-mêmes, IBAN ou courriel, et des
/// valeurs annoncées par un libellé du document. Sans son libellé, un BIC est huit majuscules et
/// un ADELI neuf chiffres, deux formes trop banales pour être masquées à l'aveugle.
/// </summary>
public sealed class RegexPiiEngine : IPiiDetectionEngine
{
    public string Name => "regex";
    public string Version => "2";

    private const int TimeoutMs = 2000;
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(TimeoutMs);

    // Blanc horizontal : un nom ou une raison sociale ne se poursuit pas à la ligne suivante.
    private const string MemeLigne = @"[^\S\n]+";
    private const string Mot = @"\p{Lu}[\p{L}'’-]+";

    // Vocabulaire du générateur, pas règle de langue : ces suffixes ne se transposeront pas à des
    // documents de production.
    private const string Suffixe = @"(?:BTP|Industrie|Compétences|Formations?|Conseil)\b";

    private const string Nom = $@"(?:M\.|Mme|Mlle|Dr\.?)?[^\S\n]*{Mot}(?:{MemeLigne}{Mot}){{1,2}}";

    private static readonly (CategoriePii Categorie, string Regle, Regex Motif)[] Regles =
    [
        // Le retour à la ligne toléré dans le domaine vient d'une carte de visite du corpus, qui
        // coupe l'adresse sur un trait d'union en fin de ligne.
        (CategoriePii.Email, "Email",
            Motif(@"[\p{L}\p{N}._%+-]+@[\p{L}\p{N}.-]+(?:\r?\n[\p{L}\p{N}.-]+)?\.\p{L}{2,}")),

        // Les documents impriment l'IBAN par groupes de quatre, le manifest sans espace.
        (CategoriePii.Iban, "Iban", Motif(@"\b[A-Z]{2}[0-9]{2}(?: ?[A-Z0-9]{4}){3,7} ?[A-Z0-9]{0,4}\b")),

        (CategoriePii.Telephone, "TelephoneFr", Motif(@"\b0[1-9](?:[ .-]?[0-9]{2}){4}\b")),
        (CategoriePii.Telephone, "TelephoneInternational", Motif(@"\+[0-9]{1,3}(?:[ .-]?[0-9]){8,11}\b")),

        (CategoriePii.Bic, "BicApresLibelle",
            Motif(@"\bBIC\b[^\p{L}\p{N}]{0,10}([A-Z]{6}[A-Z0-9]{2}(?:[A-Z0-9]{3})?)\b")),
        (CategoriePii.Siret, "SiretApresLibelle",
            Motif(@"\bSIRET\b[^\p{N}]{0,5}([0-9]{3} ?[0-9]{3} ?[0-9]{3} ?[0-9]{5})\b")),
        (CategoriePii.IdentifiantProfessionnel, "RppsApresLibelle",
            Motif(@"\bRPPS\b[^\p{N}]{0,5}([0-9]{11})\b")),
        (CategoriePii.IdentifiantProfessionnel, "AdeliApresLibelle",
            Motif(@"\bADELI\b[^\p{N}]{0,5}([0-9]{9})\b")),
        (CategoriePii.NumeroDocument, "RumApresLibelle",
            Motif(@"\bRUM\b[^\p{L}\p{N}]{0,5}([A-Z]{2,6}[0-9]{6,12})\b")),

        // L'adresse passe souvent à la ligne avant son code postal, d'où le corps paresseux ; il
        // est borné court, une règle courant jusqu'au prochain code postal avalerait un paragraphe.
        (CategoriePii.Adresse, "AdressePostale",
            Motif(@"\b[0-9]{1,4}(?: ?(?i:bis|ter))?\s+(?i:rue|avenue|boulevard|chemin|impasse|place|all[ée]e|route|quai|cours|voie)\b.{0,60}?\b[0-9]{5}\b[ \t]*\p{Lu}[\p{L}'’-]*(?:[- ]\p{L}[\p{L}'’-]+){0,3}",
                RegexOptions.Singleline)),

        (CategoriePii.Nom, "NomApresLibelle",
            Motif($@"(?i:titulaire du compte|titulaire|formateur|formatrice|stagiaire|contact|auteur de l'offre d'emploi|rencontrez)\s*:?\s*({Nom})")),
        (CategoriePii.Nom, "NomApresAttentionDe", Motif($@"(?i:à l['’]attention de)\s+({Nom})")),
        (CategoriePii.Nom, "NomApresCivilite", Motif($@"(?:M\.|Mme|Mlle|Dr\.?){MemeLigne}({Mot}(?:{MemeLigne}{Mot}){{0,2}})")),

        // Le regard arrière borné limite la portée du libellé aux noms de la même énumération.
        (CategoriePii.Nom, "NomDansListeParticipants",
            Motif(@"(?<=(?i:participants?)\s*:[^\n]{0,300})\p{Lu}[\p{L}'’-]+\s+\p{Lu}[\p{L}'’-]+")),

        (CategoriePii.Organisme, "OrganismeAvecPrefixe",
            Motif($@"(?i:centre de formation|institut|académie|campus|groupe|cabinet){MemeLigne}{Mot}(?:{MemeLigne}{Mot})?")),
        (CategoriePii.Organisme, "OrganismeAvecSuffixe",
            Motif($@"{Mot}{MemeLigne}{Suffixe}(?:{MemeLigne}et{MemeLigne}{Suffixe})?")),

        (CategoriePii.ReseauSocial, "ProfilLinkedIn", Motif(@"\bin/[\p{L}\p{N}-]{3,60}\b"))
    ];

    public IReadOnlyDictionary<string, object> Parametres { get; } = new Dictionary<string, object>
    {
        ["regles"] = string.Join(", ", Regles.Select(r => r.Regle)),
        ["categories_visees"] = string.Join(", ", Regles.Select(r => r.Categorie).Distinct().Order()),
        ["timeout_ms"] = TimeoutMs
    };

    public IReadOnlyList<Detection> Detecter(string texte)
    {
        var detections = new List<Detection>();

        foreach (var (categorie, regle, motif) in Regles)
        {
            foreach (Match correspondance in motif.Matches(texte))
            {
                // Les règles à libellé capturent la valeur seule dans le groupe 1 : le libellé
                // reste du texte ordinaire, il n'a pas à être masqué.
                var plage = correspondance.Groups.Count > 1 ? correspondance.Groups[1] : correspondance.Groups[0];
                detections.Add(new Detection(categorie, plage.Index, plage.Length, regle));
            }
        }

        return detections;
    }

    private static Regex Motif(string motif, RegexOptions options = RegexOptions.None) =>
        new(motif, options | RegexOptions.CultureInvariant, Timeout);

    public void Dispose()
    {
    }
}
