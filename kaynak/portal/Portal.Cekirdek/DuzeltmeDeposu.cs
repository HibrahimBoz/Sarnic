namespace Portal.Cekirdek;

using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

/// <summary>
/// Düzeltmeler uygulandıktan sonraki segment durumu, geçmişi ve çakışma bayrağı.
/// </summary>
public record EfektifSegment(
    int No,
    double Bas,
    double Son,
    string Metin,
    IReadOnlyList<Duzeltme> Gecmis,
    bool Cakisma,
    string? Ceviri = null
) : Segment(No, Bas, Son, Metin, Ceviri)
{
    public Segment AsilSegment => new(No, Bas, Son, Metin, Ceviri);
}

/// <summary>
/// Transkript ile düzeltmelerin birleştirilme sonucu.
/// </summary>
public class BirlestirmeSonucu : IReadOnlyList<EfektifSegment>
{
    public IReadOnlyList<EfektifSegment> Segmentler { get; }
    public IReadOnlyList<Duzeltme> Yetimler { get; }
    public int AtlananDosyaSayisi { get; }
    public bool CakismaVar => Segmentler.Any(s => s.Cakisma);

    public BirlestirmeSonucu(
        IReadOnlyList<EfektifSegment> segmentler,
        IReadOnlyList<Duzeltme> yetimler,
        int atlananDosyaSayisi = 0)
    {
        Segmentler = segmentler ?? Array.Empty<EfektifSegment>();
        Yetimler = yetimler ?? Array.Empty<Duzeltme>();
        AtlananDosyaSayisi = atlananDosyaSayisi;
    }

    public int Count => Segmentler.Count;
    public EfektifSegment this[int index] => Segmentler[index];
    public IEnumerator<EfektifSegment> GetEnumerator() => Segmentler.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Segment düzeltmelerini dosya sisteminde yönetir ve transkript ile birleştirir.
/// </summary>
public class DuzeltmeDeposu
{
    private static readonly JsonSerializerOptions JsonAyar = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        WriteIndented = false
    };

    private readonly string? _kokKlasor;
    private readonly IKimlik _kimlik;

    /// <summary>
    /// Okuma sırasında atlanan (.tmp veya ayrıştırılamayan) dosya sayısı.
    /// </summary>
    public int AtlananDosyaSayisi { get; private set; }

    public DuzeltmeDeposu(string? kokKlasor = null, IKimlik? kimlik = null)
    {
        _kokKlasor = kokKlasor;
        _kimlik = kimlik ?? Kimlik.Varsayilan;
    }

    /// <summary>
    /// Verilen ders için düzeltmeler klasörünü belirler.
    /// </summary>
    public string HedefKlasor(string? dersAdi = null)
    {
        if (!string.IsNullOrWhiteSpace(_kokKlasor))
        {
            if (_kokKlasor.EndsWith(".duzeltmeler", StringComparison.OrdinalIgnoreCase) ||
                _kokKlasor.EndsWith(".duzeltmeler/", StringComparison.OrdinalIgnoreCase) ||
                _kokKlasor.EndsWith(".duzeltmeler\\", StringComparison.OrdinalIgnoreCase))
            {
                return _kokKlasor;
            }

            if (!string.IsNullOrWhiteSpace(dersAdi))
            {
                return Path.Combine(_kokKlasor, $"{dersAdi}.duzeltmeler");
            }

            return _kokKlasor;
        }

        string ad = string.IsNullOrWhiteSpace(dersAdi) ? "ders" : dersAdi;
        return Path.Combine(Directory.GetCurrentDirectory(), $"{ad}.duzeltmeler");
    }

    /// <summary>
    /// Dosya adı için kullanıcı bilgisini güvenli karakterlere dönüştürür.
    /// </summary>
    public static string GuvenliKullaniciAdi(string? kullanici)
    {
        if (string.IsNullOrWhiteSpace(kullanici))
        {
            return "anonim";
        }

        var sb = new StringBuilder(kullanici.Length);
        var gecersizler = Path.GetInvalidFileNameChars();

        foreach (char c in kullanici)
        {
            if (c == '\\' || c == '/' || c == ':' || c == '*' || c == '?' ||
                c == '"' || c == '<' || c == '>' || c == '|' || c == ' ' ||
                Array.IndexOf(gecersizler, c) >= 0)
            {
                sb.Append('_');
            }
            else
            {
                sb.Append(c);
            }
        }

        string sonuc = sb.ToString().Trim('_');
        return string.IsNullOrEmpty(sonuc) ? "anonim" : sonuc;
    }

