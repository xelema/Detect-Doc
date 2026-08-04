namespace OcrComparison.Scoring;

public static class SimilariteMots
{
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