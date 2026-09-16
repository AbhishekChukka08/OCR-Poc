using System.Text;
using System.Text.Json.Nodes;
using KycOcr.Api.Models;

namespace KycOcr.Api.Services;

// Alternative to TesseractKycExtractor: instead of OCR text + label
// matching, sends the image straight to a Gemini vision model and asks it
// to read the fields directly. Selected explicitly via engine=gemini on the
// same endpoint - not an automatic fallback - so it's easy to compare the
// two side by side.
public class GeminiKycExtractor : IKycExtractor
{
    private const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/interactions";

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;

    private static readonly Dictionary<DocumentType, string> DocTypeDescriptions = new()
    {
        [DocumentType.NationalId] = "national identity card",
        [DocumentType.Passport] = "passport bio-data page (the printed fields and/or the machine-readable zone at the bottom)",
        [DocumentType.TradeLicense] = "commercial/trade license certificate",
    };

    public GeminiKycExtractor(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = configuration["Gemini:ApiKey"]
            ?? throw new InvalidOperationException("Gemini:ApiKey is not configured. Run: dotnet user-secrets set \"Gemini:ApiKey\" \"...\"");
        _model = configuration["Gemini:Model"] ?? "gemini-3.1-flash-lite";
    }

    public async Task<ExtractionResponse> ExtractAsync(DocumentType docType, byte[] imageBytes)
    {
        var fieldMap = KycFieldSchema.FieldsByDocType[docType];
        var requestBody = BuildRequest(docType, fieldMap, imageBytes);

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Add("x-goog-api-key", _apiKey);
        request.Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");

        using var httpResponse = await _httpClient.SendAsync(request);
        var responseText = await httpResponse.Content.ReadAsStringAsync();

        if (!httpResponse.IsSuccessStatusCode)
            throw new InvalidOperationException($"Gemini API call failed ({(int)httpResponse.StatusCode}): {responseText}");

        var fields = ParseFields(responseText);

        return new ExtractionResponse
        {
            DocumentType = docType.ToString(),
            Engine = "gemini",
            Fields = fields,
            NeedsReview = KycFieldSchema.ComputeNeedsReview(docType, fields),
            OcrConfidence = 1f, // not meaningful for a model-based read - kept only for response-shape parity
            RawText = responseText,
        };
    }

    private JsonObject BuildRequest(DocumentType docType, (string Field, string Label)[] fieldMap, byte[] imageBytes)
    {
        var docDescription = DocTypeDescriptions[docType];
        var fieldHints = string.Join("\n", fieldMap.Select(f => $"- {f.Field} - labeled \"{f.Label}\" on the document"));

        var prompt = $"""
            You are extracting structured KYC data from a photo of a {docDescription}.
            Carefully read the document (including any machine-readable zone, if present)
            and return exactly the fields listed below as a JSON object.
            If a field is not visible, not legible, or not present on this document, use
            an empty string "" for its value - never guess or invent a value.

            Fields to extract:
            {fieldHints}
            """;

        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var (field, _) in fieldMap)
        {
            properties[field] = new JsonObject { ["type"] = "string" };
            required.Add(field);
        }

        return new JsonObject
        {
            ["model"] = _model,
            ["input"] = new JsonArray
            {
                new JsonObject { ["type"] = "text", ["text"] = prompt },
                new JsonObject
                {
                    ["type"] = "image",
                    ["data"] = Convert.ToBase64String(imageBytes),
                    ["mime_type"] = DetectMimeType(imageBytes),
                },
            },
            ["response_format"] = new JsonObject
            {
                ["type"] = "text",
                ["mime_type"] = "application/json",
                ["schema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = properties,
                    ["required"] = required,
                },
            },
        };
    }

    private static string DetectMimeType(byte[] bytes)
    {
        if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return "image/png";
        return "image/jpeg";
    }

    private static Dictionary<string, string> ParseFields(string responseJson)
    {
        var root = JsonNode.Parse(responseJson)?.AsObject()
            ?? throw new InvalidOperationException("Gemini response was not valid JSON.");

        var steps = root["steps"]?.AsArray()
            ?? throw new InvalidOperationException("Gemini response had no 'steps' array.");

        var modelOutputText = steps
            .Select(step => step?.AsObject())
            .Where(step => step is not null && step["type"]?.GetValue<string>() == "model_output")
            .Select(step => step!["content"]?.AsArray()?.FirstOrDefault()?["text"]?.GetValue<string>())
            .FirstOrDefault(text => text is not null);

        if (modelOutputText is null)
            throw new InvalidOperationException("Gemini response had no model_output step with text content.");

        var fieldsJson = JsonNode.Parse(modelOutputText)?.AsObject()
            ?? throw new InvalidOperationException("Gemini's structured output was not valid JSON.");

        var fields = new Dictionary<string, string>();
        foreach (var (key, value) in fieldsJson)
        {
            var text = value?.GetValue<string>()?.Trim();
            if (!string.IsNullOrEmpty(text))
                fields[key] = text;
        }
        return fields;
    }
}
