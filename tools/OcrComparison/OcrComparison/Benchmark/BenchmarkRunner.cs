using System.Diagnostics;
using F23.StringSimilarity;
using OcrComparison.Engines;
using OcrComparison.Manifest;
using OcrComparison.Scoring;

namespace OcrComparison.Benchmark;

public sealed class BenchmarkRunner(ITextExtractionEngine moteur, string cheminCorpus)
{
    // Déclenche le repli à quatre orientations sans avoir besoin d'un texte de référence, qui
    // n'existe pas sur un document réel. Mesuré le 07/08/2026 (Page.GetMeanConfidence, sans
    // rapport avec le jaccard) sur neuf documents du split dev : les six à bon score tombent
    // entre 0,85 et 0,935, les trois à mauvais score entre 0,43 et 0,72. Un vide net entre 0,72
    // et 0,85 sépare les deux groupes, 0,80 s'y place avec de la marge des deux côtés.
    private const float SeuilConfianceExtraction = 0.80f;

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
            var cheminDocument = Path.Combine(cheminCorpus, manifestEntry.Fichier);

            // Repli à 4 orientations réservé à Tesseract. PdfPig lit le texte natif.
            if (moteur is TesseractOcrEngine moteurTesseract)
            {
                var (texte, confiance) = moteurTesseract.ExtraireAvecConfiance(cheminDocument);
                texteExtrait = texte;

                if (!string.IsNullOrWhiteSpace(texteExtrait) && confiance < SeuilConfianceExtraction)
                {
                    // Jaccard calculé uniquement pour logging/contrôle POC (aucun impact sur la décision).
                    var jaccardAvant = SimilariteMots.Jaccard(texteReferenceNormalise,
                        TexteNormalise.NormaliserPourScore(texteExtrait));
                    Console.WriteLine(
                        $"{manifestEntry.Id}: confiance OCR {confiance:F4} sous le seuil de {SeuilConfianceExtraction}, tentative des quatre orientations (jaccard avant repli : {jaccardAvant:F4})");
                    texteExtrait = moteurTesseract.ExtractMeilleureOrientation(cheminDocument);
                }
            }
            else
            {
                texteExtrait = moteur.Extract(cheminDocument);
            }
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
            return Creer(manifestEntry, Statut.TexteVide, nbMotsReference, ressources, texteExtrait: texteExtrait);
        }

        var texteExtraitNormalise = TexteNormalise.NormaliserPourScore(texteExtrait);

        return Creer(manifestEntry, Statut.Ok, nbMotsReference, ressources,
            nbMotsExtrait: SimilariteMots.NombreDeMots(texteExtraitNormalise),
            texteExtrait: texteExtrait,
            scoreLevenshtein: 1 - _levenshtein.Distance(texteReferenceNormalise, texteExtraitNormalise),
            scoreJaccard: SimilariteMots.Jaccard(texteReferenceNormalise, texteExtraitNormalise));
    }

    /// <summary>
    /// Assemble le résultat d'un document. Les champs communs viennent tous du manifest, les
    /// autres sont ceux que le chemin de sortie a réellement mesuré : ce qu'il n'a pas mesuré
    /// reste à sa valeur neutre, sans être écrit puis réécrit. Les textes portés sont les
    /// versions brutes, pas celles normalisées pour le score : une relecture humaine du jsonl
    /// veut la ponctuation et la casse d'origine, le calcul du score s'en passe déjà ailleurs.
    /// </summary>
    private ResultatDocument Creer(ManifestEntry manifestEntry, Statut statut, int nbMotsReference,
        Ressources ressources, int nbMotsExtrait = 0, string? texteExtrait = null, double scoreLevenshtein = 0,
        double scoreJaccard = 0, string? messageErreur = null)
    {
        return new ResultatDocument
        {
            Moteur = moteur.Name,
            Id = manifestEntry.Id,
            TypeAttendu = manifestEntry.TypeAttendu,
            ScoreLevenshtein = scoreLevenshtein,
            ScoreJaccard = scoreJaccard,
            Statut = statut,
            MessageErreur = messageErreur,
            Regime = manifestEntry.Regime,
            Split = manifestEntry.Split,
            Pages = manifestEntry.Pages,
            NbMotsReference = nbMotsReference,
            NbMotsExtrait = nbMotsExtrait,
            TexteReference = manifestEntry.TexteReference,
            TexteExtrait = texteExtrait,
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
