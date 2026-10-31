namespace Portal.Cekirdek;

using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

/// <summary>
/// Bir dersin önbellek geçerlilik anahtarı.
/// </summary>
public record DersOnbellekAnahtari(
    long JsonZamanUtcTicks,
    long JsonBoyut,
    int DuzeltmeSayisi,
    long DuzeltmeEnYeniUtcTicks
)
{
    /// <summary>
    /// Ders kaydı için dosya ve düzeltme meta verilerinden anahtar üretir.
    /// </summary>
    public static DersOnbellekAnahtari Uret(DersKaydi ders)
    {
        long jsonZamanTicks = 0;
        long jsonBoyut = 0;
        if (!string.IsNullOrEmpty(ders.JsonYolu) && File.Exists(ders.JsonYolu))
        {
            try
            {
                var fi = new FileInfo(ders.JsonYolu);
                jsonZamanTicks = fi.LastWriteTimeUtc.Ticks;
                jsonBoyut = fi.Length;
            }
            catch
            {
                // Dosya erişim hatasını yut
            }
        }

        int duzeltmeSayisi = 0;
        long enYeniDuzeltmeTicks = 0;

        string duzeltmeKlasoru = DuzeltmelerKlasoruBul(ders);
        if (Directory.Exists(duzeltmeKlasoru))
        {
            try
            {
                var dosyalar = Directory.GetFiles(duzeltmeKlasoru)
                    .Where(f => !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                duzeltmeSayisi = dosyalar.Length;
                foreach (var d in dosyalar)
                {
                    try
                    {
                        long t = File.GetLastWriteTimeUtc(d).Ticks;
                        if (t > enYeniDuzeltmeTicks)
                        {
                            enYeniDuzeltmeTicks = t;
                        }
                    }
                    catch
                    {
                        // Tekil dosya hatasını yut
                    }
                }
            }
            catch
            {
                // Dizin erişim hatasını yut
            }
        }

        return new DersOnbellekAnahtari(jsonZamanTicks, jsonBoyut, duzeltmeSayisi, enYeniDuzeltmeTicks);
    }

    /// <summary>
    /// Ders kaydına ait .duzeltmeler klasörünü bulur veya varsayılan yolu döner.
    /// </summary>
    public static string DuzeltmelerKlasoruBul(DersKaydi ders)
    {
        if (!string.IsNullOrEmpty(ders.KlasorYolu))
        {
            string p1 = Path.Combine(ders.KlasorYolu, $"{ders.Ad}.duzeltmeler");
            if (Directory.Exists(p1))
            {
                return p1;
            }
        }

        if (!string.IsNullOrEmpty(ders.JsonYolu))
        {
            string? dir = Path.GetDirectoryName(ders.JsonYolu);
            if (!string.IsNullOrEmpty(dir))
            {
                string p2 = Path.Combine(dir, $"{ders.Ad}.duzeltmeler");
                if (Directory.Exists(p2))
                {
                    return p2;
                }
            }
        }

        return !string.IsNullOrEmpty(ders.KlasorYolu)
            ? Path.Combine(ders.KlasorYolu, $"{ders.Ad}.duzeltmeler")
            : $"{ders.Ad}.duzeltmeler";
    }
}

/// <summary>
/// Önbelleğe kaydedilen tek bir segment bilgisi.
/// </summary>
public record OnbellekSegment(
    int No,
    double Bas,
    double Son,
    string Metin,
    string? Ceviri = null
);

/// <summary>
/// Önbellekte saklanan ders verisi.
/// </summary>
public class OnbellekDers
{
    public string DersAd { get; set; } = string.Empty;
    public string JsonYolu { get; set; } = string.Empty;
    public DersOnbellekAnahtari Anahtar { get; set; } = new(0, 0, 0, 0);
    public List<OnbellekSegment> Segmentler { get; set; } = new();
}

/// <summary>
/// Disk üzerinde JSON olarak saklanan önbellek yapısı.
/// </summary>
public class OnbellekVerisi
{
    public int Surum { get; set; } = IndeksOnbellek.GuncelSurum;
    public DateTime KayitZamaniUtc { get; set; } = DateTime.UtcNow;
    public Dictionary<string, OnbellekDers> Dersler { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Yerel disk önbelleğini yöneten sınıf.
/// </summary>
public class IndeksOnbellek
{
    public const int GuncelSurum = 3;

    private static readonly JsonSerializerOptions JsonAyar = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        WriteIndented = false
    };

    private readonly string? _ozelDosyaYolu;
    private readonly string? _ozelKlasorYolu;
    private readonly string? _kokKlasor;

    public IndeksOnbellek(string? dosyaVeyaKlasorYolu = null, string? kokKlasor = null)
    {
        _kokKlasor = kokKlasor;
        if (!string.IsNullOrWhiteSpace(dosyaVeyaKlasorYolu))
        {
            if (dosyaVeyaKlasorYolu.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                dosyaVeyaKlasorYolu.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            {
                _ozelDosyaYolu = Path.GetFullPath(dosyaVeyaKlasorYolu);
            }
            else
            {
                _ozelKlasorYolu = Path.GetFullPath(dosyaVeyaKlasorYolu);
            }
        }
    }

    /// <summary>
    /// Kök dizin parametresine göre önbellek dosyasının tam yolunu üretir.
    /// </summary>
    public string DosyaYoluBelirle(string? kok = null)
    {
        if (!string.IsNullOrEmpty(_ozelDosyaYolu))
        {
            return _ozelDosyaYolu;
        }

        string hedefKok = kok ?? _kokKlasor ?? string.Empty;
        string hash = Sha1Hesapla(hedefKok);

        string anaKlasor = !string.IsNullOrEmpty(_ozelKlasorYolu)
            ? _ozelKlasorYolu
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sarnic", "indeks");

        return Path.Combine(anaKlasor, $"{hash}.json");
    }

    /// <summary>
    /// Önbellek dosyasını okur. Dosya yoksa veya bozuksa sessizce null döner.
    /// </summary>
    public OnbellekVerisi? Yukle(string? kok = null)
    {
        string dosyaYolu = DosyaYoluBelirle(kok);
        if (!File.Exists(dosyaYolu))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(dosyaYolu, Encoding.UTF8);
            var veri = JsonSerializer.Deserialize<OnbellekVerisi>(json, JsonAyar);
            if (veri == null || veri.Surum != GuncelSurum || veri.Dersler == null)
            {
                return null;
            }

            var gecersizAnahtarlar = new List<string>();
            foreach (var kvp in veri.Dersler)
            {
                var ders = kvp.Value;
                if (ders == null || ders.Anahtar == null || !SegmentlerGecerliMi(ders.Segmentler))
                {
                    gecersizAnahtarlar.Add(kvp.Key);
                }
            }

            foreach (var k in gecersizAnahtarlar)
            {
                veri.Dersler.Remove(k);
            }

            return veri;
        }
        catch
        {
            // Bozuk/eski sürüm önbellek sessizce yok sayılır
            return null;
        }
    }

    private static bool SegmentlerGecerliMi(List<OnbellekSegment>? segmentler)
    {
        if (segmentler == null)
        {
            return false;
        }

        foreach (var seg in segmentler)
        {
            if (seg == null || seg.Metin == null)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Önbellek verisini atomik olarak (.tmp + Move) diske kaydeder.
    /// </summary>
    public void Kaydet(OnbellekVerisi veri, string? kok = null)
    {
        if (veri == null)
        {
            throw new ArgumentNullException(nameof(veri));
        }

        string dosyaYolu = DosyaYoluBelirle(kok);
        string? klasor = Path.GetDirectoryName(dosyaYolu);
        if (!string.IsNullOrEmpty(klasor))
        {
            Directory.CreateDirectory(klasor);
        }

        string tmpYolu = Path.Combine(klasor ?? ".", $"{Path.GetFileName(dosyaYolu)}.{Guid.NewGuid():N}.tmp");

        try
        {
            string json = JsonSerializer.Serialize(veri, JsonAyar);
            File.WriteAllText(tmpYolu, json, Encoding.UTF8);
            File.Move(tmpYolu, dosyaYolu, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tmpYolu))
                {
                    File.Delete(tmpYolu);
                }
            }
            catch
            {
                // tmp temizleme istisnasını yut
            }
            throw;
        }
    }

    /// <summary>
    /// Metnin SHA1 özetini küçük harfli onaltılık dize olarak döner.
    /// </summary>
    public static string Sha1Hesapla(string girdi)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(girdi.Trim().ToLowerInvariant());
        byte[] hash = SHA1.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
