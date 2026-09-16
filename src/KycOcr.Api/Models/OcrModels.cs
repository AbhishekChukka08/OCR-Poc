namespace KycOcr.Api.Models;

public enum DocumentType
{
    NationalId,
    Passport,
    TradeLicense
}

public class ExtractionResponse
{
    public string DocumentType { get; set; } = "";
    public Dictionary<string, string> Fields { get; set; } = new();

    // True when too few of the expected fields for this document type were
    // read. The caller (SPFx form) should ask the user to retake the photo
    // rather than trust a mostly-empty extraction.
    public bool NeedsReview { get; set; }
    public float OcrConfidence { get; set; }
    public string RawText { get; set; } = "";
}
