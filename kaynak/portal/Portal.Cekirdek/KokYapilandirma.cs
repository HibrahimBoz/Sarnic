namespace Portal.Cekirdek;

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

/// <summary>
/// Kök klasör yapılandırmasını çözen, okuyan ve kaydeden saf mantık sınıfı.
/// </summary>
public class KokYapilandirma
{
    private static readonly JsonSerializerOptions JsonAyar = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        WriteIndented = true
    };

    private static readonly JsonDocumentOptions DokumanAyar = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private readonly string? _kullaniciAyarYolu;
    private readonly string? _paketAyarYolu;

    /// <summary>
    /// Varsayılan kullanıcı ayar dosyası yolu (%LOCALAPPDATA%\Sarnic\kullanici.json).
    /// </summary>
    public static string VarsayilanKullaniciAyarYolu => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Sarnic",
        "kullanici.json"
    );

    /// <summary>
    /// Varsayılan paketlenen ayar dosyası yolu (uygulama klasöründe ayar.json).
    /// </summary>
    public static string VarsayilanPaketAyarYolu => Path.Combine(
        AppContext.BaseDirectory,
        "ayar.json"
    );

    /// <summary>
    /// Testler ve özel yollar için enjekte edilebilir yapıcı.
    /// </summary>
    public KokYapilandirma(string? kullaniciAyarYolu = null, string? paketAyarYolu = null)
    {
        _kullaniciAyarYolu = kullaniciAyarYolu;
        _paketAyarYolu = paketAyarYolu;
    }

    /// <summary>
    /// Kök dizinini öncelik sırasına göre çözer (örnek metot):
    /// 1. Komut satırı argümanı (--kok)
    /// 2. Kullanıcı ayarı (kullanici.json)
    /// 3. Paketlenen ayar (ayar.json)
    /// 4. Hiçbiri yoksa null
    /// </summary>
    public string? Coz(string[]? args = null) => Coz(args, _kullaniciAyarYolu, _paketAyarYolu);

    /// <summary>
    /// Kullanıcı ayarına kök yolunu atomik olarak kaydeder (örnek metot).
    /// </summary>
    public void Kaydet(string kok) => KullaniciKokuKaydet(kok, _kullaniciAyarYolu);

    /// <summary>
    /// Kullanıcı ayarından kök yolunu okur (örnek metot).
    /// </summary>
    public string? KullaniciKokuOku() => KullaniciKokuOku(_kullaniciAyarYolu);

    /// <summary>
    /// Paketlenen ayardan kök yolunu okur (örnek metot).
    /// </summary>
    public string? PaketlenenKokuOku() => PaketlenenKokuOku(_paketAyarYolu);

    /// <summary>
    /// Uygulama başlatma akışını örnek ayarlarıyla yürütür.
    /// </summary>
    public string? UygulamaKokuBelirle(
        string[]? args,
        Func<string, bool> uyariVeDegistirSor,
        Func<string?> klasorSec,
        Func<string?, bool>? erisilebilirMi = null)
        => UygulamaKokuBelirle(args, uyariVeDegistirSor, klasorSec, _kullaniciAyarYolu, _paketAyarYolu, erisilebilirMi);

    /// <summary>
    /// Kök dizinini öncelik sırasına göre çözer:
    /// 1. Komut satırı --kok &lt;yol&gt; (varsa)
    /// 2. Kullanıcı ayarı (%LOCALAPPDATA%\Sarnic\kullanici.json)
    /// 3. Paketlenen ayar.json (uygulama klasöründe, AppContext.BaseDirectory)
    /// 4. Hiçbiri yoksa null
    /// Not: Yolun diskte var olup olmadığı burada denetlenmez.
    /// </summary>
    public static string? Coz(string[]? args = null, string? kullaniciAyarYolu = null, string? paketAyarYolu = null)
    {
        // 1. Komut satırı argümanı
        string? argKok = ParseKokArgumani(args);
        if (!string.IsNullOrWhiteSpace(argKok))
        {
            return argKok;
        }

        // 2. Kullanıcı ayarı
        string? kullaniciKok = KullaniciKokuOku(kullaniciAyarYolu);
        if (!string.IsNullOrWhiteSpace(kullaniciKok))
        {
            return kullaniciKok;
        }

        // 3. Paketlenen ayar
        string? paketKok = PaketlenenKokuOku(paketAyarYolu);
        if (!string.IsNullOrWhiteSpace(paketKok))
        {
            return paketKok;
        }

        // 4. Hiçbiri yok
        return null;
    }

    /// <summary>
    /// Komut satırı argümanları arasından --kok değerini ayrıştırır.
    /// Desteklenen biçimler: --kok &lt;yol&gt; ve --kok=&lt;yol&gt;
    /// </summary>
    public static string? ParseKokArgumani(string[]? args)
    {
        if (args == null || args.Length == 0)
        {
            return null;
        }

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (string.Equals(arg, "--kok", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    string deger = args[i + 1].Trim().Trim('"', '\'');
                    return string.IsNullOrWhiteSpace(deger) ? null : deger;
                }
            }
            else if (arg.StartsWith("--kok=", StringComparison.OrdinalIgnoreCase))
            {
                string deger = arg.Substring("--kok=".Length).Trim().Trim('"', '\'');
                return string.IsNullOrWhiteSpace(deger) ? null : deger;
            }
        }

        return null;
    }

    /// <summary>
    /// Kullanıcı ayar dosyasından kök yolunu okur.
    /// Bozuk veya boş JSON sessizce yok sayılır.
    /// </summary>
    public static string? KullaniciKokuOku(string? kullaniciAyarYolu = null)
    {
        if (!string.IsNullOrWhiteSpace(kullaniciAyarYolu))
        {
            return JsonDosyasindanKokOku(kullaniciAyarYolu);
        }

        if (File.Exists(VarsayilanKullaniciAyarYolu))
        {
            return JsonDosyasindanKokOku(VarsayilanKullaniciAyarYolu);
        }

        // Eski kurulumun ayarı: yeni dosya yoksa okunur, yazma her zaman yeni yola.
        return JsonDosyasindanKokOku(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VideoPortali",
            "kullanici.json"));
    }

    /// <summary>
    /// Paketlenen ayar.json dosyasından kök yolunu okur.
    /// Bozuk veya boş JSON sessizce yok sayılır.
    /// </summary>
    public static string? PaketlenenKokuOku(string? paketAyarYolu = null)
    {
        string dosyaYolu = string.IsNullOrWhiteSpace(paketAyarYolu)
            ? VarsayilanPaketAyarYolu
            : paketAyarYolu;

        return JsonDosyasindanKokOku(dosyaYolu);
    }

    /// <summary>
    /// JSON dosyasından "kok" anahtarının değerini okur.
    /// Bozuk, boş veya geçersiz dosyalarda hata fırlatmaz, null döner.
    /// </summary>
    public static string? JsonDosyasindanKokOku(string dosyaYolu)
    {
        if (string.IsNullOrWhiteSpace(dosyaYolu) || !File.Exists(dosyaYolu))
        {
            return null;
        }

        try
        {
            string metin = File.ReadAllText(dosyaYolu, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(metin))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(metin, DokumanAyar);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name, "kok", StringComparison.OrdinalIgnoreCase))
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        string? deger = prop.Value.GetString();
                        return string.IsNullOrWhiteSpace(deger) ? null : deger;
                    }
                    return null;
                }
            }

            return null;
        }
        catch
        {
            // Bozuk veya geçersiz JSON yok sayılır
            return null;
        }
    }

    /// <summary>
    /// Kullanıcı ayar dosyasına kök yolunu atomik olarak kaydeder (.tmp + Move).
    /// UNC yollarını ve Türkçe karakterleri olduğu gibi korur.
    /// </summary>
    public static void KullaniciKokuKaydet(string kok, string? kullaniciAyarYolu = null)
    {
        Kaydet(kok, kullaniciAyarYolu);
    }

    /// <summary>
    /// Kök yolunu belirtilen ayar dosyasına atomik olarak kaydeder (.tmp + Move).
    /// </summary>
    public static void Kaydet(string kok, string? kullaniciAyarYolu = null)
    {
        if (kok == null)
        {
            throw new ArgumentNullException(nameof(kok));
        }

        string hedefYol = string.IsNullOrWhiteSpace(kullaniciAyarYolu)
            ? VarsayilanKullaniciAyarYolu
            : kullaniciAyarYolu;

        string? klasor = Path.GetDirectoryName(hedefYol);
        if (!string.IsNullOrEmpty(klasor))
        {
            Directory.CreateDirectory(klasor);
        }

        var ayarVerisi = new AyarVerisi { Kok = kok };
        string jsonMetni = JsonSerializer.Serialize(ayarVerisi, JsonAyar);

        string geciciYol = Path.Combine(klasor ?? ".", $"{Path.GetFileName(hedefYol)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(geciciYol, jsonMetni, Encoding.UTF8);
            File.Move(geciciYol, hedefYol, overwrite: true);
        }
        finally
        {
            if (File.Exists(geciciYol))
            {
                try
                {
                    File.Delete(geciciYol);
                }
                catch
                {
                    // Geçici dosya silme hatasını yut
                }
            }
        }
    }

    /// <summary>
    /// Verilen kök klasör yolunun erişilebilir olup olmadığını denetler.
    /// Directory.Exists false veya istisna durumunda false döner.
    /// </summary>
    public static bool KokErisilebilirMi(string? kok)
    {
        if (string.IsNullOrWhiteSpace(kok))
        {
            return false;
        }

        try
        {
            return Directory.Exists(kok);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Uygulama başlatma akışını yürütür:
    /// 1. Coz ile aday kök belirlenir (1. --kok, 2. kullanici.json, 3. ayar.json).
    /// 2. Aday kök yoksa kullanıcıya klasör seçtirilir ve seçim kullanici.json'a kaydedilir.
    /// 3. Aday kök erişilemiyorsa sonraki kaynağa DÜŞMEZ; kullanıcıya uyarı gösterilip değiştirme seçeneği sunulur.
    ///    Kullanıcı kabul ederse yeni klasör seçtirilir, erişilebilirse kullanici.json'a kaydedilir.
    /// </summary>
    public static string? UygulamaKokuBelirle(
        string[]? args,
        Func<string, bool> uyariVeDegistirSor,
        Func<string?> klasorSec,
        string? kullaniciAyarYolu = null,
        string? paketAyarYolu = null,
        Func<string?, bool>? erisilebilirMi = null)
    {
        ArgumentNullException.ThrowIfNull(uyariVeDegistirSor);
        ArgumentNullException.ThrowIfNull(klasorSec);

        erisilebilirMi ??= KokErisilebilirMi;

        string? kok = Coz(args, kullaniciAyarYolu, paketAyarYolu);

        if (string.IsNullOrWhiteSpace(kok))
        {
            string? secilen = klasorSec();
            if (string.IsNullOrWhiteSpace(secilen))
            {
                return null;
            }

            if (erisilebilirMi(secilen))
            {
                Kaydet(secilen, kullaniciAyarYolu);
                return secilen;
            }

            kok = secilen;
        }

        while (!erisilebilirMi(kok))
        {
            bool degistir = uyariVeDegistirSor(kok);
            if (!degistir)
            {
                return null;
            }

            string? yeniSecim = klasorSec();
            if (string.IsNullOrWhiteSpace(yeniSecim))
            {
                return null;
            }

            if (erisilebilirMi(yeniSecim))
            {
                Kaydet(yeniSecim, kullaniciAyarYolu);
                return yeniSecim;
            }

            kok = yeniSecim;
        }

        return kok;
    }

    private class AyarVerisi
    {
        [JsonPropertyName("kok")]
        public string Kok { get; set; } = string.Empty;
    }
}
