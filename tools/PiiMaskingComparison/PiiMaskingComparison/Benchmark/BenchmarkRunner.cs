using System.Diagnostics;
using PiiMaskingComparison.Engines;
using PiiMaskingComparison.Manifest;
using PiiMaskingComparison.VeriteTerrain;

namespace PiiMaskingComparison.Benchmark;

public sealed class BenchmarkRunner(IPiiDetectionEngine moteur)
{
    /// <summary>
    /// Au-delà d'un document à la fois, le moteur est sollicité en parallèle : c'est la
    /// situation d'une API qui garde son modèle chargé et reçoit plusieurs requêtes. Les
    /// résultats sont rendus dans l'ordre du manifest, quel que soit l'ordre d'arrivée.
    /// </summary>
    public IReadOnlyList<ResultatDocument> Executer(IReadOnlyList<ManifestEntry> manifestEntries, int concurrence)
    {
        if (concurrence <= 1)
        {
            return manifestEntries.Select(Mesurer).ToList();
        }

        var resultats = new ResultatDocument[manifestEntries.Count];
        Parallel.For(0, manifestEntries.Count, new ParallelOptions { MaxDegreeOfParallelism = concurrence },
            i => resultats[i] = Mesurer(manifestEntries[i]));

        return resultats;
    }

    private ResultatDocument Mesurer(ManifestEntry entree)
    {
        var texte = entree.TexteReference ?? string.Empty;

        // Le document sans texte reste dans les résultats pour que le corpus mesuré se recoupe
        // ligne pour ligne avec le corpus déclaré au manifest.
        if (string.IsNullOrWhiteSpace(texte))
        {
            return Creer(entree, Statut.ReferenceAbsente, [], [], [], 0, 0);
        }

        var (attendues, absentes) = VeriteTerrainDocument.Construire(entree, texte);
        var nonLocalisees = absentes
            .Select(a => new ValeurNonLocalisee(a.Categorie, a.Cle, a.Valeur))
            .ToList();

        var allouesAvant = GC.GetAllocatedBytesForCurrentThread();
        var horodatage = Stopwatch.GetTimestamp();

        IReadOnlyList<Detection> detections;
        try
        {
            detections = moteur.Detecter(texte);
        }
        catch (Exception ex)
        {
            // Le temps passé avant l'échec fait partie de ce que coûte le moteur sur ce corpus.
            return Creer(entree, Statut.Erreur, [], [], nonLocalisees,
                Stopwatch.GetElapsedTime(horodatage).TotalMilliseconds,
                GC.GetAllocatedBytesForCurrentThread() - allouesAvant,
                $"{ex.GetType().Name}: {ex.Message}");
        }

        var duree = Stopwatch.GetElapsedTime(horodatage).TotalMilliseconds;
        var alloues = GC.GetAllocatedBytesForCurrentThread() - allouesAvant;
        var (occurrences, enregistrees) = Confronter(texte, attendues, detections);

        return Creer(entree, Statut.Ok, occurrences, enregistrees, nonLocalisees, duree, alloues);
    }

    /// <summary>
    /// Confronte plages attendues et plages détectées caractère par caractère : deux moteurs
    /// peuvent couvrir la même valeur avec des bornes différentes, seule la couverture compte.
    /// </summary>
    private static (List<OccurrencePii>, List<DetectionEnregistree>) Confronter(
        string texte, List<OccurrenceAttendue> attendues, IReadOnlyList<Detection> detections)
    {
        var masque = Peindre(texte.Length, detections.Select(d => (d.Start, d.Length)));
        var masqueHorsVocabulaire = Peindre(texte.Length, detections
            .Where(d => d.Nature != NatureRegle.Vocabulaire)
            .Select(d => (d.Start, d.Length)));
        var sensible = Peindre(texte.Length, attendues.Select(a => (a.Start, a.Length)));

        var occurrences = attendues
            .Select(a => new OccurrencePii(a.Categorie, a.Cle, a.Valeur, a.Start, a.Length,
                Couverte: Signifiants(texte, a.Start, a.Length).All(i => masque[i]),
                CouverteHorsVocabulaire: Signifiants(texte, a.Start, a.Length).All(i => masqueHorsVocabulaire[i])))
            .ToList();

        // Est fausse la détection qui ne touche aucune PII attendue. Celle qui déborde d'une
        // valeur réelle masque du texte en trop, pas une personne inventée.
        var enregistrees = detections
            .Select(d => new DetectionEnregistree(d.Categorie, d.Regle, d.Nature, d.Start, d.Length,
                texte.Substring(d.Start, d.Length),
                Fausse: !Signifiants(texte, d.Start, d.Length).Any(i => sensible[i])))
            .ToList();

        return (occurrences, enregistrees);
    }

    // Une adresse imprimée sur deux lignes est couverte par deux détections, et le retour à la
    // ligne resté en clair entre elles ne révèle rien.
    private static IEnumerable<int> Signifiants(string texte, int start, int length) =>
        Enumerable.Range(start, length)
            .Where(i => i < texte.Length && !VeriteTerrainDocument.EstNonSignifiant(texte[i]));

    private static bool[] Peindre(int longueur, IEnumerable<(int Start, int Length)> plages)
    {
        var peint = new bool[longueur];
        foreach (var (start, length) in plages)
        {
            for (var i = start; i < start + length && i < longueur; i++)
            {
                peint[i] = true;
            }
        }

        return peint;
    }

    private ResultatDocument Creer(ManifestEntry entree, Statut statut, List<OccurrencePii> occurrences,
        List<DetectionEnregistree> detections, List<ValeurNonLocalisee> nonLocalisees,
        double dureeMs, long octetsAlloues, string? messageErreur = null)
    {
        return new ResultatDocument
        {
            Moteur = moteur.Name,
            Id = entree.Id,
            TypeAttendu = entree.TypeAttendu,
            Split = entree.Split,
            Statut = statut,
            MessageErreur = messageErreur,
            Occurrences = occurrences,
            Detections = detections,
            ValeursNonLocalisees = nonLocalisees,
            DureeMs = dureeMs,
            OctetsAlloues = octetsAlloues
        };
    }
}
