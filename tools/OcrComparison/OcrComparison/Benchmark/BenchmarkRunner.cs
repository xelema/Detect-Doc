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
        var texteReferenceNormalise = TexteNormalise.NormaliserPourScore(manifestEntry.TextReference);
        var resultat = new ResultatDocument
        {
            Moteur = moteur.Name,
            Id = manifestEntry.Id,
            TypeAttendu = manifestEntry.TypeAttendu,
            Regime = manifestEntry.Regime,
            Split = manifestEntry.Split,
            Pages = manifestEntry.Pages,
            NbMotsReference = SimilariteMots.NombreDeMots(texteReferenceNormalise),
            NbMotsExtrait = 0,
            ScoreLevenshtein = 0,
            ScoreJaccard = 0,
            Statut = Statut.Erreur,
            DureeMs = 0,
            CpuMs = 0,
            PicMemoireOctets = 0,
            MemoireOctets = 0
        };

        if (string.IsNullOrWhiteSpace(texteReferenceNormalise))
        {
            resultat = resultat with
            {
                Statut = Statut.ReferenceAbsente,
                NbMotsReference = 0
            };
            return resultat;
        }

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

        var duree = Stopwatch.GetElapsedTime(horodatage); // TimeSpan
        var cpu = Processus.TotalProcessorTime - cpuAvant; // TimeSpan
        Processus.Refresh();
        var pic = Processus.PeakWorkingSet64;
        var courant = Processus.WorkingSet64;

        resultat = resultat with
        {
            DureeMs = duree.TotalMilliseconds,
            CpuMs = cpu.TotalMilliseconds,
            PicMemoireOctets = pic,
            MemoireOctets = courant
        };

        if (erreur != null)
        {
            resultat = resultat with
            {
                Statut = Statut.Erreur,
                MessageErreur = erreur
            };
            return resultat;
        }

        if (string.IsNullOrWhiteSpace(texteExtrait))
        {
            resultat = resultat with
            {
                Statut = Statut.TexteVide,
                NbMotsExtrait = 0
            };
            return resultat;
        }

        var texteExtraitNormalise = TexteNormalise.NormaliserPourScore(texteExtrait);

        resultat = resultat with
        {
            Statut = Statut.Ok,
            NbMotsExtrait = SimilariteMots.NombreDeMots(texteExtraitNormalise),
            ScoreLevenshtein = 1 - _levenshtein.Distance(texteReferenceNormalise, texteExtraitNormalise),
            ScoreJaccard = SimilariteMots.Jaccard(texteReferenceNormalise, texteExtraitNormalise)
        };

        return resultat;
    }
}