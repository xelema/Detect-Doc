namespace OcrComparison.Scoring;
using System.Text.RegularExpressions;

public static class TexteNormalise
{
    public static string Normaliser(string texte)
    {
        return Regex.Replace(texte, @"\s+", " ");
    }

    /// <summary>
    /// Normalise le texte de façon drastique (on ne garde qu'Unicode, chiffres et espaces)
    /// À utiliser uniquement pour le calcul du score OCR, pas dans le texte extrait final
    /// </summary>
    /// <param name="texte">Texte à normaliser</param>
    /// <returns></returns>
    public static string NormaliserPourScore(string texte)
    {
        var sansPonctuation = Regex.Replace(texte, @"[^\p{L}\p{N}\s]", "");
        return Normaliser(sansPonctuation);
    }
}