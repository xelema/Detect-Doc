namespace OcrComparison.Engines;

public interface ITextExtractionEngine
{
    string Name { get; }
    string Version { get; }
    IReadOnlyDictionary<string, object> Parametres { get; }
    string Extract(string pdfPath);
}