# Coast KYC OCR API

A minimal .NET Web API that extracts KYC fields (name, ID number, dates, etc.) from a photo of a National ID, Passport, or Trade License. Two interchangeable extraction engines sit behind the same endpoint:

- **Tesseract** (default) — open source OCR + label matching, runs fully offline, free.
- **OpenAI** — sends the image to a GPT-4o-mini vision model and asks it to read the fields directly. More accurate on rotated/blurry/noisy images, requires an API key and an internet connection. Costs more per call than Tesseract (roughly $0.005-0.006 per extraction, since OpenAI's image tokenization is comparatively expensive) but noticeably more reliable on hard images.

## Prerequisites

- **.NET 8 SDK** — check with `dotnet --version`. If missing, install from https://dotnet.microsoft.com/download/dotnet/8.0, or run:
  ```powershell
  Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile "$env:TEMP\dotnet-install.ps1"
  & "$env:TEMP\dotnet-install.ps1" -Channel 8.0 -InstallDir "$env:USERPROFILE\.dotnet"
  ```
  (installs to your user profile, no admin rights needed — then add `$env:USERPROFILE\.dotnet` to your PATH)
- No separate Tesseract install needed — the native engine ships inside the `Tesseract` NuGet package, and the English language data (`tessdata/eng.traineddata`) is already committed in this repo.
- **An OpenAI API key** — only needed if you want to use `engine=openai`. Get one at https://platform.openai.com/api-keys. Tesseract works with zero setup.

## Setup

1. Clone/pull the repo.
2. (Optional, only for the OpenAI engine) Store your API key with `dotnet user-secrets` — this keeps it out of the repo entirely:
   ```powershell
   cd src/KycOcr.Api
   dotnet user-secrets set "OpenAI:ApiKey" "your-key-here"
   dotnet user-secrets set "OpenAI:Model" "gpt-4o-mini"
   ```
   Without this, `engine=openai` requests will fail with a clear error; `engine=tesseract` (the default) is unaffected.

## Running

```powershell
cd src/KycOcr.Api
dotnet run --urls http://localhost:5080
```

Wait for `Now listening on: http://localhost:5080`. Stop with `Ctrl+C`.

Test it with curl, Postman, or your frontend directly — see below. (No Swagger UI; kept this deliberately minimal.)

## Using the API

**Endpoint:** `POST /api/ocr/extract`, `Content-Type: multipart/form-data`

| Form field | Required | Values |
|---|---|---|
| `image` | yes | the document photo/scan (jpg/png) |
| `docType` | yes | `NationalId`, `Passport`, or `TradeLicense` (case-insensitive) |
| `engine` | no | `tesseract` (default) or `openai` |

**Example (curl):**
```bash
curl -X POST "http://localhost:5080/api/ocr/extract" \
  -F "docType=NationalId" \
  -F "engine=openai" \
  -F "image=@/path/to/id-photo.jpg"
```

**Response:**
```json
{
  "documentType": "NationalId",
  "engine": "openai",
  "fields": { "IdNumber": "...", "FullName": "...", "...": "..." },
  "needsReview": false,
  "ocrConfidence": 0.9,
  "inputTokens": 37091,
  "outputTokens": 121,
  "estimatedCostUsd": 0.00563625,
  "rawText": "..."
}
```

`needsReview: true` means at least one expected field for that document type came back missing — the caller should prompt for a retake / manual entry rather than trust the result as-is.

`inputTokens` / `outputTokens` / `estimatedCostUsd` are only populated for `engine=openai` (Tesseract is free/local, so these are `null`). Cost is computed from OpenAI's own reported token usage against `gpt-4o-mini`'s rate card ($0.15/1M input tokens, $0.60/1M output tokens) — update the constants in `OpenAiKycExtractor.cs` if the configured model changes.

## Project structure

```
src/KycOcr.Api/
  Program.cs                        - the one endpoint, routes to an engine by the `engine` field
  Models/OcrModels.cs                - request/response shapes
  Services/
    IKycExtractor.cs                 - shared contract both engines implement
    KycFieldSchema.cs                - per-docType expected field list + needsReview rule (shared)
    TesseractOcrEngine.cs            - OCR + column-aware row splitting + deskew
    LabelFieldExtractor.cs           - fuzzy label matching -> field values
    MrzParser.cs                     - passport MRZ (machine-readable zone) parsing
    TesseractKycExtractor.cs         - Tesseract engine, implements IKycExtractor
    OpenAiKycExtractor.cs            - OpenAI engine, implements IKycExtractor
  tessdata/eng.traineddata           - Tesseract's English language data

context/                             - sample KYC documents used for testing, plus the
                                        original Figma-exported form design
```

## Known limitations

- Tesseract's label matching needs the field label *wording* to match what it's tuned for (e.g. "DATE OF BIRTH") — different wording, abbreviations, or another language on a new document type needs a new entry added to `KycFieldSchema`.
- Tesseract's accuracy drops sharply on rotated, blurry, or heavily noisy images. Use `engine=openai` for those, or expect `needsReview: true`.
- OpenAI's API terms (for the standard, non-enterprise tier) allow using submitted data to improve models unless you've opted out via the platform's data controls — fine for testing with synthetic specimens, but check your account's data-usage settings (or use AWS Bedrock, which doesn't train on customer data by default) before sending real customer documents through it in production.
- The `env` file at the repo root (if present) is gitignored and should only ever be used to seed `dotnet user-secrets` locally — it should never be committed.
