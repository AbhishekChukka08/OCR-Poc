using KycOcr.Api.Models;
using KycOcr.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<TesseractOcrEngine>();
builder.Services.AddSingleton<LabelFieldExtractor>();
builder.Services.AddSingleton<KycExtractionService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapPost("/api/ocr/extract", async (HttpRequest request, KycExtractionService extractionService) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest("Expected multipart/form-data with an 'image' file and a 'docType' field.");

    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("image");
    var docTypeRaw = form["docType"].ToString();

    if (file is null || file.Length == 0)
        return Results.BadRequest("Missing 'image' file.");

    if (!Enum.TryParse<DocumentType>(docTypeRaw, ignoreCase: true, out var docType))
        return Results.BadRequest($"Invalid 'docType'. Expected one of: {string.Join(", ", Enum.GetNames<DocumentType>())}");

    using var ms = new MemoryStream();
    await file.CopyToAsync(ms);

    var result = extractionService.Extract(docType, ms.ToArray());
    return Results.Ok(result);
})
.WithName("ExtractKycFields")
.DisableAntiforgery();

app.Run();
