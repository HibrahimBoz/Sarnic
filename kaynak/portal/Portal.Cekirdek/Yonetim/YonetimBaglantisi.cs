namespace Portal.Cekirdek.Yonetim;

using System.Net.Http.Json;
using System.Text.Json;

/// <summary>Atölye servisine iki taşıyıcı için ortak arayüz.</summary>
public interface IYonetimBaglantisi : IDisposable
{
    string Ad { get; }
    Task<YonetimDurumu?> DurumAsync(CancellationToken iptal = default);
    Task<KomutSonucu> GonderAsync(YonetimKomutu komut, CancellationToken iptal = default);
}

/// <summary>A: HTTPS. Sertifika parmak izi servis.json ile eşleşmelidir; kimlik Windows oturumundan gider.</summary>
public sealed class HttpYonetimBaglantisi : IYonetimBaglantisi
{
    private readonly HttpClient _istemci;

    public HttpYonetimBaglantisi(string url, string parmakIzi, HttpMessageHandler? isleyici = null)
    {
        _istemci = new HttpClient(isleyici ?? IsleyiciOlustur(parmakIzi))
        {
            BaseAddress = new Uri(url),
            Timeout = TimeSpan.FromSeconds(15),
        };
    }

    public string Ad => "Ağ servisi";

    private static HttpClientHandler IsleyiciOlustur(string parmakIzi) => new()
    {
        UseDefaultCredentials = true,
        ServerCertificateCustomValidationCallback = (_, sertifika, _, _) =>
            sertifika != null && string.Equals(sertifika.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256), parmakIzi, StringComparison.OrdinalIgnoreCase),
    };

    public Task<YonetimDurumu?> DurumAsync(CancellationToken iptal = default) =>
        _istemci.GetFromJsonAsync<YonetimDurumu>("api/durum", iptal);

    public async Task<KomutSonucu> GonderAsync(YonetimKomutu komut, CancellationToken iptal = default)
    {
        using var cevap = await _istemci.PostAsJsonAsync("api/komut", komut, iptal);
        cevap.EnsureSuccessStatusCode();
        return await cevap.Content.ReadFromJsonAsync<KomutSonucu>(iptal)
            ?? new KomutSonucu { Mesaj = "Servis boş cevap verdi." };
    }

    public void Dispose() => _istemci.Dispose();
}

/// <summary>B: paylaşım klasörü. Port izni yoksa kullanılır; gecikmelidir.</summary>
public sealed class DosyaYonetimBaglantisi : IYonetimBaglantisi
{
    private readonly string _klasor;
    private readonly TimeSpan _zamanAsimi;

    public DosyaYonetimBaglantisi(string yonetimKlasoru, TimeSpan? zamanAsimi = null, string ad = "Paylaşım klasörü")
    {
        _klasor = yonetimKlasoru;
        _zamanAsimi = zamanAsimi ?? TimeSpan.FromSeconds(30);
        Ad = ad;
    }

    public string Ad { get; }

    public async Task<YonetimDurumu?> DurumAsync(CancellationToken iptal = default)
    {
        string yol = Path.Combine(_klasor, YonetimDosyalari.DurumDosyasi);
        if (!File.Exists(yol))
        {
            return null;
        }
        await using var fs = new FileStream(yol, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return await JsonSerializer.DeserializeAsync<YonetimDurumu>(fs, cancellationToken: iptal);
    }

    public async Task<KomutSonucu> GonderAsync(YonetimKomutu komut, CancellationToken iptal = default)
    {
        string hedef = Path.Combine(_klasor, "komut", komut.Id + ".json");
        YonetimDosyalari.AtomikYaz(hedef, Path.Combine(_klasor, "gecici"), komut, uzerineYaz: false);

        string sonucYolu = Path.Combine(_klasor, "sonuc", komut.Id + ".json");
        DateTime son = DateTime.UtcNow + _zamanAsimi;
        while (DateTime.UtcNow < son)
        {
            if (File.Exists(sonucYolu))
            {
                KomutSonucu? sonuc;
                try
                {
                    sonuc = JsonSerializer.Deserialize<KomutSonucu>(await File.ReadAllTextAsync(sonucYolu, iptal));
                }
                catch (IOException)
                {
                    // Paylaşım anlık kilitli olabilir; sonraki turda yeniden okunur.
                    await Task.Delay(500, iptal);
                    continue;
                }
                try
                {
                    File.Delete(sonucYolu);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
                return sonuc ?? new KomutSonucu { Mesaj = "Servis boş cevap verdi." };
            }
            await Task.Delay(500, iptal);
        }
        return new KomutSonucu { Mesaj = "Servis cevap vermedi. Atölye bilgisayarı kapalı olabilir." };
    }

    public void Dispose()
    {
    }
}

/// <summary>Dizin deposundaki yonetim klasörünü okur; önce ağ servisini dener, olmazsa paylaşım klasörüne geçer.</summary>
public static class YonetimBaglayici
{
    public static ServisBilgisi? BilgiOku(string dizinKlasoru)
    {
        string yol = Path.Combine(dizinKlasoru, YonetimDosyalari.KlasorAdi, YonetimDosyalari.ServisDosyasi);
        try
        {
            return File.Exists(yol) ? JsonSerializer.Deserialize<ServisBilgisi>(File.ReadAllText(yol)) : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string VarsayilanGuvenYolu => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sarnic", "yonetim_guven.txt");

    /// <summary>
    /// Paylaşımdaki servis.json herkesçe değiştirilebilir. İlk görülen adres ve parmak izi yerelde saklanır;
    /// sonradan değişirse Windows kimliği o adrese gönderilmez.
    /// Atölye bilgisayarı değiştiyse kullanıcı yonetim_guven.txt dosyasını siler.
    /// </summary>
    public static bool GuvenilirMi(string url, string parmakIzi, string guvenYolu)
    {
        string kayit = url + "|" + parmakIzi;
        try
        {
            if (File.Exists(guvenYolu))
            {
                return string.Equals(File.ReadAllText(guvenYolu).Trim(), kayit, StringComparison.OrdinalIgnoreCase);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(guvenYolu)!);
            File.WriteAllText(guvenYolu, kayit);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static async Task<IYonetimBaglantisi?> BaglanAsync(string dizinKlasoru, CancellationToken iptal = default, string? guvenYolu = null)
    {
        ServisBilgisi? bilgi = BilgiOku(dizinKlasoru);
        if (bilgi == null)
        {
            return null;
        }
        string yonetimKlasoru = Path.Combine(dizinKlasoru, YonetimDosyalari.KlasorAdi);
        if (!string.IsNullOrEmpty(bilgi.Url) && !string.IsNullOrEmpty(bilgi.ParmakIzi))
        {
            if (!GuvenilirMi(bilgi.Url, bilgi.ParmakIzi, guvenYolu ?? VarsayilanGuvenYolu))
            {
                return new DosyaYonetimBaglantisi(yonetimKlasoru, ad: "Paylaşım klasörü (UYARI: servis adresi veya sertifikası değişti)");
            }
            var http = new HttpYonetimBaglantisi(bilgi.Url, bilgi.ParmakIzi);
            try
            {
                using var kisa = CancellationTokenSource.CreateLinkedTokenSource(iptal);
                kisa.CancelAfter(TimeSpan.FromSeconds(3));
                await http.DurumAsync(kisa.Token);
                return http;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
            {
                http.Dispose();
            }
        }
        return new DosyaYonetimBaglantisi(yonetimKlasoru);
    }
}
