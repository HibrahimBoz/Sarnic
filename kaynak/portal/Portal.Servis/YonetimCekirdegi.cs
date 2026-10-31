namespace Portal.Servis;

using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Portal.Cekirdek.Yonetim;

/// <summary>Parolanın yerel kaydı. Yalnız türetilmiş anahtar durur; dosya Atölye bilgisayarından çıkmaz.</summary>
public sealed class YonetimParolasi
{
    public string Tuz { get; set; } = "";
    public int Iter { get; set; }
    public string Anahtar { get; set; } = "";

    public static YonetimParolasi Olustur(string parola)
    {
        if (parola.Length < YonetimImza.EnAzParolaUzunlugu)
        {
            throw new ArgumentException($"Parola en az {YonetimImza.EnAzParolaUzunlugu} karakter olmalı.");
        }
        byte[] tuz = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        return new YonetimParolasi
        {
            Tuz = Convert.ToBase64String(tuz),
            Iter = YonetimImza.StandartIterasyon,
            Anahtar = Convert.ToBase64String(YonetimImza.AnahtarTuret(parola, tuz, YonetimImza.StandartIterasyon)),
        };
    }

    public static YonetimParolasi? Oku(string yol)
    {
        return File.Exists(yol) ? JsonSerializer.Deserialize<YonetimParolasi>(File.ReadAllText(yol)) : null;
    }

    public void Yaz(string yol)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(yol)!);
        File.WriteAllText(yol, JsonSerializer.Serialize(this));
    }
}

/// <summary>Taşıyıcıdan bağımsız komut işleme. Tüm yetki denetimi buradadır.</summary>
public sealed class YonetimCekirdegi
{
    private static readonly TimeSpan ZamanPenceresi = TimeSpan.FromMinutes(5);
    private readonly IsKuyrugu _kuyruk;
    private readonly IsIscisi _isci;
    private readonly YonetimParolasi? _parola;
    private readonly string _denetimYolu;
    private readonly TimeSpan _hataGecikmesi;
    private readonly string _diskKoku;
    private readonly ConcurrentDictionary<string, DateTime> _gorulen = new();
    private readonly object _denetimKilidi = new();
    private readonly SemaphoreSlim _parolaSirasi = new(1, 1);
    private const int EnCokGorulen = 100_000;
    private DateTime _sonBudama = DateTime.MinValue;

    public YonetimCekirdegi(IsKuyrugu kuyruk, IsIscisi isci, YonetimParolasi? parola, string denetimYolu, string diskKoku, TimeSpan? hataGecikmesi = null)
    {
        _kuyruk = kuyruk;
        _isci = isci;
        _parola = parola;
        _denetimYolu = denetimYolu;
        _diskKoku = diskKoku;
        _hataGecikmesi = hataGecikmesi ?? TimeSpan.FromSeconds(2);
    }

    /// <summary>Şimdilik herkes yönetici (kullanıcı kararı). Roller gelince yalnız burası değişir.</summary>
    private static bool KullaniciYetkili(string kullanici, string eylem) => true;

    private static bool ParolaGerekli(string eylem) => eylem is Eylemler.Temizle or Eylemler.Dogrula;

    public YonetimDurumu Durum()
    {
        double bos = 0;
        try
        {
            bos = Math.Round(new DriveInfo(Path.GetPathRoot(_diskKoku)!).AvailableFreeSpace / 1073741824.0, 1);
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
        }
        return new YonetimDurumu
        {
            Zaman = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            Makine = Environment.MachineName,
            DiskBosGb = bos,
            Isler = _kuyruk.SonIsler(),
        };
    }

    /// <param name="kimlik">Taşıyıcının kanıtladığı kullanıcı. A'da Windows kimliği, B'de yalnız bildirim.</param>
    public async Task<KomutSonucu> IsleAsync(YonetimKomutu komut, string kimlik, string kanal)
    {
        KomutSonucu sonuc = await IcIsleAsync(komut, kimlik, kanal);
        Denetle(komut, kimlik, kanal, sonuc);
        return sonuc;
    }

    private async Task<KomutSonucu> IcIsleAsync(YonetimKomutu komut, string kimlik, string kanal)
    {
        if (!Guid.TryParse(komut.Id, out _))
        {
            return Hata("Geçersiz komut kimliği.");
        }
        var zaman = DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(komut.Zaman, 0, 253402300799));
        if ((DateTimeOffset.UtcNow - zaman).Duration() > ZamanPenceresi)
        {
            return Hata("Komutun süresi geçmiş. Bilgisayar saatlerini denetleyin.");
        }
        Budama();
        if (_gorulen.Count >= EnCokGorulen)
        {
            return Hata("Servis yoğun. Birazdan yeniden deneyin.");
        }
        if (!_gorulen.TryAdd(komut.Id, DateTime.UtcNow))
        {
            return Hata("Bu komut daha önce işlendi.");
        }
        if (!KullaniciYetkili(kimlik, komut.Eylem))
        {
            return Hata("Bu işlem için yetkiniz yok.");
        }
        if (ParolaGerekli(komut.Eylem))
        {
            if (_parola == null)
            {
                return Hata("Yönetim parolası kurulmamış. Atölye bilgisayarında servis.bat --parola-ayarla komutunu çalıştırın.");
            }
            if (!YonetimImza.Dogrula(komut, Convert.FromBase64String(_parola.Anahtar)))
            {
                await HataGecikmesiAsync(kanal);
                return new KomutSonucu { Mesaj = "Parola yanlış.", YetkiGerekli = true };
            }
        }

