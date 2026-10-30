namespace Portal.Cekirdek;

using System.Globalization;
using System.Text;

/// <summary>
/// Türkçe metin normalizasyonu ve kelime ayırma işlemleri.
/// </summary>
public static class TrNormalizasyon
{
    /// <summary>
    /// Metni küçük harfe dönüştürür, Türkçe ve şapkalı karakterleri katlar,
    /// noktalama işaretlerini boşluğa çevirir ve çoklu boşlukları teke indirir.
    /// </summary>
    public static string Normalle(string? metin)
    {
        if (string.IsNullOrWhiteSpace(metin))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(metin.Length);
        bool oncekiBosluk = false;

        foreach (char ch in metin)
        {
            if (char.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            char donusen = Donustur(ch);
            if (donusen == ' ')
            {
                if (!oncekiBosluk && sb.Length > 0)
                {
                    sb.Append(' ');
                    oncekiBosluk = true;
                }
            }
            else
            {
                sb.Append(donusen);
                oncekiBosluk = false;
            }
        }

        // Sondaki boşluğu temizle
        while (sb.Length > 0 && sb[^1] == ' ')
        {
            sb.Length--;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Normalleştirilmiş metni kelime dizisine dönüştürür.
    /// </summary>
    public static string[] Kelimeler(string? metin)
    {
        string norm = Normalle(metin);
        if (string.IsNullOrEmpty(norm))
        {
            return Array.Empty<string>();
        }

        return norm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static char Donustur(char c)
    {
        switch (c)
        {
            case 'ı':
            case 'İ':
            case 'I':
            case 'i':
                return 'i';
            case 'ş':
            case 'Ş':
                return 's';
            case 'ğ':
            case 'Ğ':
                return 'g';
            case 'ü':
            case 'Ü':
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
            case 'î':
            case 'Î':
                return 'i';
            case 'û':
            case 'Û':
                return 'u';
            case '\'':
            case '’':
            case '‘':
            case '`':
            case '´':
                return ' ';
            default:
                if (char.IsSurrogate(c))
                {
                    return ' ';
                }

                if (c >= 'A' && c <= 'Z')
                {
                    return (char)(c + 32);
                }

                if (char.IsPunctuation(c) || char.IsWhiteSpace(c) || char.IsSymbol(c) || char.IsControl(c))
                {
                    return ' ';
                }

                return char.ToLowerInvariant(c);
        }
    }
}
