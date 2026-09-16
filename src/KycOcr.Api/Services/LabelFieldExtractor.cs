using System.Text.RegularExpressions;

namespace KycOcr.Api.Services;

// Every specimen document prints fields as "LABEL" then its value directly
// below it, sometimes two label/value pairs side by side on the same row
// (already split into a left/right column by TesseractOcrEngine.ExtractRows).
// Extraction is just: find which row+column a label sits in, then read the
// same column of the next non-empty row as its value.
public class LabelFieldExtractor
{
    public Dictionary<string, string> Extract(List<OcrRow> rows, IReadOnlyList<(string Field, string Label)> labelMap)
    {
        var result = new Dictionary<string, string>();

        foreach (var (field, label) in labelMap)
        {
            var normalizedLabel = Normalize(label);

            for (var i = 0; i < rows.Count; i++)
            {
                if (FuzzyContains(Normalize(rows[i].Left), normalizedLabel))
                {
                    var value = NextNonEmpty(rows, i, r => r.Left);
                    if (value != null) result[field] = value;
                    break;
                }

                if (FuzzyContains(Normalize(rows[i].Right), normalizedLabel))
                {
                    var value = NextNonEmpty(rows, i, r => r.Right);
                    if (value != null) result[field] = value;
                    break;
                }
            }
        }

        return result;
    }

    private static string? NextNonEmpty(List<OcrRow> rows, int fromIndex, Func<OcrRow, string> column)
    {
        for (var j = fromIndex + 1; j < rows.Count; j++)
        {
            var text = column(rows[j]).Trim();
            if (text.Length > 0) return text;
        }
        return null;
    }

    private static string Normalize(string s) => Regex.Replace(s.ToUpperInvariant(), "[^A-Z0-9]", "");

    // OCR occasionally clips or misreads a character (e.g. "DATE OF BIRTH"
    // read as "ATE OF BIRTH"), so an exact substring match is too brittle.
    // Allow a small edit-distance tolerance when looking for the label
    // inside a line of OCR'd text.
    private static bool FuzzyContains(string haystack, string needle)
    {
        if (haystack.Contains(needle)) return true;

        // Short labels (e.g. "SEX") can't safely tolerate fuzzy matching: a
        // 2-3 character window is close enough to almost anything by pure
        // chance (e.g. "SEX" matched inside "YOUSEF" via the "SE" substring).
        // Only extend tolerance to labels long enough that a coincidental
        // near-match is actually unlikely.
        if (needle.Length < 5) return false;

        var maxDistance = Math.Max(1, needle.Length / 6);
        for (var len = Math.Max(1, needle.Length - maxDistance); len <= needle.Length + maxDistance; len++)
        {
            for (var start = 0; start + len <= haystack.Length; start++)
            {
                if (LevenshteinDistance(haystack.Substring(start, len), needle) <= maxDistance)
                    return true;
            }
        }
        return false;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) dp[0, j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1), dp[i - 1, j - 1] + cost);
            }
        }
        return dp[a.Length, b.Length];
    }
}
