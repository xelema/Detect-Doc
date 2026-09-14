using System.Globalization;
using System.Text;

public static class Metrics
{
    public static string BuildReport(string runId, RunMetadata metadata, IReadOnlyList<ResultDocument> results)
    {
        var submitted = metadata.Types.ToHashSet(StringComparer.Ordinal);
        var classified = results.Where(result => result.Status == "ok").ToList();
        var closed = classified.Where(result => submitted.Contains(result.ExpectedType)).ToList();
        var distractors = classified.Where(result => !submitted.Contains(result.ExpectedType)).ToList();
        var report = new StringBuilder();
        report.AppendLine($"# Run `{runId}`");
        report.AppendLine();
        report.AppendLine($"- **Date** : {metadata.Date:yyyy-MM-dd HH:mm:ss} UTC");
        report.AppendLine($"- **Corpus** : split `{metadata.Split}`, {results.Count} documents, manifest `{metadata.ManifestSha256[..12]}…`");
        report.AppendLine($"- **API** : `{metadata.ApiUrl}`");
        report.AppendLine($"- **Moteur** : `{metadata.Engine}`");
        report.AppendLine($"- **Types soumis** : {string.Join(", ", metadata.Types.Select(type => $"`{type}`"))}");
        report.AppendLine(metadata.DescriptionsSha256 is null
            ? "- **Descriptions** : non"
            : $"- **Descriptions** : oui, sha256 `{metadata.DescriptionsSha256[..12]}…`");
        report.AppendLine();
        report.AppendLine("## Résultats de l'API");
        report.AppendLine();
        report.AppendLine($"- Succès : {classified.Count}/{results.Count}");
        report.AppendLine($"- Erreurs : {results.Count - classified.Count}");
        report.AppendLine($"- Classables : {closed.Count}, distracteurs : {distractors.Count}");
        report.AppendLine();

        if (closed.Count > 0)
        {
            var accuracy = closed.Count(result => result.ExpectedType == result.PredictedType) / (double)closed.Count;
            var halfWidth = 1.96 * Math.Sqrt(accuracy * (1 - accuracy) / closed.Count);
            report.AppendLine("## Qualité");
            report.AppendLine();
            report.AppendLine("| Métrique | Valeur |");
            report.AppendLine("|---|---|");
            report.AppendLine($"| Exactitude globale | {Percent(accuracy)}, IC 95 % [{Percent(Math.Max(0, accuracy - halfWidth))}, {Percent(Math.Min(1, accuracy + halfWidth))}] |");
            report.AppendLine($"| Macro-F1 | {closed.GroupBy(result => result.ExpectedType).Average(group => F1(group.Key, closed)):F3} |");
            report.AppendLine();
            report.AppendLine("| Type | n | Exactitude | F1 |");
            report.AppendLine("|---|---|---|---|");
            foreach (var group in closed.GroupBy(result => result.ExpectedType).OrderBy(group => group.Key))
            {
                report.AppendLine($"| {group.Key} | {group.Count()} | {Percent(group.Count(result => result.ExpectedType == result.PredictedType) / (double)group.Count())} | {F1(group.Key, closed):F3} |");
            }
            report.AppendLine();
            AppendCoverage(report, closed);
            AppendCalibration(report, closed);
            AppendRegimes(report, closed);
        }

        report.AppendLine("## Distracteurs");
        report.AppendLine();
        report.AppendLine("| Seuil | Sous le seuil | Auto-classés à tort |");
        report.AppendLine("|---|---|---|");
        foreach (var threshold in new[] { 0.95, 0.90, 0.80 })
        {
            var below = distractors.Count(result => result.Confidence < threshold);
            report.AppendLine($"| {threshold:F2} | {below}/{distractors.Count} | {distractors.Count - below}/{distractors.Count} |");
        }
        report.AppendLine();
        AppendLatency(report, classified);
        report.AppendLine("## Limites");
        report.AppendLine();
        report.AppendLine("Les tokens, le coût Mistral, la version du flow et l'empreinte du prompt ne sont pas exposés par l'API. Ce run ne les déduit pas.");
        return report.ToString();
    }

    public static string BuildConfusionCsv(IReadOnlyList<ResultDocument> results, IReadOnlyList<string> types)
    {
        var closed = results.Where(result => result.Status == "ok" && types.Contains(result.ExpectedType, StringComparer.Ordinal)).ToList();
        var labels = types.Concat(closed.Select(result => result.PredictedType!).Where(type => !types.Contains(type, StringComparer.Ordinal))).Distinct(StringComparer.Ordinal).ToList();
        var csv = new StringBuilder("type_attendu");
        csv.AppendJoin(',', labels);
        csv.AppendLine();
        foreach (var expected in types)
        {
            csv.Append(expected);
            foreach (var predicted in labels)
            {
                csv.Append(',').Append(closed.Count(result => result.ExpectedType == expected && result.PredictedType == predicted));
            }
            csv.AppendLine();
        }
        return csv.ToString();
    }

    public static void RunSelfCheck()
    {
        var results = new[]
        {
            Ok("a", "rib", "rib", 0.95), Ok("b", "rib", "sepa", 0.85), Ok("c", "sepa", "sepa", 0.75), Ok("d", "facture", "rib", 0.10)
        };
        var closed = results.Where(result => result.ExpectedType != "facture").ToList();
        Assert(closed.Count == 3, "distracteur inclus dans la classification fermée");
        Assert(closed.Count(result => result.ExpectedType == result.PredictedType) == 2, "exactitude incorrecte");
        Assert(BuildConfusionCsv(results, ["rib", "sepa"]).Contains("rib,1,1"), "matrice incorrecte");
        Assert(closed.Count(result => result.Confidence >= 0.80) == 2, "seuil incorrect");
        Assert(results.Single(result => result.Id == "d").Confidence < 0.80, "distracteur incorrect");
        Assert(Ece(closed) > 0, "ECE incorrecte");
    }

