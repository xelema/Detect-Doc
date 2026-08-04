namespace OcrComparison.Scoring;

public static class SimilariteMots
{
    /// <summary>
    /// Calcule la similarité de Jaccard entre le texte de référence et le texte extrait.
    /// Chaque mot est pondéré par son nombre d'occurrences, afin de tenir compte des
    /// répétitions dans le texte. Retourne 1 lorsque les deux textes sont vides.
    /// </summary>
    /// <param name="reference">Texte de référence attendu.</param>
    /// <param name="extrait">Texte produit par l'extraction.</param>
    /// <returns>Le rapport entre la taille de l'intersection et celle de l'union des mots.
    /// (1 = identique, 0 = totalement différent)</returns>
    public static double Jaccard(string reference, string extrait)
    {
        // RemoveEmptyEntries : une espace en bordure ou doublée produirait un mot vide, qui
        // compterait alors comme un mot commun de plus et gonflerait le score.
        var occurrencesMotsReference = CompterMots(reference);
        var occurrencesMotsExtrait = CompterMots(extrait);

        var tailleIntersection = 0;
        var tailleUnion = 0;

        var tousLesMots = occurrencesMotsExtrait.Keys.Union(occurrencesMotsReference.Keys);

        foreach (var mot in tousLesMots)
        {
            var nbMotsReference = occurrencesMotsReference.GetValueOrDefault(mot, 0);
            var nbMotsExtrait = occurrencesMotsExtrait.GetValueOrDefault(mot, 0);

            tailleIntersection += Math.Min(nbMotsReference, nbMotsExtrait);
            tailleUnion += Math.Max(nbMotsReference, nbMotsExtrait);
        }

        return tailleUnion == 0 ? 1.0 : (double)tailleIntersection / tailleUnion;
    }

    private static Dictionary<string, int> CompterMots(string texte)
    {
        return texte.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .GroupBy(mot => mot)
            .ToDictionary(groupe => groupe.Key, groupe => groupe.Count());
    }
}
