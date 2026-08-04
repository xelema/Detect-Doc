namespace OcrComparison.Scoring;
using System.Text.RegularExpressions;

public static partial class TexteNormalise
{
    public static string Normaliser(string texte)
    {
        return EspacesMultiples().Replace(texte, " ").Trim();
    }

    /// <summary>
    /// Normalise le texte de façon drastique (on ne garde que lettres, chiffres et espaces)
    /// À utiliser uniquement pour le calcul du score OCR, pas dans le texte extrait final
    /// </summary>
    /// <remarks>
    /// La ponctuation est remplacée par une espace et non supprimée : l'OCR et la couche texte
    /// du PDF ne placent pas les mêmes espaces autour d'un signe, et une suppression sèche
    /// souderait « Total:1250 » d'un côté face à « Total 1250 » de l'autre, faisant chuter le
    /// score de Jaccard pour une différence qui n'en est pas une.
    /// </remarks>
    /// <param name="texte">Texte à normaliser</param>
    /// <returns>Le texte réduit aux lettres, aux chiffres et à des espaces simples, sans espace en bordure.</returns>
    public static string NormaliserPourScore(string texte)
    {
        var sansPonctuation = HorsLettresEtChiffres().Replace(texte, " ");
        return Normaliser(sansPonctuation);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex EspacesMultiples();

    [GeneratedRegex(@"[^\p{L}\p{N}\s]")]
    private static partial Regex HorsLettresEtChiffres();
}
