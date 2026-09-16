using KycOcr.Api.Models;
using KycOcr.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<TesseractOcrEngine>();
builder.Services.AddSingleton<LabelFieldExtractor>();
builder.Services.AddSingleton<TesseractKycExtractor>();
builder.Services.AddHttpClient<OpenAiKycExtractor>();

var app = builder.Build();

app.MapPost("/api/ocr/extract", async (HttpRequest request, TesseractKycExtractor tesseractExtractor, OpenAiKycExtractor openAiExtractor) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest("Expected multipart/form-data with an 'image' file and a 'docType' field.");

    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("image");
    var docTypeRaw = form["docType"].ToString();
    var engineRaw = form["engine"].ToString();

    if (file is null || file.Length == 0)
        return Results.BadRequest("Missing 'image' file.");

    if (!Enum.TryParse<DocumentType>(docTypeRaw, ignoreCase: true, out var docType))
        return Results.BadRequest($"Invalid 'docType'. Expected one of: {string.Join(", ", Enum.GetNames<DocumentType>())}");

    // "tesseract" (default) or "openai" - same request/response contract either way.
    IKycExtractor extractor = engineRaw.Equals("openai", StringComparison.OrdinalIgnoreCase)
        ? openAiExtractor
        : tesseractExtractor;

    using var ms = new MemoryStream();
    await file.CopyToAsync(ms);

    var result = await extractor.ExtractAsync(docType, ms.ToArray());
    return Results.Ok(result);
})
.WithName("ExtractKycFields")
.DisableAntiforgery();

app.Run();
