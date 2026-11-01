namespace Portal.Servis;

using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Portal.Cekirdek.Yonetim;

/// <summary>
/// Paylaşım klasörü taşıyıcısı (port izni yoksa). Durumu yazar, komutları sırayla işler.
/// Dosya sahibi yalnız denetim kaydı içindir, yetki kanıtı sayılmaz.
/// </summary>
public sealed class DosyaKanali
{
    private const long EnBuyukKomutBayt = 64 * 1024;
    private readonly YonetimCekirdegi _cekirdek;
    private readonly string _klasor;
    private readonly TimeSpan _aralik;

    public DosyaKanali(YonetimCekirdegi cekirdek, string yonetimKlasoru, TimeSpan? aralik = null)
    {
        _cekirdek = cekirdek;
        _klasor = yonetimKlasoru;
        _aralik = aralik ?? TimeSpan.FromSeconds(2);
    }

    public async Task CalistirAsync(CancellationToken durdur)
    {
        while (!durdur.IsCancellationRequested)
        {
            try
            {
                await TurAsync();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Paylaşım geçici erişilemez olabilir; ağ kanalı çalışmaya devam eder.
                Console.Error.WriteLine("Paylaşım hatası: " + e.Message);
            }
            try
            {
                await Task.Delay(_aralik, durdur);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public async Task TurAsync()
    {
        string gecici = Path.Combine(_klasor, "gecici");
        string komutKlasoru = Path.Combine(_klasor, "komut");
        string sonucKlasoru = Path.Combine(_klasor, "sonuc");
        Directory.CreateDirectory(komutKlasoru);
        Directory.CreateDirectory(sonucKlasoru);

        // GUID adı sıra taşımaz: komutlar geliş sırasıyla işlenir.
        foreach (string dosya in new DirectoryInfo(komutKlasoru).GetFiles("*.json").OrderBy(f => f.CreationTimeUtc).Select(f => f.FullName))
        {
            await KomutIsleAsync(dosya, sonucKlasoru, gecici);
        }
        EskileriSil(sonucKlasoru);
        EskileriSil(gecici);
        YonetimDosyalari.AtomikYaz(Path.Combine(_klasor, YonetimDosyalari.DurumDosyasi), gecici, _cekirdek.Durum(), uzerineYaz: true);
    }

    private async Task KomutIsleAsync(string dosya, string sonucKlasoru, string gecici)
    {
        YonetimKomutu? komut = null;
        var bilgi = new FileInfo(dosya);
        if (bilgi.Length <= EnBuyukKomutBayt)
        {
            try
            {
                komut = JsonSerializer.Deserialize<YonetimKomutu>(await File.ReadAllTextAsync(dosya));
            }
            catch (JsonException)
            {
            }
        }
        string ad = Path.GetFileNameWithoutExtension(dosya);
        KomutSonucu sonuc;
        if (komut == null || komut.Id != ad)
        {
            sonuc = new KomutSonucu { Mesaj = "Bozuk komut dosyası." };
        }
        else
        {
            string kimlik = DosyaSahibi(dosya) ?? ("bildirilen:" + komut.Kullanici);
            sonuc = await _cekirdek.IsleAsync(komut, kimlik, "paylasim");
        }
        // Önce sonuç: silme başarısız olursa komut yeniden okunur ama ilk sonuç bozulmaz.
        string sonucYolu = Path.Combine(sonucKlasoru, ad + ".json");
        if (Guid.TryParse(ad, out _) && !File.Exists(sonucYolu))
        {
            YonetimDosyalari.AtomikYaz(sonucYolu, gecici, sonuc, uzerineYaz: true);
        }
        File.Delete(dosya);
    }

    private static string? DosyaSahibi(string dosya)
    {
        try
        {
            return new FileInfo(dosya).GetAccessControl().GetOwner(typeof(NTAccount))?.Value;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or IdentityNotMappedException or SystemException)
        {
            return null;
        }
    }

    private static void EskileriSil(string klasor)
    {
        if (!Directory.Exists(klasor))
        {
            return;
        }
        DateTime sinir = DateTime.UtcNow.AddHours(-1);
        foreach (var dosya in new DirectoryInfo(klasor).EnumerateFiles())
        {
            if (dosya.LastWriteTimeUtc < sinir)
            {
                try
                {
                    dosya.Delete();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
