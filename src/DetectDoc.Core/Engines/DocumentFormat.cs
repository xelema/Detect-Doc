namespace DetectDoc.Core.Engines;

// DocumentFileFormat et non DocumentFormat : ce dernier est l'espace de noms racine du
// package DocumentFormat.OpenXml et masquerait l'enum partout où le package est référencé.
public enum DocumentFileFormat
{
    Pdf,
    Image,
    Docx
}

// Un format inconnu n'est pas un document corrompu : l'exception reste distincte
// d'InvalidDataException pour que la couche HTTP rende 415 et non 422.
public sealed class UnsupportedDocumentFormatException()
    : Exception("Le format du document n'est pas pris en charge.");
