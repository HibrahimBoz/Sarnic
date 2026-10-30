namespace Portal.Cekirdek.Dizin;

using System.Globalization;
using System.Text;

/// <summary>
/// Dosya adı ve yol normalizasyonu.
/// Türkçe harf katlama + küçük harf + Mn atma; noktalama, ayraç ve boşluklar aynen korunur.
/// </summary>
public static class DosyaNormalizasyon
{
    /// <summary>
    /// Metni dosya arama kurallarına göre normalize eder.
    /// </summary>
    public static string Normalle(string? metin)
    {
        if (string.IsNullOrEmpty(metin))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(metin.Length);
        foreach (char c in metin)
        {
            if (char.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(Donustur(c));
        }

        return sb.ToString();
    }

    private static char Donustur(char c)
    {
        switch (c)
        {
            case 'ı':
            case 'İ':
            case 'I':
            case 'i':
            case 'î':
            case 'Î':
                return 'i';
            case 'ş':
            case 'Ş':
                return 's';
            case 'ğ':
            case 'Ğ':
                return 'g';
            case 'ü':
            case 'Ü':
            case 'û':
            case 'Û':
                return 'u';
            case 'ö':
            case 'Ö':
                return 'o';
            case 'ç':
            case 'Ç':
                return 'c';
            case 'â':
            case 'Â':
                return 'a';
            default:
                if (c >= 'A' && c <= 'Z')
                {
                    return (char)(c + 32);
                }

                return char.ToLowerInvariant(c);
        }
    }
}
