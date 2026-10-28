using System.Globalization;
using System.Text;

namespace MovieDB.AccessImport;

/// <summary>
/// Approximation of the database collation <c>utf8mb4_uca1400_ai_ci</c> (accent- and case-insensitive, PAD SPACE), used
/// to plan duplicate groups. A characteristics test compares it with the real collation; the unique key on
/// <c>movies</c> remains the safety net.
/// </summary>
internal static class CollationKey
{
    public static string Of(string value)
    {
        var decomposed = value.TrimEnd(' ').Normalize(NormalizationForm.FormKD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            switch (char.ToLowerInvariant(c))
            {
                case 'ø':
                    builder.Append('o');
                    break;
                case 'æ':
                    builder.Append("ae");
                    break;
                case 'œ':
                    builder.Append("oe");
                    break;
                case 'ß':
                    builder.Append("ss");
                    break;
                case 'đ':
                    builder.Append('d');
                    break;
                case 'ł':
                    builder.Append('l');
                    break;
                default:
                    builder.Append(char.ToLowerInvariant(c));
                    break;
            }
        }

        return builder.ToString();
    }

    public static string Of(string title, int year, string director) =>
        string.Create(CultureInfo.InvariantCulture, $"{Of(title)}\u0001{year}\u0001{Of(director)}");
}