    /// <summary>
    /// Belirtilen düzeltme için standart dosya adını üretir:
    /// yyyyMMddTHHmmssfffZ_kullanici_guidİlk6.json
    /// </summary>
    public static string DosyaAdiUret(Duzeltme duzeltme)
    {
        if (duzeltme == null)
        {
            throw new ArgumentNullException(nameof(duzeltme));
        }

        string zamanStr = duzeltme.ZamanUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        string guvenliKullanici = GuvenliKullaniciAdi(duzeltme.Kullanici);
        string guidKisa = duzeltme.Id.Replace("-", "");
        if (guidKisa.Length > 6)
        {
            guidKisa = guidKisa[..6];
        }
        else
        {
            guidKisa = guidKisa.PadRight(6, '0');
        }

        return $"{zamanStr}_{guvenliKullanici}_{guidKisa}.json";
    }

    /// <summary>
    /// Düzeltmeyi deposuna kaydeder.
    /// </summary>
    public string Yaz(Duzeltme duzeltme)
    {
        if (duzeltme == null)
        {
            throw new ArgumentNullException(nameof(duzeltme));
        }

        string klasor = HedefKlasor(duzeltme.Ders);
        return Yaz(klasor, duzeltme);
    }

    /// <summary>
    /// Düzeltmeyi belirtilen klasöre atomik olarak (.tmp + Move) yazar.
    /// </summary>
    public string Yaz(string klasor, Duzeltme duzeltme)
    {
        if (string.IsNullOrWhiteSpace(klasor))
        {
            throw new ArgumentException("Klasör yolu boş olamaz.", nameof(klasor));
        }
        if (duzeltme == null)
        {
            throw new ArgumentNullException(nameof(duzeltme));
        }

        Directory.CreateDirectory(klasor);

        string dosyaAdi = DosyaAdiUret(duzeltme);
        string sonDosyaYolu = Path.Combine(klasor, dosyaAdi);

        // Nadir çakışma durumunda benzersizleştirme
        if (File.Exists(sonDosyaYolu))
        {
            string guidKalan = duzeltme.Id.Replace("-", "");
            string ek = guidKalan.Length > 6 ? guidKalan[6..Math.Min(12, guidKalan.Length)] : Guid.NewGuid().ToString("N")[..6];
            string zamanStr = duzeltme.ZamanUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
            string guvenliKullanici = GuvenliKullaniciAdi(duzeltme.Kullanici);
            dosyaAdi = $"{zamanStr}_{guvenliKullanici}_{guidKalan[..6]}_{ek}.json";
            sonDosyaYolu = Path.Combine(klasor, dosyaAdi);
        }

        string tmpDosyaAdi = $"{Path.GetFileNameWithoutExtension(dosyaAdi)}_{Guid.NewGuid():N}.tmp";
        string tmpYolu = Path.Combine(klasor, tmpDosyaAdi);

        try
        {
            using (var fs = new FileStream(tmpYolu, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                using (var writer = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = false, Encoder = JsonAyar.Encoder }))
                {
                    JsonSerializer.Serialize(writer, duzeltme, JsonAyar);
                }
                fs.Flush(true);
            }

            int deneme = 0;
            while (true)
            {
                try
                {
                    File.Move(tmpYolu, sonDosyaYolu);
                    break;
                }
                catch (IOException) when (deneme < 10)
                {
                    deneme++;
                    Thread.Sleep(5 * deneme);
                }
            }

            return sonDosyaYolu;
        }
        catch
        {
            // Yalnızca tmp başarısızlık temizliği
            try
            {
                if (File.Exists(tmpYolu))
                {
                    File.Delete(tmpYolu);
                }
            }
            catch
            {
                // Temizlik istisnasını yut
            }
            throw;
        }
    }

    /// <summary>
    /// Klasördeki geçerli tüm düzeltmeleri okur, bozuk ve tmp dosyaları sayar.
    /// </summary>
    public IReadOnlyList<Duzeltme> DuzeltmeleriYukle(string klasor)
    {
        var liste = new List<Duzeltme>();
        if (string.IsNullOrWhiteSpace(klasor) || !Directory.Exists(klasor))
        {
            return liste;
        }

        foreach (string dosyaYolu in Directory.EnumerateFiles(klasor))
        {
            if (dosyaYolu.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                AtlananDosyaSayisi++;
                continue;
            }

            try
            {
                string json = File.ReadAllText(dosyaYolu, Encoding.UTF8);
                var d = JsonSerializer.Deserialize<Duzeltme>(json, JsonAyar);
                if (d == null || d.Sema != 1 || string.IsNullOrWhiteSpace(d.Id))
                {
                    AtlananDosyaSayisi++;
                    continue;
                }
                liste.Add(d);
            }
            catch
            {
                AtlananDosyaSayisi++;
            }
        }

        return liste;
    }

    /// <summary>
    /// Transkript ile varsayılan klasördeki düzeltmeleri birleştirir.
    /// </summary>
    public BirlestirmeSonucu Birlestir(Transkript transkript)
    {
        if (transkript == null)
        {
            throw new ArgumentNullException(nameof(transkript));
        }

        string klasor = HedefKlasor(transkript.Ad);
        return Birlestir(transkript, klasor);
    }

    /// <summary>
    /// Transkript ile belirtilen klasördeki düzeltmeleri birleştirir.
    /// </summary>
    public BirlestirmeSonucu Birlestir(Transkript transkript, string klasor)
    {
        if (transkript == null)
        {
            throw new ArgumentNullException(nameof(transkript));
        }

        AtlananDosyaSayisi = 0;
        var tumDuzeltmeler = DuzeltmeleriYukle(klasor);

        var segmentDuzeltmeleri = new Dictionary<int, List<Duzeltme>>();
        var yetimler = new List<Duzeltme>();
        var guncelMetinler = new Dictionary<int, string>();
        var gecmisMetinler = new Dictionary<int, List<string>>();

        foreach (var s in transkript.Segmentler)
        {
            segmentDuzeltmeleri[s.No] = new List<Duzeltme>();
            guncelMetinler[s.No] = s.Metin;
            gecmisMetinler[s.No] = new List<string> { s.Metin };
        }

        // Düzeltmeleri zaman sırasına göre çöz: (zaman_utc, id)
        var siraliTumDuzeltmeler = tumDuzeltmeler
            .OrderBy(d => d.ZamanUtc)
            .ThenBy(d => d.Id, StringComparer.Ordinal)
            .ToList();

        foreach (var d in siraliTumDuzeltmeler)
        {
            bool kaynakOzetUyusuyor = KaynakOzetUyusuyor(transkript.KaynakOzet, d.KaynakOzet);

            if (kaynakOzetUyusuyor)
            {
                if (segmentDuzeltmeleri.TryGetValue(d.SegmentNo, out var liste))
                {
                    liste.Add(d);
                    string yeniMetin = d.Yeni;
                    if (string.Equals(d.Tur, "geri_al", StringComparison.OrdinalIgnoreCase))
                    {
                        var hedef = liste.FirstOrDefault(x => x.Id == d.GeriAlHedef)
                                 ?? siraliTumDuzeltmeler.FirstOrDefault(x => x.Id == d.GeriAlHedef);
                        if (hedef != null)
                        {
                            yeniMetin = hedef.Eski;
                        }
                    }
                    guncelMetinler[d.SegmentNo] = yeniMetin;
                    gecmisMetinler[d.SegmentNo].Add(yeniMetin);
                }
                else
                {
                    yetimler.Add(d);
                }
            }
            else
            {
                // Bulgu 2: Bas yoksa zaman şartı atlanamaz; zaman yoksa düzeltme Yetim sayılır
                if (!d.Bas.HasValue)
                {
                    yetimler.Add(d);
                    continue;
                }

                // Bulgu 1: Eşleşeni zamanla bulup zinciri güncel metin üzerinden çöz
                Segment? eslesenSegment = SegmentEslestir(
                    transkript.Segmentler,
                    d,
                    guncelMetinler,
                    gecmisMetinler,
                    segmentDuzeltmeleri);

                if (eslesenSegment != null && segmentDuzeltmeleri.TryGetValue(eslesenSegment.No, out var liste))
                {
                    liste.Add(d);
                    string yeniMetin = d.Yeni;
                    if (string.Equals(d.Tur, "geri_al", StringComparison.OrdinalIgnoreCase))
                    {
                        var hedef = liste.FirstOrDefault(x => x.Id == d.GeriAlHedef)
                                 ?? siraliTumDuzeltmeler.FirstOrDefault(x => x.Id == d.GeriAlHedef);
                        if (hedef != null)
                        {
                            yeniMetin = hedef.Eski;
                        }
                    }
                    guncelMetinler[eslesenSegment.No] = yeniMetin;
                    gecmisMetinler[eslesenSegment.No].Add(yeniMetin);
                }
                else
                {
                    yetimler.Add(d);
                }
            }
        }

        var efektifSegmentler = new List<EfektifSegment>(transkript.Segmentler.Count);

        foreach (var s in transkript.Segmentler)
        {
            var bagli = segmentDuzeltmeleri[s.No];
            var sirali = bagli
                .OrderBy(d => d.ZamanUtc)
                .ThenBy(d => d.Id, StringComparer.Ordinal)
                .ToList();

            if (sirali.Count == 0)
            {
                efektifSegmentler.Add(new EfektifSegment(s.No, s.Bas, s.Son, s.Metin, Array.Empty<Duzeltme>(), false, s.Ceviri));
                continue;
            }

            // null OncekiId kök anahtardır. Aynı anahtarı paylaşan ≥2 kayıt çakışır.
            // "cozum" dalları birleştirir: elenmez, çakışma bayrağını kaldırır.
            var (elenenIdler, cakisma) = CakismayiCoz(sirali);
            string guncelMetin = s.Metin;

            foreach (var d in sirali)
            {
                if (!string.IsNullOrEmpty(d.Id) && elenenIdler.Contains(d.Id))
                {
                    continue;
                }

                guncelMetin = MetniUygula(d, sirali, tumDuzeltmeler, guncelMetin);
            }

            efektifSegmentler.Add(new EfektifSegment(s.No, s.Bas, s.Son, guncelMetin, sirali, cakisma, s.Ceviri));
        }

        return new BirlestirmeSonucu(efektifSegmentler, yetimler, AtlananDosyaSayisi);
    }

    /// <summary>null / boş onceki_id çakışma grubunda ortak kök sayılır.</summary>
    private const string KokOncekiAnahtar = "\0kok";

    /// <summary>
    /// Elenmemiş zincirin ucundaki kayıt kimliği. Çakışmada elenen dallar uca sayılmaz.
    /// Kayıt yoksa veya tümü elendiyse null (kök).
    /// </summary>
    public static string? ElenmemisZincirUcu(IReadOnlyList<Duzeltme>? gecmis)
    {
        if (gecmis == null || gecmis.Count == 0)
        {
            return null;
        }

        var sirali = gecmis
            .OrderBy(d => d.ZamanUtc)
            .ThenBy(d => d.Id, StringComparer.Ordinal)
            .ToList();
        var (elenen, _) = CakismayiCoz(sirali);
        string? uc = null;
        foreach (var d in sirali)
        {
            if (!string.IsNullOrEmpty(d.Id) && !elenen.Contains(d.Id))
            {
                uc = d.Id;
            }
        }

        return uc;
    }

    private static string OncekiAnahtar(Duzeltme d) =>
        string.IsNullOrEmpty(d.OncekiId) ? KokOncekiAnahtar : d.OncekiId!;

    private static bool CozumKaydiMi(Duzeltme d) =>
        string.Equals(d.Tur, "cozum", StringComparison.OrdinalIgnoreCase);

    private static bool GeriAlKaydiMi(Duzeltme d) =>
        string.Equals(d.Tur, "geri_al", StringComparison.OrdinalIgnoreCase);

    private static (HashSet<string> Elenen, bool Cakisma) CakismayiCoz(List<Duzeltme> sirali)
    {
        var elenen = new HashSet<string>(StringComparer.Ordinal);
        if (sirali.Count == 0)
        {
            return (elenen, false);
        }

        var gruplar = sirali.GroupBy(OncekiAnahtar, StringComparer.Ordinal).ToList();

        foreach (var g in gruplar)
        {
            if (g.Count() < 2)
            {
                continue;
            }

            var cozumler = new List<Duzeltme>();
            foreach (var d in g)
            {
                if (CozumKaydiMi(d))
                {
                    cozumler.Add(d);
                }
                else if (!string.IsNullOrEmpty(d.Id))
                {
                    elenen.Add(d.Id);
                }
            }

            // Aynı ebeveynde tek çözüm dalları birleştirir; birden fazlası yeniden çakışır.
            if (cozumler.Count != 1)
            {
                foreach (var d in cozumler)
                {
                    if (!string.IsNullOrEmpty(d.Id))
                    {
                        elenen.Add(d.Id);
                    }
                }
            }
        }

        bool degisti;
        do
        {
            degisti = false;
            foreach (var d in sirali)
            {
                if (string.IsNullOrEmpty(d.Id) || elenen.Contains(d.Id) || CozumKaydiMi(d))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(d.OncekiId) && elenen.Contains(d.OncekiId) && elenen.Add(d.Id))
                {
                    degisti = true;
                }
            }
        } while (degisti);

        var geriAlinan = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in sirali)
        {
            if (GeriAlKaydiMi(d) && !string.IsNullOrEmpty(d.GeriAlHedef))
            {
                geriAlinan.Add(d.GeriAlHedef);
            }
        }

        bool cakisma = false;
        foreach (var g in gruplar)
        {
            if (g.Count() < 2)
            {
                continue;
            }

            if (!GrupCozulduMu(g, sirali, elenen, geriAlinan))
            {
                cakisma = true;
                break;
            }
        }

        return (elenen, cakisma);
    }

    private static bool GrupCozulduMu(
        IEnumerable<Duzeltme> grup,
        List<Duzeltme> sirali,
        HashSet<string> elenen,
        HashSet<string> geriAlinan)
    {
        foreach (var d in grup)
        {
            if (YasayanCozumMu(d, elenen, geriAlinan))
            {
                return true;
            }
        }

        var uyeIdler = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in grup)
        {
            if (!string.IsNullOrEmpty(d.Id))
            {
                uyeIdler.Add(d.Id);
            }
        }

        foreach (var d in sirali)
        {
            if (!YasayanCozumMu(d, elenen, geriAlinan))
            {
                continue;
            }

            string? cur = d.OncekiId;
            var ziyaret = new HashSet<string>(StringComparer.Ordinal);
            while (!string.IsNullOrEmpty(cur) && ziyaret.Add(cur))
            {
                if (uyeIdler.Contains(cur))
                {
                    return true;
                }
                var onceki = sirali.FirstOrDefault(x => x.Id == cur);
                cur = onceki?.OncekiId;
            }
        }

        return false;
    }

    private static bool YasayanCozumMu(Duzeltme d, HashSet<string> elenen, HashSet<string> geriAlinan) =>
        CozumKaydiMi(d) &&
        !string.IsNullOrEmpty(d.Id) &&
        !elenen.Contains(d.Id) &&
        !geriAlinan.Contains(d.Id);

    private static string MetniUygula(
        Duzeltme d,
        IReadOnlyList<Duzeltme> sirali,
        IReadOnlyList<Duzeltme> tum,
        string guncel)
    {
        if (!GeriAlKaydiMi(d))
        {
            return d.Yeni;
        }

        var hedef = sirali.FirstOrDefault(x => x.Id == d.GeriAlHedef)
                 ?? tum.FirstOrDefault(x => x.Id == d.GeriAlHedef);
        if (hedef != null)
        {
            return hedef.Eski;
        }

        if (!string.IsNullOrEmpty(d.Yeni))
        {
            return d.Yeni;
        }

        return guncel;
    }

    private static Segment? SegmentEslestir(
        IReadOnlyList<Segment> segmentler,
        Duzeltme d,
        IReadOnlyDictionary<int, string> guncelMetinler,
        IReadOnlyDictionary<int, List<string>> gecmisMetinler,
        IReadOnlyDictionary<int, List<Duzeltme>> segmentDuzeltmeleri)
    {
        // Bulgu 2: Bas yoksa zaman şartı atlanamaz, düzeltme Yetim sayılır
        if (!d.Bas.HasValue)
        {
            return null;
        }

        // Bulgu 1: Eşleşeni zamanla bulup zinciri güncel metin üzerinden çöz
        var zamanEslesenleri = segmentler
            .Where(s => Math.Abs(s.Bas - d.Bas.Value) <= 0.5001)
            .OrderBy(s => Math.Abs(s.Bas - d.Bas.Value))
            .ToList();

        if (zamanEslesenleri.Count == 0)
        {
            return null;
        }

        // 1. Doğrudan güncel metin ile eşleşen segmente bak
        foreach (var s in zamanEslesenleri)
        {
            if (guncelMetinler.TryGetValue(s.No, out var gm) && MetinlerEslesiyor(gm, d.Eski))
            {
                return s;
            }
        }

        // 2. Ham transkript metni ile eşleşen segmente bak
        foreach (var s in zamanEslesenleri)
        {
            if (MetinlerEslesiyor(s.Metin, d.Eski))
            {
                return s;
            }
        }

        // 3. Zincir (OncekiId) veya geçmiş metinler üzerinden eşleşeni bul
        foreach (var s in zamanEslesenleri)
        {
            bool oncekiIdEslesiyor = !string.IsNullOrEmpty(d.OncekiId) &&
                segmentDuzeltmeleri.TryGetValue(s.No, out var bagli) &&
                bagli.Any(x => x.Id == d.OncekiId);

            bool gecmisMetinEslesiyor = gecmisMetinler.TryGetValue(s.No, out var gecmis) &&
                gecmis.Any(m => MetinlerEslesiyor(m, d.Eski));

            if (oncekiIdEslesiyor || gecmisMetinEslesiyor)
            {
                return s;
            }
        }

        return null;
    }

    private static Segment? SegmentEslestir(IReadOnlyList<Segment> segmentler, Duzeltme d)
    {
        // Bulgu 2: Bas yoksa zaman şartı atlanamaz, düzeltme Yetim sayılır
        if (!d.Bas.HasValue)
        {
            return null;
        }

        Segment? enIyi = null;
        double enKucukFark = double.MaxValue;

        foreach (var s in segmentler)
        {
            if (!MetinlerEslesiyor(s.Metin, d.Eski))
            {
                continue;
            }

            double fark = Math.Abs(s.Bas - d.Bas.Value);
            if (fark <= 0.5001 && fark < enKucukFark)
            {
                enKucukFark = fark;
                enIyi = s;
            }
        }

        return enIyi;
    }

    private static bool MetinlerEslesiyor(string? metin1, string? metin2)
    {
        if (metin1 == null && metin2 == null) return true;
        if (metin1 == null || metin2 == null) return false;
        if (string.Equals(metin1, metin2, StringComparison.Ordinal)) return true;
        return string.Equals(metin1.Trim(), metin2.Trim(), StringComparison.Ordinal);
    }

    private static bool KaynakOzetUyusuyor(string? transkriptOzet, string? duzeltmeOzet)
    {
        if (string.IsNullOrEmpty(transkriptOzet) && string.IsNullOrEmpty(duzeltmeOzet))
        {
            return true;
        }
        if (string.IsNullOrEmpty(transkriptOzet) || string.IsNullOrEmpty(duzeltmeOzet))
        {
            return false;
        }
        return string.Equals(transkriptOzet, duzeltmeOzet, StringComparison.Ordinal);
    }
}
