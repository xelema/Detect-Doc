using System.Globalization;
using System.Text;
using System.Globalization;
using System.Text;
using System.Text.Json;
using PiiMaskingComparison.Engines;
using PiiMaskingComparison.Manifest;

namespace PiiMaskingComparison.VeriteTerrain;

public readonly record struct OccurrenceAttendue(CategoriePii Categorie, string Cle, string Valeur, int Start, int Length);

public readonly record struct ValeurAbsente(CategoriePii Categorie, string Cle, string Valeur);

/// <summary>Localise dans le texte de référence les valeurs que le manifest déclare y avoir injectées.</summary>
public static class VeriteTerrainDocument
{
    // Tableau explicite plutôt qu'heuristique sur le nom de la clé : `contact_client` et
    // `titulaire` désignent des personnes sans que leur nom le dise, et une clé ajoutée au
    // générateur doit ressortir en Inconnue au lieu de se diluer dans une catégorie fourre-tout.
    private static readonly Dictionary<string, CategoriePii> CategorieParCle = new()
    {
        ["nom_complet"] = CategoriePii.Nom,
        ["nom_formateur"] = CategoriePii.Nom,
        ["noms"] = CategoriePii.Nom,
        ["noms_participants"] = CategoriePii.Nom,
        ["contact_client"] = CategoriePii.Nom,
        ["contact_nom_complet"] = CategoriePii.Nom,
        ["contact_prenom"] = CategoriePii.Nom,
        ["titulaire"] = CategoriePii.Nom,
        ["organisme"] = CategoriePii.Organisme,
        ["raison_sociale_emetteur"] = CategoriePii.Organisme,
        ["designation_client"] = CategoriePii.Organisme,
        ["email"] = CategoriePii.Email,
        ["email_emetteur"] = CategoriePii.Email,
        ["telephone_mobile"] = CategoriePii.Telephone,
        ["telephone_fixe"] = CategoriePii.Telephone,
        ["telephone_emetteur"] = CategoriePii.Telephone,
        ["adresse"] = CategoriePii.Adresse,
        ["adresse_titulaire"] = CategoriePii.Adresse,
        ["iban"] = CategoriePii.Iban,
        ["bic"] = CategoriePii.Bic,
        ["siret_emetteur"] = CategoriePii.Siret,
        ["rpps"] = CategoriePii.IdentifiantProfessionnel,
        ["adeli"] = CategoriePii.IdentifiantProfessionnel,
        ["rum"] = CategoriePii.NumeroDocument,
        ["reseau_social"] = CategoriePii.ReseauSocial
    };

    /// <summary>
    /// Une valeur introuvable sort en <see cref="ValeurAbsente"/> plutôt que de compter comme
    /// manquée : ce que le texte ne contient pas n'est pas un échec du détecteur.
    /// </summary>
    public static (List<OccurrenceAttendue> Attendues, List<ValeurAbsente> Absentes) Construire(
        ManifestEntry entree, string texte)
    {
        var attendues = new List<OccurrenceAttendue>();
        var absentes = new List<ValeurAbsente>();
        var (compact, indices) = Compacter(texte);

        foreach (var (cle, valeurJson) in entree.PiiInjectees ?? new Dictionary<string, JsonElement>())
        {
            var categorie = CategorieParCle.GetValueOrDefault(cle, CategoriePii.Inconnue);
            foreach (var valeur in Valeurs(valeurJson))
            {
                var occurrences = Localiser(texte, compact, indices, valeur)
                    .Select(o => new OccurrenceAttendue(categorie, cle, valeur, o.Start, o.Length))
                    .ToList();

                if (occurrences.Count == 0)
                {
                    absentes.Add(new ValeurAbsente(categorie, cle, valeur));
                    continue;
                }

                attendues.AddRange(occurrences);
            }
        }

        return (Maximales(attendues), absentes);
    }
    
