using System.Text;
using System.Xml;
using DetectDoc.Core.Engines;
using DocumentFormat.OpenXml;
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
                : string.Join(Environment.NewLine, Paragraphes(corps).Select(TexteParagraphe));
            return [new PageExtractionResult(1, texte, null)];
        }
        catch (Exception exception) when (exception
            is OpenXmlPackageException or FileFormatException or InvalidDataException or IOException or XmlException)
        {
            throw new InvalidDataException("Le document DOCX n'a pas pu être lu.", exception);
        }
    }

    // Descendants() entrerait dans mc:Fallback, qui répète le contenu de mc:Choice (zone de
    // texte en VML) : les paragraphes de secours seraient extraits une seconde fois.
    private static IEnumerable<W.Paragraph> Paragraphes(OpenXmlElement element)
    {
        foreach (var enfant in element.ChildElements)
        {
            if (enfant is AlternateContentFallback)
            {
                continue;
            }

            if (enfant is W.Paragraph paragraphe)
            {
                yield return paragraphe;
            }
            else
            {
                foreach (var descendant in Paragraphes(enfant))
                {
                    yield return descendant;
                }
            }
        }
    }

    // InnerText avalerait les tabulations et sauts de ligne, qui portent la structure du texte.
    private static string TexteParagraphe(W.Paragraph paragraphe)
    {
        var texte = new StringBuilder();
        AjouterTexte(paragraphe, texte);
        return texte.ToString();
    }

    private static void AjouterTexte(OpenXmlElement element, StringBuilder texte)
    {
        foreach (var enfant in element.ChildElements)
        {
            switch (enfant)
            {
                case AlternateContentFallback:
                    break;
                case W.Text texteRun:
                    texte.Append(texteRun.Text);
                    break;
                case W.TabChar:
                    texte.Append('\t');
                    break;
                case W.Break or W.CarriageReturn:
                    texte.Append('\n');
                    break;
                case W.NoBreakHyphen:
                    texte.Append('-');
                    break;
                case W.SoftHyphen:
                    texte.Append('\u00AD');
                    break;
                // w:sym porte un code dépendant de la police de symboles : ignoré faute de
                // correspondance fiable vers Unicode.
                case W.SymbolChar:
                    break;
                default:
                    AjouterTexte(enfant, texte);
                    break;
            }
        }
    }
}
