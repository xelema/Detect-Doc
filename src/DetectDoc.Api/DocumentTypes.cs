using DetectDoc.Core.Classification;

namespace DetectDoc.Api;

internal static class DocumentTypes
{
    // La liste vient de l'appelant et sert telle quelle au classifieur : un id blanc ou nul et un
    // doublon rendraient la réponse impossible à rattacher à un type demandé. On rejette au lieu de
    // corriger. Le tableau désérialisé peut contenir des null (`["RIB", null]`) ou des objets sans
    // id (`[{"libelle":"RIB"}]`) que l'annotation de nullabilité ne suffit pas à écarter.
    public static TypeDocumentaire[]? Normalize(IReadOnlyList<TypeDocumentaire?>? types)
    {
        if (types is not { Count: > 0 } || types.Any(type => string.IsNullOrWhiteSpace(type?.Id)))
        {
            return null;
        }

        var normalises = types
            .Select(type => new TypeDocumentaire(
                type!.Id.Trim(),
                string.IsNullOrWhiteSpace(type.Libelle) ? null : type.Libelle.Trim(),
                string.IsNullOrWhiteSpace(type.Description) ? null : type.Description.Trim()))
            .ToArray();

        return normalises.Select(type => type.Id).Distinct(StringComparer.Ordinal).Count() == normalises.Length
            ? normalises
            : null;
    }
}
