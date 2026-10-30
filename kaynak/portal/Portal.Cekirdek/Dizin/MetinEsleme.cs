namespace Portal.Cekirdek.Dizin;

using System.Globalization;
using System.Text;

/// <summary>
/// Normalize metin ile özgün metin karakter indeksleri arasında eşleme ve alıntı/vurgu üreticisi.
/// </summary>
public static class MetinEsleme
{
    private static readonly char[] BoslukKarakterleri = [' ', '\t', '\r', '\n'];

    /// <summary>
    /// Metni TrNormalizasyon ile normalize ederken, her normalize karakterin özgün metindeki karakter indeksini eşler.
    /// </summary>
    public static (string Norm, int[] Ozgun) Esle(string? metin)
    {
        if (string.IsNullOrWhiteSpace(metin))
        {
            return (string.Empty, Array.Empty<int>());
        }

        var sb = new StringBuilder(metin.Length);
        var ozgun = new List<int>(metin.Length);
        bool oncekiBosluk = false;

        for (int i = 0; i < metin.Length; i++)
        {
            char ch = metin[i];
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
                    ozgun.Add(i);
                    oncekiBosluk = true;
                }
            }
            else
            {
                sb.Append(donusen);
                ozgun.Add(i);
                oncekiBosluk = false;
            }
        }

        while (sb.Length > 0 && sb[^1] == ' ')
        {
            sb.Length--;
            ozgun.RemoveAt(ozgun.Count - 1);
        }

        return (sb.ToString(), ozgun.ToArray());
    }

    /// <summary>
    /// Özgün metinde, normalize sözcüklerden biriyle başlayan her sözcüğün eşleşen kısmının indeks ve uzunluklarını döner.
    /// </summary>
    public static (int Bas, int Uzunluk)[] Vurgula(string? metin, IReadOnlyList<string> normSozcukler)
    {
        if (string.IsNullOrWhiteSpace(metin) || normSozcukler == null || normSozcukler.Count == 0)
        {
            return Array.Empty<(int Bas, int Uzunluk)>();
        }

        var (norm, ozgun) = Esle(metin);
        if (norm.Length == 0 || ozgun.Length == 0)
        {
            return Array.Empty<(int Bas, int Uzunluk)>();
        }

        var sonuclar = new List<(int Bas, int Uzunluk)>();
        int i = 0;
        while (i < norm.Length)
        {
            while (i < norm.Length && norm[i] == ' ')
            {
                i++;
            }

            if (i >= norm.Length)
            {
                break;
            }

            int kelimeBas = i;
            while (i < norm.Length && norm[i] != ' ')
            {
                i++;
            }

            int kelimeUz = i - kelimeBas;
            ReadOnlySpan<char> kelimeSpan = norm.AsSpan(kelimeBas, kelimeUz);

            int enIyiUzunluk = 0;
            foreach (string aranan in normSozcukler)
            {
                if (!string.IsNullOrEmpty(aranan) && kelimeUz >= aranan.Length)
                {
                    if (kelimeSpan.StartsWith(aranan.AsSpan(), StringComparison.Ordinal))
                    {
                        if (aranan.Length > enIyiUzunluk)
                        {
                            enIyiUzunluk = aranan.Length;
                        }
                    }
                }
            }

            if (enIyiUzunluk > 0)
            {
                int bas = ozgun[kelimeBas];
                int son = ozgun[kelimeBas + enIyiUzunluk - 1];

                while (son + 1 < metin.Length && char.GetUnicodeCategory(metin[son + 1]) == UnicodeCategory.NonSpacingMark)
                {
                    son++;
                }

                sonuclar.Add((bas, son - bas + 1));
            }
        }

        return sonuclar.ToArray();
    }

    /// <summary>
    /// İlk eşleşmenin çevresinde en çok enFazla karakterlik alıntı ve alıntı içi vurguları üretir.
    /// </summary>
    public static (string Alinti, (int Bas, int Uzunluk)[] Vurgular) AlintiUret(
        string? metin,
        IReadOnlyList<string> normSozcukler,
        int enFazla = 240)
    {
        if (string.IsNullOrWhiteSpace(metin))
        {
            return (string.Empty, Array.Empty<(int Bas, int Uzunluk)>());
        }

        var vurgular = Vurgula(metin, normSozcukler);
        if (metin.Length <= enFazla)
        {
            return (metin, vurgular);
        }

        if (vurgular.Length == 0)
        {
            int basKesim = Math.Min(enFazla - 1, metin.Length);
            int bosluk = metin.LastIndexOfAny(BoslukKarakterleri, basKesim);
            if (bosluk > 0)
            {
                basKesim = bosluk;
            }

            string basAlinti = metin[..basKesim].TrimEnd() + "…";
            return (basAlinti, Array.Empty<(int Bas, int Uzunluk)>());
        }

        var (bas0, uz0) = vurgular[0];
        int idealBas = bas0 - ((enFazla - uz0) / 2);
        if (idealBas < 0)
        {
            idealBas = 0;
        }

        if (idealBas + enFazla > metin.Length)
        {
            idealBas = Math.Max(0, metin.Length - enFazla);
        }

        int kesimBas = idealBas;
        bool bastaUcNokta = false;
        if (kesimBas > 0)
        {
            int bosluk = metin.IndexOfAny(BoslukKarakterleri, kesimBas, Math.Max(1, bas0 - kesimBas));
            if (bosluk != -1 && bosluk < bas0)
            {
                kesimBas = bosluk + 1;
                bastaUcNokta = true;
            }
            else
            {
                int geriyeBosluk = metin.LastIndexOfAny(BoslukKarakterleri, kesimBas);
                if (geriyeBosluk != -1)
                {
                    kesimBas = geriyeBosluk + 1;
                    bastaUcNokta = true;
                }
                else
                {
                    kesimBas = 0;
                    bastaUcNokta = false;
                }
            }
        }

        int kalanButce = enFazla - (bastaUcNokta ? 1 : 0);
        int idealSon = kesimBas + kalanButce;
        int kesimSon = idealSon;
        bool sondaUcNokta = false;

        if (kesimSon < metin.Length)
        {
            idealSon = kesimBas + kalanButce - 1;
            int minSon = bas0 + uz0;
            int bosluk = metin.LastIndexOfAny(BoslukKarakterleri, Math.Min(idealSon, metin.Length - 1));
            if (bosluk >= minSon)
            {
                kesimSon = bosluk;
                sondaUcNokta = true;
            }
            else
            {
                kesimSon = Math.Min(idealSon, metin.Length);
                sondaUcNokta = true;
            }
        }
        else
        {
            kesimSon = metin.Length;
            sondaUcNokta = false;
        }

        string parca = metin[kesimBas..kesimSon];
        string alinti = (bastaUcNokta ? "…" : "") + parca + (sondaUcNokta ? "…" : "");

        int kaydirma = (bastaUcNokta ? 1 : 0) - kesimBas;
        var alintiVurgulari = new List<(int Bas, int Uzunluk)>();
        foreach (var (vBas, vUz) in vurgular)
        {
            if (vBas >= kesimBas && vBas + vUz <= kesimSon)
            {
                alintiVurgulari.Add((vBas + kaydirma, vUz));
            }
        }

        return (alinti, alintiVurgulari.ToArray());
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
