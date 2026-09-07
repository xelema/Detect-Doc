namespace DetectDoc.Core.Engines;

/// <param name="Confiance">
/// Nulle quand le moteur n'a pas de notion de confiance, ce qui n'est pas la même chose qu'une
/// confiance de zéro : PdfPig lit le texte que le PDF porte déjà, il ne reconnaît rien.
/// </param>
public readonly record struct ResultatExtraction(
    string Texte,
    float? Confiance);

public interface ITextExtractionEngine : IDisposable
{
    string Name { get; }
    string Version { get; }
    IReadOnlyDictionary<string, object> Parametres { get; }
    ResultatExtraction Extract(Stream fluxDocument);
}
