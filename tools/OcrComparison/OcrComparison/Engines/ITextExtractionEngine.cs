namespace OcrComparison.Engines;

public interface ITextExtractionEngine
{
    string Name { get; }
    string Extract(string pdfPath);
}