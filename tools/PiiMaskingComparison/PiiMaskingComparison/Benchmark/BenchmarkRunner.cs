using System.Diagnostics;
using PiiMaskingComparison.Engines;
using PiiMaskingComparison.Manifest;
using PiiMaskingComparison.VeriteTerrain;

namespace PiiMaskingComparison.Benchmark;

public sealed class BenchmarkRunner(IPiiDetectionEngine moteur)
{
    public IEnumerable<ResultatDocument> Executer(IEnumerable<ManifestEntry> manifestEntries)
    {
        return manifestEntries.Select(Mesurer);
    }

    private ResultatDocument Mesurer(ManifestEntry entree)
    {
        var texte = entree.TexteReference ?? string.Empty;

        // Sans texte de référence, il n'y a ni vérité terrain ni entrée à donner au moteur. Le
        // document reste dans les résultats pour que le corpus mesuré se recoupe ligne pour
        // ligne avec le corpus déclaré au manifest.
        if (string.IsNullOrWhiteSpace(texte))
        {
            return Creer(entree, Statut.ReferenceAbsente, [], [], [], 0, 0, 0);
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
            return Creer(entree, Statut.Erreur, [], [], nonLocalisees, 0,
                Stopwatch.GetElapsedTime(horodatage).TotalMilliseconds,
                GC.GetAllocatedBytesForCurrentThread() - allouesAvant,
                $"{ex.GetType().Name}: {ex.Message}");
        }

        var duree = Stopwatch.GetElapsedTime(horodatage).TotalMilliseconds;
        var alloues = GC.GetAllocatedBytesForCurrentThread() - allouesAvant;
        var (occurrences, fauxPositifs) = Confronter(texte, attendues, detections);

        return Creer(entree, Statut.Ok, occurrences, fauxPositifs, nonLocalisees, detections.Count, duree, alloues);
    }

    /// <summary>
    /// Confronte plages attendues et plages détectées, caractère par caractère : deux moteurs
    /// peuvent couvrir la même valeur avec des bornes différentes, seule la couverture compte.
    /// </summary>
    private static (List<OccurrencePii>, List<DetectionFausse>) Confronter(
        string texte, List<OccurrenceAttendue> attendues, IReadOnlyList<Detection> detections)
    {
        var masque = Peindre(texte.Length, detections.Select(d => (d.Start, d.Length)));
        var sensible = Peindre(texte.Length, attendues.Select(a => (a.Start, a.Length)));

        var occurrences = attendues
            .Select(a => new OccurrencePii(a.Categorie, a.Cle, a.Valeur, a.Start, a.Length,
                Couverte: Tout(masque, a.Start, a.Length)))
            .ToList();

        // Un faux positif est une détection qui ne touche aucune PII attendue. Une détection qui
        // déborde d'une valeur réelle masque du texte en trop, pas une personne inventée : elle
        // relève des bornes de la règle, pas de sa précision.
        var fauxPositifs = detections
            .Where(d => !AuMoinsUn(sensible, d.Start, d.Length))
            .Select(d => new DetectionFausse(d.Categorie, d.Regle, texte.Substring(d.Start, d.Length)))
            .ToList();

        return (occurrences, fauxPositifs);
    }

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

    private static bool Tout(bool[] peint, int start, int length) =>
        Enumerable.Range(start, length).All(i => i < peint.Length && peint[i]);

    private static bool AuMoinsUn(bool[] peint, int start, int length) =>
        Enumerable.Range(start, length).Any(i => i < peint.Length && peint[i]);

    private ResultatDocument Creer(ManifestEntry entree, Statut statut, List<OccurrencePii> occurrences,
        List<DetectionFausse> fauxPositifs, List<ValeurNonLocalisee> nonLocalisees, int nbDetections,
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
            FauxPositifs = fauxPositifs,
            ValeursNonLocalisees = nonLocalisees,
            NbDetections = nbDetections,
            DureeMs = dureeMs,
            OctetsAlloues = octetsAlloues
        };
    }
}
