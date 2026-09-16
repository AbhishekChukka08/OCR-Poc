using KycOcr.Api.Models;

namespace KycOcr.Api.Services;

// One contract, multiple engines behind it (Tesseract today, Gemini as an
// explicitly-selected alternative) - the endpoint and response shape stay
// identical no matter which engine actually ran.
public interface IKycExtractor
{
    Task<ExtractionResponse> ExtractAsync(DocumentType docType, byte[] imageBytes);
}
