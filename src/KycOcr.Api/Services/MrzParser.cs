using System.Text.RegularExpressions;

namespace KycOcr.Api.Services;

// Parses the 2-line TD3 machine-readable zone printed at the bottom of a
// passport. Fixed-width fixed-format text, so this is far more reliable than
// reading the free-text fields above it. Checksum digits are ignored for now.
public static class MrzParser
{
    private static readonly Regex MrzLinePattern = new("^[A-Z0-9<]{40,44}$", RegexOptions.Compiled);

    public static Dictionary<string, string>? TryParse(string text)
    {
        var candidateLines = text
            .Split('\n')
            .Select(l => l.Replace(" ", "").ToUpperInvariant().Trim())
            .Where(l => MrzLinePattern.IsMatch(l))
            .ToList();

        if (candidateLines.Count < 2)
            return null;

        var line1 = candidateLines[0].PadRight(44, '<')[..44];
        var line2 = candidateLines[1].PadRight(44, '<')[..44];

        var names = line1[5..].Split("<<", 2);
        var surname = names.ElementAtOrDefault(0)?.Replace('<', ' ').Trim() ?? "";
        var givenNames = names.ElementAtOrDefault(1)?.Replace('<', ' ').Trim() ?? "";

        return new Dictionary<string, string>
        {
            ["Surname"] = surname,
            ["GivenNames"] = givenNames,
            ["PassportNumber"] = line2[0..9].Replace("<", "").Trim(),
            ["Nationality"] = line2[10..13].Trim(),
            ["DateOfBirth"] = FormatMrzDate(line2[13..19]),
            ["Sex"] = line2[20] switch { 'M' => "M", 'F' => "F", _ => "" },
            ["DateOfExpiry"] = FormatMrzDate(line2[21..27]),
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
