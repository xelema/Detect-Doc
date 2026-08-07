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
        // Le manifest vient d'un outil externe : rien ne garantit que chaque ligne porte la clé
        // texte_reference, et une entrée sans référence doit sortir en ReferenceAbsente, pas
        // faire tomber le run entier depuis un point qu'aucun try/catch ne couvre.
        var texteReferenceNormalise =
            TexteNormalise.NormaliserPourScore(manifestEntry.TexteReference ?? string.Empty);

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
        float? confianceOcr = null;
        var repliDeclenche = false;

        try
        {
            var cheminDocument = Path.Combine(cheminCorpus, manifestEntry.Fichier);

            // Repli à 4 orientations réservé à Tesseract. PdfPig lit le texte natif.
            if (moteur is TesseractOcrEngine moteurTesseract)
            {
                var (texte, confiance) = moteurTesseract.ExtraireAvecConfiance(cheminDocument);
                texteExtrait = texte;
                confianceOcr = confiance;

                // Un texte vide vaut un repli au même titre qu'un texte peu sûr : c'est le
                // symptôme le plus franc d'une page à l'envers, et il n'a justement aucune
                // confiance à confronter au seuil.
                if (string.IsNullOrWhiteSpace(texteExtrait)
                    || confiance < TesseractOcrEngine.SeuilConfianceExtraction)
                {
                    Console.WriteLine(
                        $"{manifestEntry.Id}: confiance OCR {confiance:F4} sous le seuil de {TesseractOcrEngine.SeuilConfianceExtraction}, tentative des quatre orientations");
                    repliDeclenche = true;
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
            return Creer(manifestEntry, Statut.Erreur, nbMotsReference, ressources, messageErreur: erreur,
                confianceOcr: confianceOcr, repliDeclenche: repliDeclenche);
        }

        if (string.IsNullOrWhiteSpace(texteExtrait))
        {
            return Creer(manifestEntry, Statut.TexteVide, nbMotsReference, ressources, texteExtrait: texteExtrait,
                confianceOcr: confianceOcr, repliDeclenche: repliDeclenche);
        }

        var texteExtraitNormalise = TexteNormalise.NormaliserPourScore(texteExtrait);

        return Creer(manifestEntry, Statut.Ok, nbMotsReference, ressources,
            nbMotsExtrait: SimilariteMots.NombreDeMots(texteExtraitNormalise),
            texteExtrait: texteExtrait,
            scoreLevenshtein: 1 - _levenshtein.Distance(texteReferenceNormalise, texteExtraitNormalise),
            scoreJaccard: SimilariteMots.Jaccard(texteReferenceNormalise, texteExtraitNormalise),
            confianceOcr: confianceOcr, repliDeclenche: repliDeclenche);
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
        double scoreJaccard = 0, string? messageErreur = null, float? confianceOcr = null,
        bool repliDeclenche = false)
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
            ConfianceOcr = confianceOcr,
            RepliDeclenche = repliDeclenche,
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
