using System.Diagnostics;
using F23.StringSimilarity;
using OcrComparison.Engines;
using OcrComparison.Manifest;
using OcrComparison.Scoring;

namespace OcrComparison.Benchmark;

public sealed class BenchmarkRunner(ITextExtractionEngine moteur, string cheminCorpus)
{
    private static readonly Process Processus = Process.GetCurrentProcess();
    private readonly NormalizedLevenshtein _levenshtein = new();

    public IEnumerable<ResultatDocument> Executer(IEnumerable<ManifestEntry> manifestEntries)
    {
        return manifestEntries.Select(Mesurer);
    }

    private ResultatDocument Mesurer(ManifestEntry manifestEntry)
    {
        var texteReferenceNormalise = TexteNormalise.NormaliserPourScore(manifestEntry.TexteReference);

        // Sans référence, il n'y a rien à comparer : l'extraction n'est pas lancée, donc aucune
        // ressource n'est relevée. Le document reste dans le résultat pour que le corpus mesuré
        // et le corpus déclaré au manifest se recoupent ligne pour ligne.
        if (string.IsNullOrWhiteSpace(texteReferenceNormalise))
        {
            return Creer(manifestEntry, Statut.ReferenceAbsente, nbMotsReference: 0, Ressources.Aucune);
        }

        var nbMotsReference = SimilariteMots.NombreDeMots(texteReferenceNormalise);

        var cpuAvant = Processus.TotalProcessorTime;
        var horodatage = Stopwatch.GetTimestamp();

        string? texteExtrait = null;
        string? erreur = null;

        try
        {
            texteExtrait = moteur.Extract(Path.Combine(cheminCorpus, manifestEntry.Fichier));
        }
        catch (Exception ex)
        {
            erreur = $"{ex.GetType().Name}: {ex.Message}";
        }

        var duree = Stopwatch.GetElapsedTime(horodatage);
        var cpu = Processus.TotalProcessorTime - cpuAvant;
        Processus.Refresh();
        var ressources = new Ressources(duree.TotalMilliseconds, cpu.TotalMilliseconds,
            Processus.PeakWorkingSet64, Processus.WorkingSet64);

        // Une extraction qui échoue ou qui ne rend rien garde ses ressources : le temps passé
        // avant l'échec fait partie de ce que coûte le moteur sur ce corpus.
        if (erreur != null)
        {
            return Creer(manifestEntry, Statut.Erreur, nbMotsReference, ressources, messageErreur: erreur);
        }

        if (string.IsNullOrWhiteSpace(texteExtrait))
        {
            return Creer(manifestEntry, Statut.TexteVide, nbMotsReference, ressources);
        }

        var texteExtraitNormalise = TexteNormalise.NormaliserPourScore(texteExtrait);

        return Creer(manifestEntry, Statut.Ok, nbMotsReference, ressources,
            nbMotsExtrait: SimilariteMots.NombreDeMots(texteExtraitNormalise),
            scoreLevenshtein: 1 - _levenshtein.Distance(texteReferenceNormalise, texteExtraitNormalise),
            scoreJaccard: SimilariteMots.Jaccard(texteReferenceNormalise, texteExtraitNormalise));
    }

    /// <summary>
    /// Assemble le résultat d'un document. Les champs communs viennent tous du manifest, les
    /// autres sont ceux que le chemin de sortie a réellement mesuré : ce qu'il n'a pas mesuré
    /// reste à sa valeur neutre, sans être écrit puis réécrit.
    /// </summary>
    private ResultatDocument Creer(ManifestEntry manifestEntry, Statut statut, int nbMotsReference,
        Ressources ressources, int nbMotsExtrait = 0, double scoreLevenshtein = 0,
        double scoreJaccard = 0, string? messageErreur = null)
    {
        return new ResultatDocument
        {
            Moteur = moteur.Name,
            Id = manifestEntry.Id,
            TypeAttendu = manifestEntry.TypeAttendu,
            Regime = manifestEntry.Regime,
            Split = manifestEntry.Split,
            Pages = manifestEntry.Pages,
            NbMotsReference = nbMotsReference,
            NbMotsExtrait = nbMotsExtrait,
            ScoreLevenshtein = scoreLevenshtein,
            ScoreJaccard = scoreJaccard,
            Statut = statut,
            MessageErreur = messageErreur,
            DureeMs = ressources.DureeMs,
            CpuMs = ressources.CpuMs,
            PicMemoireOctets = ressources.PicMemoireOctets,
            MemoireOctets = ressources.MemoireOctets
        };
    }

    /// <summary>Ressources relevées autour d'une extraction.</summary>
    private readonly record struct Ressources(
        double DureeMs, double CpuMs, long PicMemoireOctets, long MemoireOctets)
    {
        /// <summary>Aucune extraction lancée, donc rien à relever.</summary>
        public static readonly Ressources Aucune = new(0, 0, 0, 0);
    }
}