        switch (komut.Eylem)
        {
            case Eylemler.Dogrula:
                return Tamam("Parola doğru.");
            case Eylemler.IsEkle:
                return IsEkle(komut, kimlik);
            case Eylemler.Iptal:
                if (komut.IsId is not long iptalId)
                {
                    return Hata("İş seçilmedi.");
                }
                return _kuyruk.BekleyeniIptalEt(iptalId) || _isci.CalisaniIptalEt(iptalId)
                    ? Tamam("İş iptal edildi.")
                    : Hata("İş iptal edilemedi. İş bitmiş olabilir.");
            case Eylemler.YenidenDene:
                if (komut.IsId is not long tekrarId)
                {
                    return Hata("İş seçilmedi.");
                }
                return _kuyruk.YenidenDene(tekrarId) ? Tamam("İş yeniden sıraya girdi.") : Hata("Yalnız hatalı veya iptal edilen iş yeniden denenir.");
            case Eylemler.Temizle:
                return Tamam($"{_kuyruk.Temizle()} kayıt temizlendi. Kaynak ve çıktı dosyaları silinmedi.");
            default:
                return Hata("Bilinmeyen komut.");
        }
    }

    private KomutSonucu IsEkle(YonetimKomutu k, string kimlik)
    {
        if (!IsSabitleri.TurGecerli(k.Tur))
        {
            return Hata("Geçersiz iş türü.");
        }
        if (!YolGecerli(k.Yol))
        {
            return Hata("Yol tam olmalı. Örnek: \\\\sunucu\\Arsiv");
        }
        if (!string.IsNullOrWhiteSpace(k.Cikis) && !YolGecerli(k.Cikis))
        {
            return Hata("Çıktı yolu tam olmalı.");
        }
        long? id = _kuyruk.Ekle(k.Tur!, k.Yol!, string.IsNullOrWhiteSpace(k.Cikis) ? null : k.Cikis, kimlik);
        return id == null
            ? Hata($"Kuyruk dolu: en çok {IsKuyrugu.EnCokBekleyen} bekleyen iş olur.")
            : Tamam($"İş {id} sıraya girdi.");
    }

    public static bool YolGecerli(string? yol)
    {
        if (string.IsNullOrWhiteSpace(yol) || yol.StartsWith('-') || yol.Length > 1024)
        {
            return false;
        }
        try
        {
            return Path.IsPathFullyQualified(yol);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Ağ kanalında denemeler tek sıraya girer; paralel istek gecikmeyi aşamaz.
    /// Paylaşım kanalında gecikme yok: tek döngüyü bloklar ve dosyayı okuyan kişi zaten çevrimdışı deneyebilir.
    /// </summary>
    private async Task HataGecikmesiAsync(string kanal)
    {
        if (kanal == "paylasim")
        {
            return;
        }
        await _parolaSirasi.WaitAsync();
        try
        {
            await Task.Delay(_hataGecikmesi);
        }
        finally
        {
            _parolaSirasi.Release();
        }
    }

    private void Budama()
    {
        if (DateTime.UtcNow - _sonBudama < TimeSpan.FromMinutes(1))
        {
            return;
        }
        _sonBudama = DateTime.UtcNow;
        DateTime sinir = DateTime.UtcNow - ZamanPenceresi - ZamanPenceresi;
        foreach (var (anahtar, gorulme) in _gorulen)
        {
            if (gorulme < sinir)
            {
                _gorulen.TryRemove(anahtar, out _);
            }
        }
    }

    private void Denetle(YonetimKomutu komut, string kimlik, string kanal, KomutSonucu sonuc)
    {
        string satir = JsonSerializer.Serialize(new
        {
            zaman = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            kullanici = kimlik,
            bildirilen = komut.Kullanici,
            kanal,
            eylem = komut.Eylem,
            is_id = komut.IsId,
            yol = komut.Yol,
            basarili = sonuc.Basarili,
            mesaj = sonuc.Mesaj,
        });
        lock (_denetimKilidi)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_denetimYolu)!);
            File.AppendAllText(_denetimYolu, satir + Environment.NewLine);
        }
    }

    private static KomutSonucu Tamam(string mesaj) => new() { Basarili = true, Mesaj = mesaj };
    private static KomutSonucu Hata(string mesaj) => new() { Mesaj = mesaj };
}
