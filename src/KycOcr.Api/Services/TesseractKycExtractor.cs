using KycOcr.Api.Models;

namespace KycOcr.Api.Services;

public class TesseractKycExtractor : IKycExtractor
{
    private readonly TesseractOcrEngine _ocrEngine;
    private readonly LabelFieldExtractor _labelExtractor;

    public TesseractKycExtractor(TesseractOcrEngine ocrEngine, LabelFieldExtractor labelExtractor)
    {
        _ocrEngine = ocrEngine;
        _labelExtractor = labelExtractor;
    }

    public Task<ExtractionResponse> ExtractAsync(DocumentType docType, byte[] imageBytes)
    {
        var ocr = _ocrEngine.Extract(imageBytes);
        var fields = _labelExtractor.Extract(ocr.Rows, KycFieldSchema.FieldsByDocType[docType]);

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

        var response = new ExtractionResponse
        {
            DocumentType = docType.ToString(),
            Engine = "tesseract",
            Fields = fields,
            NeedsReview = KycFieldSchema.ComputeNeedsReview(docType, fields),
            OcrConfidence = ocr.MeanConfidence,
            RawText = ocr.RawText,
        };
        return Task.FromResult(response);
    }
}
