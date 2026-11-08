namespace Portal.Cekirdek.Yonetim;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>Kuyruktaki iş türleri ve durumları.</summary>
public static class IsSabitleri
{
    public const string TurIsle = "isle";
    public const string TurBelge = "belge";
    public const string TurDizinle = "dizinle";

    public const string Bekliyor = "bekliyor";
    public const string Calisiyor = "calisiyor";
    public const string Bitti = "bitti";
    public const string Hata = "hata";
    public const string Iptal = "iptal";

    public static bool TurGecerli(string? tur) => tur is TurIsle or TurBelge or TurDizinle;
}

/// <summary>Yönetim komutlarının adları.</summary>
public static class Eylemler
{
    public const string Dogrula = "dogrula";
    public const string IsEkle = "is_ekle";
    public const string Iptal = "iptal";
    public const string YenidenDene = "yeniden_dene";
    public const string Temizle = "temizle";
}

public sealed class IsKaydi
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("tur")] public string Tur { get; set; } = "";
    [JsonPropertyName("yol")] public string Yol { get; set; } = "";
    [JsonPropertyName("cikis")] public string? Cikis { get; set; }
    [JsonPropertyName("durum")] public string Durum { get; set; } = IsSabitleri.Bekliyor;
    [JsonPropertyName("ekleyen")] public string Ekleyen { get; set; } = "";
    [JsonPropertyName("eklenme")] public string Eklenme { get; set; } = "";
    [JsonPropertyName("baslama")] public string? Baslama { get; set; }
    [JsonPropertyName("bitis")] public string? Bitis { get; set; }
    [JsonPropertyName("son_satir")] public string? SonSatir { get; set; }
    [JsonPropertyName("cikis_kodu")] public int? CikisKodu { get; set; }
}

public sealed class YonetimDurumu
{
    [JsonPropertyName("zaman")] public string Zaman { get; set; } = "";
    [JsonPropertyName("makine")] public string Makine { get; set; } = "";
    [JsonPropertyName("disk_bos_gb")] public double DiskBosGb { get; set; }
    [JsonPropertyName("isler")] public List<IsKaydi> Isler { get; set; } = new();
}

public sealed class YonetimKomutu
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("zaman")] public long Zaman { get; set; }
    [JsonPropertyName("eylem")] public string Eylem { get; set; } = "";
    [JsonPropertyName("kullanici")] public string Kullanici { get; set; } = "";
    [JsonPropertyName("is_id")] public long? IsId { get; set; }
    [JsonPropertyName("tur")] public string? Tur { get; set; }
    [JsonPropertyName("yol")] public string? Yol { get; set; }
    [JsonPropertyName("cikis")] public string? Cikis { get; set; }
    [JsonPropertyName("imza")] public string? Imza { get; set; }

    /// <summary>İmzalanan metin. Komutun her alanı içindedir; değiştirilirse imza tutmaz.</summary>
    public string ImzaMetni() => string.Join("\n", Id, Zaman, Eylem, Kullanici, IsId, Tur, Yol, Cikis);
}

public sealed class KomutSonucu
{
    [JsonPropertyName("basarili")] public bool Basarili { get; set; }
    [JsonPropertyName("mesaj")] public string Mesaj { get; set; } = "";
    [JsonPropertyName("yetki_gerekli")] public bool YetkiGerekli { get; set; }
}


/// <summary>Servisin paylaşım klasörüne yazdığı bağlantı bilgisi. Gizli değer içermez.</summary>
public sealed class ServisBilgisi
{
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("parmak_izi")] public string? ParmakIzi { get; set; }
    [JsonPropertyName("tuz")] public string? Tuz { get; set; }
    [JsonPropertyName("iter")] public int Iter { get; set; }
}

/// <summary>Yönetim parolasından anahtar türetir, komut imzalar ve imza doğrular.</summary>
public static class YonetimImza
{
    public const int StandartIterasyon = 310_000;
    public const int EnAzParolaUzunlugu = 12;
    private const int AnahtarBayt = 32;

    public static byte[] AnahtarTuret(string parola, byte[] tuz, int iterasyon) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(parola), tuz, iterasyon, HashAlgorithmName.SHA256, AnahtarBayt);

    public static string Imzala(YonetimKomutu komut, byte[] anahtar) =>
        Convert.ToBase64String(HMACSHA256.HashData(anahtar, Encoding.UTF8.GetBytes(komut.ImzaMetni())));

    public static bool Dogrula(YonetimKomutu komut, byte[] anahtar)
    {
        if (string.IsNullOrEmpty(komut.Imza))
        {
            return false;
        }
        byte[] beklenen = HMACSHA256.HashData(anahtar, Encoding.UTF8.GetBytes(komut.ImzaMetni()));
        byte[] gelen;
        try
        {
            gelen = Convert.FromBase64String(komut.Imza);
        }
        catch (FormatException)
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(beklenen, gelen);
    }
}

public static class YonetimDosyalari
{
    public const string KlasorAdi = "yonetim";
    public const string ServisDosyasi = "servis.json";
    public const string DurumDosyasi = "durum.json";

    /// <summary>Paylaşımda yarım dosya görünmesin: önce geçici ad, sonra yeniden adlandırma.</summary>
    public static void AtomikYaz<T>(string hedef, string geciciKlasor, T veri, bool uzerineYaz)
    {
        Directory.CreateDirectory(geciciKlasor);
        Directory.CreateDirectory(Path.GetDirectoryName(hedef)!);
        string gecici = Path.Combine(geciciKlasor, $"{Guid.NewGuid():N}.tmp");
        try
        {
            using (var fs = new FileStream(gecici, FileMode.CreateNew, FileAccess.Write))
            {
                JsonSerializer.Serialize(fs, veri);
                fs.Flush(true);
            }
            File.Move(gecici, hedef, uzerineYaz);
        }
        finally
        {
            if (File.Exists(gecici))
            {
                File.Delete(gecici);
            }
        }
    }
}
