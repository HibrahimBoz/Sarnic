namespace Portal.Uygulama;

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Portal.Cekirdek;
using Portal.Cekirdek.Yonetim;

/// <summary>Atölye kuyruğunu izler ve yönetir. Tüm yetki denetimi serviste yapılır.</summary>
public partial class YonetimPenceresi : Window
{
    /// <summary>Parola bir kez sorulur; anahtar Sarnıç kapanana kadar bellekte kalır (kullanıcı kararı).</summary>
    private static byte[]? _yoneticiAnahtari;

    private readonly string _dizinKlasoru;
    private readonly DispatcherTimer _zamanlayici = new() { Interval = TimeSpan.FromSeconds(2) };
    private IYonetimBaglantisi? _baglanti;
    private bool _yenileniyor;
    private bool _kapandi;

    private sealed record Tur(string Kod, string Ad)
    {
        public override string ToString() => Ad;
    }

    private sealed record IsSatiri(long Id, string Kod, string TurMetni, string Durum, string DurumMetni, string Yol, string Ekleyen, string? SonSatir);

    public YonetimPenceresi(string dizinKlasoru)
    {
        InitializeComponent();
        _dizinKlasoru = dizinKlasoru;
        TurKutusu.ItemsSource = new[]
        {
            new Tur(IsSabitleri.TurIsle, "Video işle"),
            new Tur(IsSabitleri.TurBelge, "Belge işle"),
            new Tur(IsSabitleri.TurDizinle, "Dizinle"),
        };
        Loaded += async (_, _) => await BaglanAsync();
        Closed += (_, _) =>
        {
            _kapandi = true;
            _zamanlayici.Stop();
            _baglanti?.Dispose();
        };
        _zamanlayici.Tick += async (_, _) => await YenileAsync();
    }

    private async Task BaglanAsync()
    {
        IYonetimBaglantisi? baglanti = await YonetimBaglayici.BaglanAsync(_dizinKlasoru);
        if (_kapandi)
        {
            baglanti?.Dispose();
            return;
        }
        _baglanti = baglanti;
        if (_baglanti == null)
        {
            DurumMetni.Text = $"Atölye servisi bulunamadı. Beklenen dosya: {_dizinKlasoru}\\{YonetimDosyalari.KlasorAdi}\\{YonetimDosyalari.ServisDosyasi}";
            return;
        }
        await YenileAsync();
        _zamanlayici.Start();
    }

    private async Task YenileAsync()
    {
        if (_baglanti == null || _yenileniyor)
        {
            return;
        }
        _yenileniyor = true;
        try
        {
            YonetimDurumu? durum = await _baglanti.DurumAsync();
            if (_kapandi)
            {
                return;
            }
            if (durum == null)
            {
                DurumMetni.Text = "Servis henüz durum yazmadı.";
                return;
            }
            int bekleyen = durum.Isler.Count(i => i.Durum == IsSabitleri.Bekliyor);
            int calisan = durum.Isler.Count(i => i.Durum == IsSabitleri.Calisiyor);
            string gecikme = _baglanti is DosyaYonetimBaglantisi && SaatEski(durum.Zaman)
                ? " — UYARI: durum 30 saniyeden eski, servis kapalı olabilir"
                : string.Empty;
            DurumMetni.Text = $"{durum.Makine} — bağlantı: {_baglanti.Ad} — çalışan: {calisan}, bekleyen: {bekleyen} — boş disk: {durum.DiskBosGb} GB{gecikme}";

            long? secili = (IsTablosu.SelectedItem as IsSatiri)?.Id;
            var satirlar = durum.Isler.Select(Satir).ToList();
            IsTablosu.ItemsSource = satirlar;
            if (secili != null)
            {
                IsTablosu.SelectedItem = satirlar.FirstOrDefault(s => s.Id == secili);
            }
        }
        catch (ObjectDisposedException)
        {
            // Pencere istek sürerken kapandı.
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException or System.IO.IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            DurumMetni.Text = "Atölye servisine ulaşılamadı: " + e.Message;
        }
        finally
        {
            _yenileniyor = false;
        }
    }

    private static bool SaatEski(string zaman) =>
        DateTimeOffset.TryParse(zaman, out var z) && DateTimeOffset.UtcNow - z > TimeSpan.FromSeconds(30);

    private static IsSatiri Satir(IsKaydi k) => new(k.Id, k.Tur, k.Tur switch
    {
        IsSabitleri.TurIsle => "Video",
        IsSabitleri.TurBelge => "Belge",
        IsSabitleri.TurDizinle => "Dizinleme",
        _ => k.Tur,
    }, k.Durum, k.Durum switch
    {
        IsSabitleri.Bekliyor => "Bekliyor",
        IsSabitleri.Calisiyor => "Çalışıyor",
        IsSabitleri.Bitti => "Bitti",
        IsSabitleri.Hata => "Hata",
        IsSabitleri.Iptal => "İptal",
        _ => k.Durum,
    }, k.Yol, k.Ekleyen, k.SonSatir);