    private static ResultDocument Ok(string id, string expected, string predicted, double confidence) =>
        new(id, $"{id}.pdf", expected, "scanne", "dev", "ok", predicted, confidence, "auto", 1, 1, 1, 1, null, null);

    private static void AppendCoverage(StringBuilder report, IReadOnlyList<ResultDocument> results)
    {
        report.AppendLine("## Couverture et confiance");
        report.AppendLine();
        report.AppendLine("| Seuil | Couverture | Erreur sur la partie auto-classée | À relire |");
        report.AppendLine("|---|---|---|---|");
        foreach (var threshold in new[] { 0.95, 0.90, 0.80, 0.0 })
        {
            var accepted = results.Where(result => result.Confidence >= threshold).ToList();
            var error = accepted.Count == 0 ? 0 : accepted.Count(result => result.ExpectedType != result.PredictedType) / (double)accepted.Count;
            report.AppendLine($"| {(threshold == 0 ? "aucun" : threshold.ToString("F2", CultureInfo.InvariantCulture))} | {Percent(accepted.Count / (double)results.Count)} | {Percent(error)} | {Percent(1 - accepted.Count / (double)results.Count)} |");
        }
        report.AppendLine();
    }

    private static void AppendCalibration(StringBuilder report, IReadOnlyList<ResultDocument> results)
    {
        report.AppendLine("### Fiabilité du score");
        report.AppendLine();
        report.AppendLine("| Tranche | Score moyen | Exactitude observée | Documents |");
        report.AppendLine("|---|---|---|---|");
        foreach (var (label, min, max) in Bins)
        {
            var bucket = results.Where(result => result.Confidence >= min && result.Confidence < max).ToList();
            report.AppendLine($"| {label} | {(bucket.Count == 0 ? "" : bucket.Average(result => result.Confidence!.Value).ToString("F3", CultureInfo.InvariantCulture))} | {(bucket.Count == 0 ? "" : Percent(bucket.Count(result => result.ExpectedType == result.PredictedType) / (double)bucket.Count))} | {bucket.Count} |");
        }
        report.AppendLine();
        report.AppendLine($"ECE : {Ece(results):F3}.");
        report.AppendLine();
    }

    private static void AppendRegimes(StringBuilder report, IReadOnlyList<ResultDocument> results)
    {
        report.AppendLine("### Exactitude par régime");
        report.AppendLine();
        report.AppendLine("| Régime | n | Exactitude |");
        report.AppendLine("|---|---|---|");
        foreach (var group in results.GroupBy(result => result.Regime).OrderBy(group => group.Key))
        {
            report.AppendLine($"| {group.Key} | {group.Count()} | {Percent(group.Count(result => result.ExpectedType == result.PredictedType) / (double)group.Count())} |");
        }
        report.AppendLine();
    }

    private static void AppendLatency(StringBuilder report, IReadOnlyList<ResultDocument> results)
    {
        report.AppendLine("## Latence retournée par l'API");
        report.AppendLine();
        report.AppendLine("| Étape | p50 | p95 |");
        report.AppendLine("|---|---|---|");
        foreach (var (name, values) in new[]
        {
            ("Extraction", results.Select(result => result.ExtractionMs!.Value)),
            ("Masquage PII", results.Select(result => result.PiiMaskingMs!.Value)),
            ("Classification", results.Select(result => result.ClassificationMs!.Value)),
            ("Total", results.Select(result => result.TotalMs!.Value))
        })
        {
            report.AppendLine($"| {name} | {Percentile(values, 0.50)} ms | {Percentile(values, 0.95)} ms |");
        }
        report.AppendLine();
    }

    private static readonly (string Label, double Min, double Max)[] Bins = [("0,9 à 1,0", 0.9, 1.0000001), ("0,8 à 0,9", 0.8, 0.9), ("0,7 à 0,8", 0.7, 0.8), ("moins de 0,7", 0, 0.7)];

    private static double F1(string type, IReadOnlyList<ResultDocument> results)
    {
        var truePositive = results.Count(result => result.ExpectedType == type && result.PredictedType == type);
        var falsePositive = results.Count(result => result.ExpectedType != type && result.PredictedType == type);
        var falseNegative = results.Count(result => result.ExpectedType == type && result.PredictedType != type);
        return truePositive == 0 ? 0 : 2d * truePositive / (2 * truePositive + falsePositive + falseNegative);
    }

    private static double Ece(IReadOnlyList<ResultDocument> results) => Bins.Sum(bin =>
    {
        var bucket = results.Where(result => result.Confidence >= bin.Min && result.Confidence < bin.Max).ToList();
        return bucket.Count == 0 ? 0 : bucket.Count / (double)results.Count * Math.Abs(bucket.Average(result => result.Confidence!.Value) - bucket.Count(result => result.ExpectedType == result.PredictedType) / (double)bucket.Count);
    });

    private static long Percentile(IEnumerable<long> values, double percentile)
    {
        var sorted = values.Order().ToList();
        return sorted[(int)Math.Ceiling(sorted.Count * percentile) - 1];
    }

    private static string Percent(double value) => value.ToString("P1", CultureInfo.GetCultureInfo("fr-FR"));
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Self-check échoué : {message}.");
    }
}
