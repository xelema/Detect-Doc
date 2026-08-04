namespace OcrComparison.Scoring;
using System.Text.RegularExpressions;

public static partial class TexteNormalise
{
    public static string Normaliser(string texte)
    {
        return MyRegex().Replace(texte, " ");
    }

    /// <summary>
    /// Normalise le texte de façon drastique (on ne garde qu'Unicode, chiffres et espaces)
    /// À utiliser uniquement pour le calcul du score OCR, pas dans le texte extrait final
    /// </summary>
    /// <param name="texte">Texte à normaliser</param>
    /// <returns></returns>
    public static string NormaliserPourScore(string texte)
    {
        var sansPonctuation = MyRegex1().Replace(texte, "");
        return Normaliser(sansPonctuation);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex MyRegex();
    [GeneratedRegex(@"[^\p{L}\p{N}\s]")]
    private static partial Regex MyRegex1();
}