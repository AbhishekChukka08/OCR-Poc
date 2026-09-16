using KycOcr.Api.Models;

namespace KycOcr.Api.Services;

// The field set each document type is expected to yield, shared by every
// extraction engine (Tesseract label-matching, Gemini, ...) so they all
// target the same response shape and "how complete was this?" logic.
public static class KycFieldSchema
{
    public static readonly Dictionary<DocumentType, (string Field, string Label)[]> FieldsByDocType = new()
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

    // Same "was this actually a good read?" rule for every engine: for a KYC
    // document, a silently-missing field is not acceptable just because most
    // of the document came through fine - ANY expected field missing means
    // a human needs to check this before it's trusted.
    public static bool ComputeNeedsReview(DocumentType docType, Dictionary<string, string> fields)
    {
        var expectedFieldCount = FieldsByDocType[docType].Length;
        return fields.Count < expectedFieldCount;
    }
}
