namespace OcrComparison.Benchmark;

public sealed record ResultatDocument
{
    public required string Moteur { get; init; }
    public required string Id { get; init; }
    public required string TypeAttendu { get; init; }
    public required double ScoreLevenshtein { get; init; }
    public required double ScoreJaccard { get; init; }
    public required Statut Statut { get; init; }
    public string? MessageErreur { get; init; }
    public float? ConfianceOcr { get; init; } // uniquement Tesseract, nul sinon (ex. PdfPig)
    public bool RepliDeclenche { get; init; } // l'OCR a été rejoué dans les quatre orientations
    public required string Regime { get; init; }
    public required string Split { get; init; }
    public required int Pages { get; init; }
    public required int NbMotsReference { get; init; }
    public required int NbMotsExtrait { get; init; }
    public string? TexteReference { get; init; }
    public string? TexteExtrait { get; init; }
    public required double DureeMs { get; init; } // temps écoulé
    public required double CpuMs {  get; init; } // temps processeur consommé pendant l'extraction
    public double? DureeRenduPdfMs { get; init; }
    public double? DureeDecodagePngMs { get; init; }
    public double? DureeDetectMs { get; init; }
    public required long PicMemoireOctets { get; init; } // plus haut working set observé pendant l'extraction
    public required long MemoireAvantOctets { get; init; } // working set juste avant, référence du coût propre
}

public enum Statut
{
    Ok,
    ReferenceAbsente,
    TexteVide,
    Erreur
}
