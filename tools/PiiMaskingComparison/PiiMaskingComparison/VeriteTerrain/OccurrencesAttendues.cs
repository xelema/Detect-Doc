using System.Text.Json;
using PiiMaskingComparison.Engines;
using PiiMaskingComparison.Manifest;

namespace PiiMaskingComparison.VeriteTerrain;

/// <param name="Cle">Clé du manifest d'où vient la valeur, conservée pour le diagnostic.</param>
public readonly record struct OccurrenceAttendue(CategoriePii Categorie, string Cle, string Valeur, int Start, int Length);

/// <summary>Valeur injectée que le texte de référence ne porte pas sous cette forme.</summary>
public readonly record struct ValeurAbsente(CategoriePii Categorie, string Cle, string Valeur);

/// <summary>
/// Construit la vérité terrain d'un document : où se trouvent, dans le texte de référence, les
/// valeurs que le générateur déclare y avoir injectées.
/// </summary>
/// <remarks>
/// La recherche ignore les blancs des deux côtés : le manifest porte l'IBAN d'un seul tenant
/// quand le document l'imprime par groupes de quatre, et un téléphone change de séparateurs
/// d'un gabarit à l'autre. Ce qui reste introuvable sort en <see cref="ValeurAbsente"/> plutôt
/// que de compter comme une PII manquée : une valeur que le texte ne contient pas n'est pas un
/// échec du détecteur. La casse, elle, est respectée, donc un nom imprimé en capitales alors
/// que le manifest le porte en casse mixte est déclaré absent.
/// </remarks>
public static class VeriteTerrainDocument
{
    public static (List<OccurrenceAttendue> Attendues, List<ValeurAbsente> Absentes) Construire(
        ManifestEntry entree, string texte)
    {
        var attendues = new List<OccurrenceAttendue>();
        var absentes = new List<ValeurAbsente>();
        var (compact, indices) = Compacter(texte);

        foreach (var (cle, valeurJson) in entree.PiiInjectees ?? new Dictionary<string, JsonElement>())
        {
            var categorie = CategorieDe(cle);
            foreach (var valeur in Valeurs(valeurJson))
            {
                var occurrences = Localiser(compact, indices, valeur)
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

        return (attendues, absentes);
    }

    /// <summary>
    /// La catégorie se déduit du nom de la clé : le générateur en utilise plus de vingt, dont
    /// `email_emetteur` et `telephone_mobile`, qui décrivent le rôle du champ, pas sa nature.
    /// </summary>
    private static CategoriePii CategorieDe(string cle) => cle switch
    {
        _ when cle.Contains("email") => CategoriePii.Email,
        _ when cle.Contains("iban") => CategoriePii.Iban,
        _ when cle.Contains("telephone") => CategoriePii.Telephone,
        _ => CategoriePii.Autre
    };

    private static IEnumerable<string> Valeurs(JsonElement valeur) =>
        (valeur.ValueKind switch
        {
            JsonValueKind.Array => valeur.EnumerateArray().Select(e => e.GetString()),
            _ => [valeur.GetString()]
        })
        .OfType<string>()
        .Where(v => v.Trim().Length > 0);

    /// <summary>Texte privé de ses blancs, et l'indice d'origine de chaque caractère conservé.</summary>
    private static (string Compact, int[] Indices) Compacter(string texte)
    {
        var compact = new char[texte.Length];
        var indices = new int[texte.Length];
        var n = 0;

        for (var i = 0; i < texte.Length; i++)
        {
            if (char.IsWhiteSpace(texte[i]))
            {
                continue;
            }

            compact[n] = texte[i];
            indices[n] = i;
            n++;
        }

        return (new string(compact, 0, n), indices[..n]);
    }

    private static IEnumerable<(int Start, int Length)> Localiser(string compact, int[] indices, string valeur)
    {
        var recherche = new string(valeur.Where(c => !char.IsWhiteSpace(c)).ToArray());
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

            // La plage rendue est celle du texte d'origine, blancs internes compris : c'est elle
            // que le masquage devra couvrir entièrement.
            var start = indices[trouve];
            var fin = indices[trouve + recherche.Length - 1];
            yield return (start, fin - start + 1);

            depuis = trouve + recherche.Length;
        }
    }
}
