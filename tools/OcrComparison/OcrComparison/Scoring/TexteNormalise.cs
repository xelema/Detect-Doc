namespace OcrComparison.Scoring;
using System.Text.RegularExpressions;

public static class TexteNormalise
{
    public static string Normaliser(string texte)
    {
        return Regex.Replace(texte, @"\s+", " ");
    }
}