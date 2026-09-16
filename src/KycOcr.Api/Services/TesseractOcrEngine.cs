using Tesseract;

namespace KycOcr.Api.Services;

public record OcrRow(string Left, string Right);
public record OcrResult(string RawText, List<OcrRow> Rows, float MeanConfidence);

public class TesseractOcrEngine
{
    private readonly string _tessDataPath;

    // Placeholder captions printed on every specimen image (photo/signature
    // boxes) that occasionally land on the same OCR text line as a real
    // field value purely by page-layout coincidence. They're never real KYC
    // data, so they're dropped before line/column grouping.
    private static readonly HashSet<string> BoilerplateWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "specimen", "photo", "signature"
    };

    public TesseractOcrEngine(IWebHostEnvironment env)
    {
        _tessDataPath = Path.Combine(env.ContentRootPath, "tessdata");
    }

    public OcrResult Extract(byte[] imageBytes)
    {
        using var engine = new TesseractEngine(_tessDataPath, "eng", EngineMode.Default);
        using var original = Pix.LoadFromMemory(imageBytes);
        using var img = original.Deskew(); // corrects tilted/rotated scans (e.g. phone photos)
        using var page = engine.Process(img);

        var rawText = page.GetText();
        var rows = ExtractRows(page);
        return new OcrResult(rawText, rows, page.GetMeanConfidence());
    }

    // Groups OCR'd words by text line, then splits each line at its widest
    // horizontal gap. Two-column form fields (e.g. "NATIONALITY   SEX") are
    // read by Tesseract as one text line, which would clobber the simple
    // "value is the next line" extraction in LabelFieldExtractor otherwise.
    private static List<OcrRow> ExtractRows(Page page)
    {
        using var iter = page.GetIterator();

        var rows = new List<OcrRow>();
        iter.Begin();
        do
        {
            do
            {
                do
                {
                    var words = new List<(string Text, Rect Box)>();
                    do
                    {
                        if (!iter.TryGetBoundingBox(PageIteratorLevel.Word, out Rect box))
                            continue;

                        var text = iter.GetText(PageIteratorLevel.Word)?.Trim();
                        var isBoilerplate = !string.IsNullOrEmpty(text) && BoilerplateWords.Contains(text.Trim('.', ',', ':'));
                        // Drop tokens Tesseract hallucinates from borders,
                        // watermarks, or textured backgrounds near real text
                        // (e.g. a stray "|" from a photo box edge). Deliberately
                        // narrow to line/border-like glyphs only - punctuation
                        // such as "&" or "-" is legitimate field content (e.g.
                        // "Trading & Real Estate", "Al-Farsi") and must survive.
                        var isSymbolNoise = !string.IsNullOrEmpty(text) && text.All(c => "|_~¦".Contains(c));
                        if (!string.IsNullOrEmpty(text) && !isBoilerplate && !isSymbolNoise)
                            words.Add((text, box));
                    } while (iter.Next(PageIteratorLevel.TextLine, PageIteratorLevel.Word));

                    if (words.Count > 0)
                        rows.Add(SplitIntoColumns(words));
                } while (iter.Next(PageIteratorLevel.Para, PageIteratorLevel.TextLine));
            } while (iter.Next(PageIteratorLevel.Block, PageIteratorLevel.Para));
        } while (iter.Next(PageIteratorLevel.Block));

        return rows;
    }

    private static OcrRow SplitIntoColumns(List<(string Text, Rect Box)> words)
    {
        words.Sort((a, b) => a.Box.X1.CompareTo(b.Box.X1));

        if (words.Count < 2)
            return new OcrRow(words.Count == 1 ? words[0].Text : "", "");

        var gaps = Enumerable.Range(0, words.Count - 1)
            .Select(i => (Index: i, Gap: words[i + 1].Box.X1 - words[i].Box.X2))
            .ToList();

        var widest = gaps.OrderByDescending(g => g.Gap).First();
        var typicalGap = gaps.Where(g => g.Index != widest.Index).Select(g => g.Gap).DefaultIfEmpty(0).Average();

        // Only treat this as two columns if the widest gap clearly stands out
        // from normal word spacing (a real column gutter, not just spacing
        // between words or after punctuation).
        if (widest.Gap < 40 || widest.Gap < typicalGap * 2.5)
            return new OcrRow(string.Join(' ', words.Select(w => w.Text)), "");

        var left = string.Join(' ', words.Take(widest.Index + 1).Select(w => w.Text));
        var right = string.Join(' ', words.Skip(widest.Index + 1).Select(w => w.Text));
        return new OcrRow(left, right);
    }
}
