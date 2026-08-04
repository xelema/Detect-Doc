using F23.StringSimilarity;
using OcrComparison.Engines;
using OcrComparison.Manifest;
using OcrComparison.Scoring;

namespace OcrComparison.Benchmark;

public sealed class BenchmarkRunner(ITextExtractionEngine moteur, string cheminCorpus)
{
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
            NbMotsReference = 0,
            NbMotsExtrait = 0,
            ScoreLevenshtein = 0,
            ScoreJaccard = 0,
            Statut = Statut.Erreur
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

        try
        {
            var texteExtrait = moteur.Extract(Path.Combine(cheminCorpus, manifestEntry.Fichier));
            var texteExtraitNormalise = TexteNormalise.NormaliserPourScore(texteExtrait);

            if (string.IsNullOrWhiteSpace(texteExtraitNormalise))
                resultat = resultat with
                {
                    Statut = Statut.TexteVide,
                    NbMotsExtrait = 0
                };
            else
                resultat = resultat with
                {
                    Statut = Statut.Ok,
                    NbMotsExtrait = SimilariteMots.NombreDeMots(texteExtraitNormalise),
                    NbMotsReference = SimilariteMots.NombreDeMots(texteReferenceNormalise),
                    ScoreLevenshtein = 1 - _levenshtein.Distance(texteReferenceNormalise, texteExtraitNormalise),
                    ScoreJaccard = SimilariteMots.Jaccard(texteReferenceNormalise, texteExtraitNormalise)
                };
        }
        catch (Exception ex)
        {
            resultat = resultat with
            {
                Statut = Statut.Erreur,
                MessageErreur = $"{ex.GetType().Name}: {ex.Message}"
            };
        }

        return resultat;
    }
}