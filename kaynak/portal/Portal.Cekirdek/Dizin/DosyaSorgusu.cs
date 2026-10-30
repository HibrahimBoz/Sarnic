namespace Portal.Cekirdek.Dizin;

using System.Globalization;
using System.Text;

public enum TerimAlani { Ad, Yol }

/// <summary>
/// Arama sorgusu içindeki tekil terim.
/// </summary>
public sealed record SorguTerimi(string Metin, TerimAlani Alan, bool Degil, bool Joker);

/// <summary>
/// Dosya boyutu aralığı (bayt cinsinden, iki uç dahil).
/// </summary>
public sealed record BoyutAraligi(long? EnAz, long? EnCok);

/// <summary>
/// Dosya değiştirilme tarihi aralığı (yerel saat; Bas dahil, Son hariç).
/// </summary>
public sealed record TarihAraligi(DateTime? Bas, DateTime? Son);

/// <summary>
/// Ayrıştırılmış dosya arama sorgusu.
/// </summary>
public sealed class DosyaSorgusu
{
    private DosyaSorgusu(
        IReadOnlyList<IReadOnlyList<SorguTerimi>> kosullar,
        IReadOnlyList<string> uzantilar,
        IReadOnlyList<string> turler,
        BoyutAraligi? boyut,
        TarihAraligi? tarih,
        string? icerik,
        bool? dersBagli,
        string duzMetin)
    {
        Kosullar = kosullar;
        Uzantilar = uzantilar;
        Turler = turler;
        Boyut = boyut;
        Tarih = tarih;
        Icerik = icerik;
        DersBagli = dersBagli;
        DuzMetin = duzMetin;
        Bos = Kosullar.Count == 0 &&
              Uzantilar.Count == 0 &&
              Turler.Count == 0 &&
              Boyut == null &&
              Tarih == null &&
              Icerik == null &&
              DersBagli == null;
    }

    public IReadOnlyList<IReadOnlyList<SorguTerimi>> Kosullar { get; }
    public IReadOnlyList<string> Uzantilar { get; }
    public IReadOnlyList<string> Turler { get; }
    public BoyutAraligi? Boyut { get; }
    public TarihAraligi? Tarih { get; }
    public string? Icerik { get; }
    public bool? DersBagli { get; }
    public string DuzMetin { get; }
    public bool Bos { get; }

