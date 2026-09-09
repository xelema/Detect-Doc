namespace DetectDoc.Api;

internal static class DocumentTypes
{
    // La liste vient de l'appelant et sert telle quelle au classifieur : un blanc ou un doublon
    // rendrait la réponse impossible à rattacher à un type demandé. On rejette au lieu de corriger.
    public static string[]? Normalize(string[]? types)
    {
        var normalises = types?
            .Select(type => type.Trim())
            .Where(type => type.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return normalises is { Length: > 0 } && normalises.Length == types!.Length ? normalises : null;
    }
}
