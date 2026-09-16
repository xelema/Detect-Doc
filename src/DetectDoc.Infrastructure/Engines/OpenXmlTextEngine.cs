using System.Text;
using System.Xml;
using DetectDoc.Core.Engines;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

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
            var corps = document.MainDocumentPart?.Document?.Body;
            var texte = corps is null
                ? string.Empty
                : string.Join(Environment.NewLine, corps.Descendants<W.Paragraph>().Select(TexteParagraphe));
            return [new PageExtractionResult(1, texte, null)];
        }
        catch (Exception exception) when (exception
            is OpenXmlPackageException or FileFormatException or InvalidDataException or IOException or XmlException)
        {
            throw new InvalidDataException("Le document DOCX n'a pas pu être lu.", exception);
        }
    }

    // InnerText avalerait les tabulations et sauts de ligne, qui portent la structure du texte.
    private static string TexteParagraphe(W.Paragraph paragraphe)
    {
        var texte = new StringBuilder();
        foreach (var element in paragraphe.Descendants())
        {
            switch (element)
            {
                case W.Text texteRun:
                    texte.Append(texteRun.Text);
                    break;
                case W.TabChar:
                    texte.Append('\t');
                    break;
                case W.Break or W.CarriageReturn:
                    texte.Append('\n');
                    break;
            }
        }

        return texte.ToString();
    }
}
