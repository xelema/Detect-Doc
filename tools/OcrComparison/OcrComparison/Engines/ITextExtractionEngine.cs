namespace OcrComparison.Engines;

public interface ITextExtractionEngine : IDisposable
{
    string Name { get; }
    string Version { get; }
    IReadOnlyDictionary<string, object> Parametres { get; }
    ResultatExtraction Extract(string cheminDocument);
}

/// <param name="Confiance">
/// Nulle quand le moteur n'a pas de notion de confiance, ce qui n'est pas la même chose qu'une
/// confiance de zéro : PdfPig lit le texte que le PDF porte déjà, il ne reconnaît rien.
/// </param>
/// <param name="RepliDeclenche">
/// Le moteur a dû passer par un chemin de rattrapage plus coûteux que sa pipeline normale. Le
/// benchmark l'enregistre sans savoir de quoi il s'agit, chaque moteur ayant les siens.
/// </param>
public readonly record struct ResultatExtraction(
    string Texte,
    float? Confiance,
    bool RepliDeclenche,
    double? DureeRenduPdfMs = null,
    double? DureeDecodagePngMs = null,
    double? DureeDetectMs = null);