    /// <summary>
    /// Une valeur entièrement contenue dans une autre ne compte pas à part : le nom du titulaire
    /// se retrouve dans son adresse de courriel et la raison sociale dans le domaine. Ce sont les
    /// mêmes caractères et une seule fuite ; les compter deux fois gonflerait autant le nombre
    /// d'occurrences attendues que celui des occurrences couvertes.
    /// </summary>
    private static List<OccurrenceAttendue> Maximales(List<OccurrenceAttendue> occurrences)
    {
        return occurrences
            .Where(o => !occurrences.Any(autre =>
                (autre.Start, autre.Length) != (o.Start, o.Length)
                && autre.Start <= o.Start
                && autre.Start + autre.Length >= o.Start + o.Length))
            .DistinctBy(o => (o.Start, o.Length))
            .ToList();
    }


    public static bool EstNonSignifiant(char c) => !char.IsLetterOrDigit(c);

    private static IEnumerable<string> Valeurs(JsonElement valeur) =>
        (valeur.ValueKind switch
        {
            JsonValueKind.Array => valeur.EnumerateArray().Select(e => e.GetString()),
            _ => [valeur.GetString()]
        })
        .OfType<string>()
        .Where(v => v.Trim().Length > 0);

    private static (string Compact, int[] Indices) Compacter(string texte)
    {
        var compact = new char[texte.Length];
        var indices = new int[texte.Length];
        var n = 0;

        for (var i = 0; i < texte.Length; i++)
        {
            if (EstNonSignifiant(texte[i]))
            {
                continue;
            }

            compact[n] = Normaliser(texte[i]);
            indices[n] = i;
            n++;
        }

        return (new string(compact, 0, n), indices[..n]);
    }

    // La comparaison se fait sur le texte compacté et en minuscules : le manifest porte l'IBAN
    // d'un seul tenant quand le document l'imprime par groupes de quatre, l'adresse `12 rue X,
    // 75000 Ville` s'imprime sur deux lignes sans virgule, et une carte de visite écrit le nom de
    // famille en capitales. La plage rendue, elle, est celle du texte d'origine.
    private static IEnumerable<(int Start, int Length)> Localiser(
        string texte, string compact, int[] indices, string valeur)
    {
        var recherche = new string(valeur.Where(c => !EstNonSignifiant(c)).Select(Normaliser).ToArray());
        if (recherche.Length == 0)
        {
            yield break;
        }

        var depuis = 0;
        while (depuis <= compact.Length - recherche.Length)
        {
            var trouve = compact.IndexOf(recherche, depuis, StringComparison.Ordinal);
            if (trouve < 0)
            {
                yield break;
            }

            var apres = trouve + recherche.Length;
            var start = indices[trouve];
            var fin = indices[apres - 1];

            // Une valeur prise au milieu d'un mot ou d'un nombre plus long n'est pas la valeur
            // cherchée : le numéro de déclaration d'activité d'un organisme contient les chiffres
            // d'un ADELI, et deux mots voisins se recollent une fois la ponctuation retirée. Le
            // voisinage se juge sur le texte d'origine, seul endroit où la séparation existe.
            if (!Colle(texte, start - 1) && !Colle(texte, fin + 1))
            {
                yield return (start, fin - start + 1);
            }

            depuis = apres;
        }
    }

    /// <summary>
    /// Minuscule et sans accent : un logo imprimé `ACADEMIE ROUEN` porte la même raison sociale
    /// que le manifest, les capitales françaises perdant couramment leurs accents.
    /// </summary>
    private static char Normaliser(char c)
    {
        var minuscule = char.ToLowerInvariant(c);
        if (minuscule < 128)
        {
            return minuscule;
        }

        foreach (var decompose in minuscule.ToString().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(decompose) != UnicodeCategory.NonSpacingMark)
            {
                return decompose;
            }
        }

        return minuscule;
    }


    private static bool Colle(string texte, int position) =>
        position >= 0 && position < texte.Length && char.IsLetterOrDigit(texte[position]);
}
