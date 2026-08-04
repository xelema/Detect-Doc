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
        var listeMotsReference = reference.Split(' ').ToList();
        var listeMotsExtrait = extrait.Split(' ').ToList();
        
        var occurrencesMotsReference = listeMotsReference.GroupBy(w => w).
            ToDictionary(g => g.Key, g => g.Count());
        
        var occurrencesMotsExtrait = listeMotsExtrait.GroupBy(w => w).
            ToDictionary(g => g.Key,  g => g.Count());

        var intersectionSize = 0;
        var unionSize = 0;

        var allWords = occurrencesMotsExtrait.Keys.Union(occurrencesMotsReference.Keys);

        foreach (var words in allWords)
        {
            var nbMotsRef = occurrencesMotsReference.GetValueOrDefault(words, 0);
            var nbMotsExtrait = occurrencesMotsExtrait.GetValueOrDefault(words, 0);

            intersectionSize += Math.Min(nbMotsRef, nbMotsExtrait);
            unionSize += Math.Max(nbMotsRef, nbMotsExtrait);
        }

        return unionSize == 0 ? 1.0 : (double)intersectionSize / unionSize;
    }
}
