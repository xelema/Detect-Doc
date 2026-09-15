using System.Xml;
using DetectDoc.Core.Engines;
using DocumentFormat.OpenXml.Packaging;

namespace DetectDoc.Infrastructure.Engines;

// Texte natif DOCX uniquement : un scan collé dans Word ressort sans texte et suit le rejet
// « texte vide » commun à toutes les extractions.
public sealed class OpenXmlTextEngine : ITextExtractionEngine
{
    public string Name => "openxml";

    public IReadOnlyList<PageExtractionResult> Extract(
        Stream fluxDocument,
        CancellationToken cancellationToken = default)
    {
        var format = DocumentFormats.Detect(fluxDocument);
        if (format is not DocumentFileFormat.Docx)
        {
            throw new ArgumentException($"Le moteur {Name} ne lit que les DOCX.", "engine");
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var document = WordprocessingDocument.Open(fluxDocument, isEditable: false);
            var texte = string.Join(
                Environment.NewLine,
                document.MainDocumentPart?.Document?.Body?
                    .Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
                    .Select(paragraphe => paragraphe.InnerText) ?? []);
            return [new PageExtractionResult(1, texte, null)];
        }
        catch (Exception exception) when (exception
            is OpenXmlPackageException or FileFormatException or InvalidDataException or XmlException)
        {
            throw new InvalidDataException("Le document DOCX n'a pas pu être lu.", exception);
        }
    }
}