    /// <summary>
    /// Ham arama metnini kurallara göre ayrıştırır.
    /// </summary>
    public static DosyaSorgusu Ayristir(string? metin)
    {
        if (string.IsNullOrWhiteSpace(metin))
        {
            return new DosyaSorgusu(
                Array.Empty<IReadOnlyList<SorguTerimi>>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                null,
                null,
                null,
                null,
                string.Empty);
        }

        var tokens = TokenlaraAyir(metin);
        var kosullar = new List<IReadOnlyList<SorguTerimi>>();
        var uzantilar = new List<string>();
        var turler = new List<string>();
        var icerikListesi = new List<string>();
        var duzMetinListesi = new List<string>();
        BoyutAraligi? boyut = null;
        TarihAraligi? tarih = null;
        bool? dersBagli = null;

        foreach (string hamToken in tokens)
        {
            string token = hamToken.Trim();
            if (string.IsNullOrEmpty(token))
            {
                continue;
            }

            // ders: / !ders:
            if (token.Equals("ders:", StringComparison.OrdinalIgnoreCase))
            {
                dersBagli = true;
                continue;
            }

            if (token.Equals("!ders:", StringComparison.OrdinalIgnoreCase))
            {
                dersBagli = false;
                continue;
            }

            // uz: / ext:
            if (token.StartsWith("uz:", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("ext:", StringComparison.OrdinalIgnoreCase))
            {
                int colonIdx = token.IndexOf(':');
                string val = TirnaklariTemizle(token[(colonIdx + 1)..]);
                string[] parcalar = val.Split(new[] { ';', '|', ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string p in parcalar)
                {
                    string temiz = p.Trim().TrimStart('.').ToLowerInvariant();
                    if (!string.IsNullOrEmpty(temiz) && !uzantilar.Contains(temiz))
                    {
                        uzantilar.Add(temiz);
                    }
                }

                continue;
            }

            // tur: / type:
            if (token.StartsWith("tur:", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("type:", StringComparison.OrdinalIgnoreCase))
            {
                int colonIdx = token.IndexOf(':');
                string val = TirnaklariTemizle(token[(colonIdx + 1)..]);
                string[] parcalar = val.Split(new[] { ';', '|', ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string p in parcalar)
                {
                    string temiz = p.Trim().ToLowerInvariant();
                    if (!string.IsNullOrEmpty(temiz) && !turler.Contains(temiz))
                    {
                        turler.Add(temiz);
                    }
                }

                continue;
            }

            // boyut: / size:
            if (token.StartsWith("boyut:", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("size:", StringComparison.OrdinalIgnoreCase))
            {
                int colonIdx = token.IndexOf(':');
                string val = TirnaklariTemizle(token[(colonIdx + 1)..]);
                if (TryParseBoyut(val, out var parsedBoyut))
                {
                    boyut = parsedBoyut;
                    continue;
                }
                // Geçersiz değerli süzgeç -> düz Ad terimi olarak kalır
            }

            // tarih: / dm:
            if (token.StartsWith("tarih:", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("dm:", StringComparison.OrdinalIgnoreCase))
            {
                int colonIdx = token.IndexOf(':');
                string val = TirnaklariTemizle(token[(colonIdx + 1)..]);
                if (TryParseTarih(val, out var parsedTarih))
                {
                    tarih = parsedTarih;
                    continue;
                }
                // Geçersiz değerli süzgeç -> düz Ad terimi olarak kalır
            }

            // icerik: / content:
            if (token.StartsWith("icerik:", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("content:", StringComparison.OrdinalIgnoreCase))
            {
                int colonIdx = token.IndexOf(':');
                string val = TirnaklariTemizle(token[(colonIdx + 1)..]);
                if (!string.IsNullOrEmpty(val))
                {
                    icerikListesi.Add(val);
                }

                continue;
            }

            // Koşul terimi (Ad ya da Yol, VEYA grupları ve ! desteği)
            var veyaGrubu = VeyaGrubuAyristir(token, duzMetinListesi);
            if (veyaGrubu.Count > 0)
            {
                kosullar.Add(veyaGrubu);
            }
        }

        string? icerik = icerikListesi.Count > 0 ? string.Join(" ", icerikListesi) : null;
        string duzMetin = string.Join(" ", duzMetinListesi);

        return new DosyaSorgusu(
            kosullar,
            uzantilar,
            turler,
            boyut,
            tarih,
            icerik,
            dersBagli,
            duzMetin);
    }

    private static List<SorguTerimi> VeyaGrubuAyristir(string token, List<string> duzMetinListesi)
    {
        var sonuc = new List<SorguTerimi>();
        var parcalar = VeyaParcala(token);

        foreach (string hamParca in parcalar)
        {
            string parca = hamParca.Trim();
            if (string.IsNullOrEmpty(parca))
            {
                continue;
            }

            bool degil = false;
            if (parca.StartsWith('!'))
            {
                degil = true;
                parca = parca[1..].Trim();
            }

            TerimAlani alan = TerimAlani.Ad;
            if (parca.StartsWith("yol:", StringComparison.OrdinalIgnoreCase))
            {
                alan = TerimAlani.Yol;
                parca = parca[4..];
            }
            else if (parca.StartsWith("path:", StringComparison.OrdinalIgnoreCase))
            {
                alan = TerimAlani.Yol;
                parca = parca[5..];
            }

            string ozgunMetin = TirnaklariTemizle(parca);
            if (string.IsNullOrEmpty(ozgunMetin))
            {
                continue;
            }

            string normMetin = DosyaNormalizasyon.Normalle(ozgunMetin);
            bool joker = normMetin.Contains('*') || normMetin.Contains('?');

            sonuc.Add(new SorguTerimi(normMetin, alan, degil, joker));

            if (!degil && alan == TerimAlani.Ad)
            {
                duzMetinListesi.Add(ozgunMetin);
            }
        }

        return sonuc;
    }

    private static List<string> VeyaParcala(string token)
    {
        var list = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < token.Length; i++)
        {
            char c = token[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                sb.Append(c);
            }
            else if (c == '|' && !inQuotes)
            {
                if (sb.Length > 0)
                {
                    list.Add(sb.ToString());
                    sb.Clear();
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        if (sb.Length > 0)
        {
            list.Add(sb.ToString());
        }

        return list;
    }

    private static List<string> TokenlaraAyir(string metin)
    {
        var list = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < metin.Length; i++)
        {
            char c = metin[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                sb.Append(c);
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (sb.Length > 0)
                {
                    list.Add(sb.ToString());
                    sb.Clear();
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        if (sb.Length > 0)
        {
            list.Add(sb.ToString());
        }

        return list;
    }

    private static string TirnaklariTemizle(string val)
    {
        string s = val.Trim();
        if (s.Length >= 2 && s.StartsWith('"') && s.EndsWith('"'))
        {
            return s[1..^1];
        }

        return s;
    }

    private static bool TryParseBoyut(string val, out BoyutAraligi? aralik)
    {
        aralik = null;
        val = val.Trim();
        if (string.IsNullOrEmpty(val))
        {
            return false;
        }

        int ikiNokta = val.IndexOf("..", StringComparison.Ordinal);
        if (ikiNokta > 0)
        {
            string sol = val[..ikiNokta];
            string sag = val[(ikiNokta + 2)..];
            if (TryParseBytes(sol, out long b1) && TryParseBytes(sag, out long b2))
            {
                aralik = new BoyutAraligi(b1, b2);
                return true;
            }

            return false;
        }

        if (val.StartsWith(">="))
        {
            if (TryParseBytes(val[2..], out long b))
            {
                aralik = new BoyutAraligi(b, null);
                return true;
            }

            return false;
        }

        if (val.StartsWith('>'))
        {
            if (TryParseBytes(val[1..], out long b))
            {
                aralik = new BoyutAraligi(b + 1, null);
                return true;
            }

            return false;
        }

        if (val.StartsWith("<="))
        {
            if (TryParseBytes(val[2..], out long b))
            {
                aralik = new BoyutAraligi(null, b);
                return true;
            }

            return false;
        }

        if (val.StartsWith('<'))
        {
            if (TryParseBytes(val[1..], out long b))
            {
                aralik = new BoyutAraligi(null, b - 1);
                return true;
            }

            return false;
        }

        if (TryParseBytes(val, out long tekBoyut))
        {
            aralik = new BoyutAraligi(tekBoyut, tekBoyut);
            return true;
        }

        return false;
    }

    private static bool TryParseBytes(string str, out long bytes)
    {
        bytes = 0;
        str = str.Trim();
        if (string.IsNullOrEmpty(str))
        {
            return false;
        }

        long carpan = 1L;
        string numStr = str;

        if (str.EndsWith("gb", StringComparison.OrdinalIgnoreCase))
        {
            carpan = 1024L * 1024L * 1024L;
            numStr = str[..^2];
        }
        else if (str.EndsWith("mb", StringComparison.OrdinalIgnoreCase))
        {
            carpan = 1024L * 1024L;
            numStr = str[..^2];
        }
        else if (str.EndsWith("kb", StringComparison.OrdinalIgnoreCase))
        {
            carpan = 1024L;
            numStr = str[..^2];
        }
        else if (str.EndsWith("b", StringComparison.OrdinalIgnoreCase))
        {
            carpan = 1L;
            numStr = str[..^1];
        }

        numStr = numStr.Trim().Replace(',', '.');
        if (double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d >= 0)
        {
            bytes = (long)Math.Round(d * carpan);
            return true;
        }

        return false;
    }

    private static bool TryParseTarih(string val, out TarihAraligi? aralik)
    {
        aralik = null;
        val = val.Trim();
        if (string.IsNullOrEmpty(val))
        {
            return false;
        }

        int ikiNokta = val.IndexOf("..", StringComparison.Ordinal);
        if (ikiNokta > 0)
        {
            string sol = val[..ikiNokta];
            string sag = val[(ikiNokta + 2)..];
            if (TryParseDonem(sol, out var p1) && TryParseDonem(sag, out var p2))
            {
                aralik = new TarihAraligi(p1.Bas, p2.Son);
                return true;
            }

            return false;
        }

        if (val.StartsWith(">="))
        {
            if (TryParseDonem(val[2..], out var p))
            {
                aralik = new TarihAraligi(p.Bas, null);
                return true;
            }

            return false;
        }

        if (val.StartsWith('>'))
        {
            if (TryParseDonem(val[1..], out var p))
            {
                aralik = new TarihAraligi(p.Son, null);
                return true;
            }

            return false;
        }

        if (val.StartsWith("<="))
        {
            if (TryParseDonem(val[2..], out var p))
            {
                aralik = new TarihAraligi(null, p.Son);
                return true;
            }

            return false;
        }

        if (val.StartsWith('<'))
        {
            if (TryParseDonem(val[1..], out var p))
            {
                aralik = new TarihAraligi(null, p.Bas);
                return true;
            }

            return false;
        }

        if (TryParseDonem(val, out var tekDonem))
        {
            aralik = new TarihAraligi(tekDonem.Bas, tekDonem.Son);
            return true;
        }

        return false;
    }

    private static bool TryParseDonem(string str, out (DateTime Bas, DateTime Son) donem)
    {
        donem = default;
        str = str.Trim();
        if (string.IsNullOrEmpty(str))
        {
            return false;
        }

        // yyyy
        if (str.Length == 4 && int.TryParse(str, out int yil) && yil >= 1900 && yil <= 9999)
        {
            var bas = new DateTime(yil, 1, 1, 0, 0, 0, DateTimeKind.Local);
            var son = bas.AddYears(1);
            donem = (bas, son);
            return true;
        }

        // yyyy-MM
        if (DateTime.TryParseExact(str, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtAy))
        {
            var bas = DateTime.SpecifyKind(dtAy, DateTimeKind.Local);
            var son = bas.AddMonths(1);
            donem = (bas, son);
            return true;
        }

        // yyyy-MM-dd
        if (DateTime.TryParseExact(str, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtGun))
        {
            var bas = DateTime.SpecifyKind(dtGun, DateTimeKind.Local);
            var son = bas.AddDays(1);
            donem = (bas, son);
            return true;
        }

        return false;
    }
}
