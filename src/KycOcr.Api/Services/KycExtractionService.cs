using KycOcr.Api.Models;

namespace KycOcr.Api.Services;

public class KycExtractionService
{
    private readonly TesseractOcrEngine _ocrEngine;
    private readonly LabelFieldExtractor _labelExtractor;

    private static readonly Dictionary<DocumentType, (string Field, string Label)[]> LabelMaps = new()
    {
        [DocumentType.NationalId] = new[]
        {
            ("IdNumber", "ID NUMBER"),
            ("FullName", "FULL NAME"),
            ("Nationality", "NATIONALITY"),
            ("Sex", "SEX"),
            ("DateOfBirth", "DATE OF BIRTH"),
            ("CardNumber", "CARD NUMBER"),
            ("Address", "ADDRESS"),
            ("DateOfIssue", "DATE OF ISSUE"),
            ("DateOfExpiry", "DATE OF EXPIRY"),
        },
        [DocumentType.Passport] = new[]
        {
            ("PassportNumber", "PASSPORT NO"),
            ("Surname", "SURNAME"),
            ("GivenNames", "GIVEN NAMES"),
            ("Nationality", "NATIONALITY"),
            ("DateOfBirth", "DATE OF BIRTH"),
            ("Sex", "SEX"),
            ("PlaceOfBirth", "PLACE OF BIRTH"),
            ("DateOfIssue", "DATE OF ISSUE"),
            ("DateOfExpiry", "DATE OF EXPIRY"),
            ("Authority", "AUTHORITY"),
        },
        [DocumentType.TradeLicense] = new[]
        {
            ("LicenseNumber", "LICENSE NUMBER"),
            ("DateOfIssue", "DATE OF ISSUE"),
            ("CompanyName", "COMPANY NAME"),
            ("LegalForm", "LEGAL FORM"),
            ("DateOfExpiry", "DATE OF EXPIRY"),
            ("CommercialRegistrationNo", "COMMERCIAL REGISTRATION NO"),
            ("AuthorizedCapital", "AUTHORIZED CAPITAL"),
            ("MainActivity", "MAIN ACTIVITY"),
            ("RegisteredAddress", "REGISTERED ADDRESS"),
            ("AuthorizedSignatory", "AUTHORIZED SIGNATORY"),
        },
    };

    public KycExtractionService(TesseractOcrEngine ocrEngine, LabelFieldExtractor labelExtractor)
    {
        _ocrEngine = ocrEngine;
        _labelExtractor = labelExtractor;
    }

    public ExtractionResponse Extract(DocumentType docType, byte[] imageBytes)
    {
        var ocr = _ocrEngine.Extract(imageBytes);
        var fields = _labelExtractor.Extract(ocr.Rows, LabelMaps[docType]);

        if (docType == DocumentType.Passport)
        {
            var mrz = MrzParser.TryParse(ocr.RawText);
            if (mrz != null)
            {
                // MRZ is fixed-format and far more reliable than the free-text
                // fields above it, so it wins where both were read.
                foreach (var (key, value) in mrz)
                    fields[key] = value;
            }
        }

        var expectedFieldCount = LabelMaps[docType].Length;
        var extractionRate = fields.Count / (double)expectedFieldCount;

        return new ExtractionResponse
        {
            DocumentType = docType.ToString(),
            Fields = fields,
            NeedsReview = extractionRate < 0.5,
            OcrConfidence = ocr.MeanConfidence,
            RawText = ocr.RawText,
        };
    }
}
