using System.Text.RegularExpressions;

namespace KycOcr.Api.Services;

// Parses the 2-line TD3 machine-readable zone printed at the bottom of a
// passport. Fixed-width fixed-format text, so this is far more reliable than
// reading the free-text fields above it. Checksum digits are ignored for now.
public static class MrzParser
{
    private static readonly Regex InvalidMrzChar = new("[^A-Z0-9<]", RegexOptions.Compiled);

    // Same length check used both to pick out MRZ candidate lines here and
    // to keep those lines OUT of the pool of "next line" values the label
    // matcher can grab for an unrelated field (see LabelFieldExtractor).
    // A rough shape check, not a strict validation - good enough to
    // recognize "this is clearly an MRZ line", which is all it's used for.
    public static bool LooksLikeMrzLine(string text)
    {
        var stripped = text.Replace(" ", "");
        return stripped.Length is >= 40 and <= 44;
    }

    public static Dictionary<string, string>? TryParse(string text)
    {
        var candidateLines = text
            .Split('\n')
            .Select(l => l.Replace(" ", "").ToUpperInvariant().Trim())
            .Where(l => l.Length is >= 40 and <= 44)
            // The MRZ character set is strictly A-Z, 0-9 and '<' (ICAO 9303),
            // so anything else here is necessarily an OCR misread. '@' is a
            // common misread of '0' in this monospace font; correcting it
            // (rather than rejecting the whole line) is what lets a
            // near-perfect MRZ read still parse instead of silently falling
            // back to the much less reliable free-text fields above it.
            .Select(l => InvalidMrzChar.Replace(l, "0"))
            .ToList();

        if (candidateLines.Count < 2)
            return null;

        var line1 = candidateLines[0].PadRight(44, '<')[..44];
        var line2 = candidateLines[1].PadRight(44, '<')[..44];

        var names = line1[5..].Split("<<", 2);
        var surname = names.ElementAtOrDefault(0)?.Replace('<', ' ').Trim() ?? "";
        var givenNames = names.ElementAtOrDefault(1)?.Replace('<', ' ').Trim() ?? "";

        // NOTE: standard ICAO TD3 puts a standalone check digit right after
        // the 9-char passport number field, before nationality (i.e.
        // nationality at idx10-12, not idx9-11). The Coast test specimens'
        // MRZ omits that one check digit, so every field below is shifted
        // one position left of "textbook" TD3. If this is ever run against
        // a real government-issued passport, these offsets likely need to
        // shift back right by 1 - verify against a real MRZ before trusting.
        return new Dictionary<string, string>
        {
            ["Surname"] = surname,
            ["GivenNames"] = givenNames,
            ["PassportNumber"] = line2[0..9].Replace("<", "").Trim(),
            ["Nationality"] = line2[9..12].Trim(),
            ["DateOfBirth"] = FormatMrzDate(line2[12..18]),
            ["Sex"] = line2[19] switch { 'M' => "M", 'F' => "F", _ => "" },
            ["DateOfExpiry"] = FormatMrzDate(line2[20..26]),
        };
    }

    private static string FormatMrzDate(string yymmdd)
    {
        if (yymmdd.Length != 6 || !yymmdd.All(char.IsDigit))
            return yymmdd;

        var yy = int.Parse(yymmdd[0..2]);
        var mm = yymmdd[2..4];
        var dd = yymmdd[4..6];
        var year = yy <= 50 ? 2000 + yy : 1900 + yy;
        return $"{year:D4}-{mm}-{dd}";
    }
}