    private static YonetimKomutu YeniKomut(string eylem) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Zaman = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        Eylem = eylem,
        Kullanici = Kimlik.Varsayilan.KullaniciAdi(),
    };

    private async Task<KomutSonucu?> GonderAsync(YonetimKomutu komut)
    {
        if (_baglanti == null)
        {
            SonucMetni.Text = "Servis bağlantısı yok.";
            return null;
        }
        try
        {
            KomutSonucu sonuc = await _baglanti.GonderAsync(komut);
            if (_kapandi)
            {
                return sonuc;
            }
            SonucMetni.Text = sonuc.Mesaj;
            await YenileAsync();
            return sonuc;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException or System.IO.IOException or UnauthorizedAccessException)
        {
            SonucMetni.Text = "Komut gönderilemedi: " + e.Message;
            return null;
        }
    }

    private async void Ekle_Click(object sender, RoutedEventArgs e)
    {
        var komut = YeniKomut(Eylemler.IsEkle);
        komut.Tur = (TurKutusu.SelectedItem as Tur)?.Kod;
        komut.Yol = YolKutusu.Text.Trim();
        komut.Cikis = CikisKutusu.IsEnabled ? CikisKutusu.Text.Trim() : null;
        KomutSonucu? sonuc = await GonderAsync(komut);
        if (sonuc?.Basarili == true)
        {
            YolKutusu.Clear();
        }
    }

    private void TurKutusu_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Dizinleme servisin deposuna yazar, çıktı klasörü almaz.
        CikisKutusu.IsEnabled = (TurKutusu.SelectedItem as Tur)?.Kod != IsSabitleri.TurDizinle;
    }

    private void IsTablosu_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var satir = IsTablosu.SelectedItem as IsSatiri;
        IptalDugmesi.IsEnabled = satir?.Durum is IsSabitleri.Bekliyor or IsSabitleri.Calisiyor;
        TekrarDugmesi.IsEnabled = satir?.Durum is IsSabitleri.Hata or IsSabitleri.Iptal;
    }

    private async void Iptal_Click(object sender, RoutedEventArgs e)
    {
        if (IsTablosu.SelectedItem is IsSatiri satir)
        {
            var komut = YeniKomut(Eylemler.Iptal);
            komut.IsId = satir.Id;
            await GonderAsync(komut);
        }
    }

    private async void Tekrar_Click(object sender, RoutedEventArgs e)
    {
        if (IsTablosu.SelectedItem is IsSatiri satir)
        {
            var komut = YeniKomut(Eylemler.YenidenDene);
            komut.IsId = satir.Id;
            await GonderAsync(komut);
        }
    }

    private async void Temizle_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Bekleyen ve biten kayıtlar kuyruktan silinir. Çalışan iş devam eder. Kaynak ve çıktı dosyaları silinmez.\n\nDevam edilsin mi?",
                "Kuyruğu temizle", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }
        if (_yoneticiAnahtari == null && !await ParolaAlAsync())
        {
            return;
        }
        var komut = YeniKomut(Eylemler.Temizle);
        komut.Imza = YonetimImza.Imzala(komut, _yoneticiAnahtari!);
        KomutSonucu? sonuc = await GonderAsync(komut);
        if (sonuc?.YetkiGerekli == true)
        {
            _yoneticiAnahtari = null;
        }
    }

    /// <summary>Parolayı serviste doğrular. Parola ağa ve dosyaya yazılmaz; yalnız imza gider.</summary>
    private async Task<bool> ParolaAlAsync()
    {
        ServisBilgisi? bilgi = YonetimBaglayici.BilgiOku(_dizinKlasoru);
        if (bilgi == null || string.IsNullOrEmpty(bilgi.Tuz) || bilgi.Iter <= 0)
        {
            SonucMetni.Text = "Yönetim parolası kurulmamış. Atölye bilgisayarında servis.bat --parola-ayarla komutunu çalıştırın.";
            return false;
        }
        var pencere = new YonetimParolaPenceresi { Owner = this };
        if (pencere.ShowDialog() != true)
        {
            return false;
        }
        byte[] tuz = Convert.FromBase64String(bilgi.Tuz);
        string parola = pencere.Parola;
        int iter = bilgi.Iter;
        byte[] anahtar = await Task.Run(() => YonetimImza.AnahtarTuret(parola, tuz, iter));
        var komut = YeniKomut(Eylemler.Dogrula);
        komut.Imza = YonetimImza.Imzala(komut, anahtar);
        KomutSonucu? sonuc = await GonderAsync(komut);
        if (sonuc?.Basarili != true)
        {
            return false;
        }
        _yoneticiAnahtari = anahtar;
        return true;
    }
}
