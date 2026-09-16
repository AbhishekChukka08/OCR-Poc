using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using KycOcr.Api.Models;

namespace KycOcr.Api.Services;

// Alternative to TesseractKycExtractor: sends the image straight to an
// OpenAI vision model and asks it to read the fields directly. Selected
// explicitly via engine=openai on the same endpoint - not an automatic
// fallback - so it's easy to compare against the Tesseract path.
public class OpenAiKycExtractor : IKycExtractor
{
    private const string Endpoint = "https://api.openai.com/v1/chat/completions";

    // Pricing for gpt-4o-mini, per OpenAI's published rate card: $0.15 per
    // 1M input tokens, $0.60 per 1M output tokens. Update these if the
    // configured model changes.
    private const decimal InputCostPerMillionTokens = 0.15m;
    private const decimal OutputCostPerMillionTokens = 0.60m;

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;

    private static readonly Dictionary<DocumentType, string> DocTypeDescriptions = new()
    {
        [DocumentType.NationalId] = "national identity card",
        [DocumentType.Passport] = "passport bio-data page (the printed fields and/or the machine-readable zone at the bottom)",
        [DocumentType.TradeLicense] = "commercial/trade license certificate",
    };

    public OpenAiKycExtractor(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = configuration["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured. Run: dotnet user-secrets set \"OpenAI:ApiKey\" \"...\"");
        _model = configuration["OpenAI:Model"] ?? "gpt-4o-mini";
    }

    public async Task<ExtractionResponse> ExtractAsync(DocumentType docType, byte[] imageBytes)
    {
        var fieldMap = KycFieldSchema.FieldsByDocType[docType];
        var requestBody = BuildRequest(docType, fieldMap, imageBytes);

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json");

        using var httpResponse = await _httpClient.SendAsync(request);
        var responseText = await httpResponse.Content.ReadAsStringAsync();

        if (!httpResponse.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenAI API call failed ({(int)httpResponse.StatusCode}): {responseText}");

        var root = JsonNode.Parse(responseText)?.AsObject()
            ?? throw new InvalidOperationException("OpenAI response was not valid JSON.");

        var fields = ParseFields(root);
        var (inputTokens, outputTokens) = ParseUsage(root);
        var estimatedCost = inputTokens.HasValue && outputTokens.HasValue
            ? inputTokens.Value / 1_000_000m * InputCostPerMillionTokens
                + outputTokens.Value / 1_000_000m * OutputCostPerMillionTokens
            : (decimal?)null;

        return new ExtractionResponse
        {
            DocumentType = docType.ToString(),
            Engine = "openai",
            Fields = fields,
            NeedsReview = KycFieldSchema.ComputeNeedsReview(docType, fields),
            OcrConfidence = 0.9f, // not meaningful for a model-based read - kept only for response-shape parity
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            EstimatedCostUsd = estimatedCost,
            RawText = responseText,
        };
    }

    private JsonObject BuildRequest(DocumentType docType, (string Field, string Label)[] fieldMap, byte[] imageBytes)
    {
        var docDescription = DocTypeDescriptions[docType];
        var fieldHints = string.Join("\n", fieldMap.Select(f => $"- {f.Field} - labeled \"{f.Label}\" on the document"));
        var fieldKeys = string.Join(", ", fieldMap.Select(f => $"\"{f.Field}\""));

        var prompt = $"""
            You are extracting structured KYC data from a photo of a {docDescription}.
            Carefully read the document (including any machine-readable zone, if present)
            and respond with ONLY a JSON object (no markdown, no commentary) containing
            exactly these keys: {fieldKeys}.
            If a field is not visible, not legible, or not present on this document, use
            an empty string "" for its value - never guess or invent a value.

            Fields to extract:
            {fieldHints}
            """;

        return new JsonObject
        {
            ["model"] = _model,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "text", ["text"] = prompt },
                        new JsonObject
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new JsonObject
                            {
                                ["url"] = $"data:{DetectMimeType(imageBytes)};base64,{Convert.ToBase64String(imageBytes)}",
                            },
                        },
                    },
                },
            },
            ["response_format"] = new JsonObject { ["type"] = "json_object" },
        };
    }

    private static string DetectMimeType(byte[] bytes)
    {
        if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return "image/png";
        return "image/jpeg";
    }

    private static Dictionary<string, string> ParseFields(JsonObject root)
    {
        var messageContent = root["choices"]?.AsArray()?.FirstOrDefault()?["message"]?["content"]?.GetValue<string>()
            ?? throw new InvalidOperationException("OpenAI response had no choices[0].message.content.");

        var fieldsJson = JsonNode.Parse(messageContent)?.AsObject()
            ?? throw new InvalidOperationException("OpenAI's response content was not valid JSON.");

        var fields = new Dictionary<string, string>();
        foreach (var (key, value) in fieldsJson)
        {
            var text = value?.GetValue<string>()?.Trim();
            if (!string.IsNullOrEmpty(text))
                fields[key] = text;
        }
        return fields;
    }

    private static (int? InputTokens, int? OutputTokens) ParseUsage(JsonObject root)
    {
        var usage = root["usage"]?.AsObject();
        var inputTokens = usage?["prompt_tokens"]?.GetValue<int>();
        var outputTokens = usage?["completion_tokens"]?.GetValue<int>();
        return (inputTokens, outputTokens);
    }
}
