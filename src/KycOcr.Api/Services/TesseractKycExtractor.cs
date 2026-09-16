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

        // MRZ lines are long, dense, and can land right after any label
        // whose real value row got missed by OCR - without this, the label
        // matcher's "grab whatever's on the next line" rule could pick up a
        // raw MRZ string as an unrelated field's value. MRZ is parsed
        // separately below, so it's safe to exclude here.
        var rowsExcludingMrz = ocr.Rows
            .Select(r => new OcrRow(
                MrzParser.LooksLikeMrzLine(r.Left) ? "" : r.Left,
                MrzParser.LooksLikeMrzLine(r.Right) ? "" : r.Right))
            .ToList();

        var fields = _labelExtractor.Extract(rowsExcludingMrz, KycFieldSchema.FieldsByDocType[docType]);

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
