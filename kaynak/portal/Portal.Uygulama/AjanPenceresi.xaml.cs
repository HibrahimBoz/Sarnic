namespace Portal.Uygulama;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Portal.Cekirdek;
using Portal.Cekirdek.Yonetim;

/// <summary>Kullanıcı soruyu yazar, servis sıraya alır, cevap kaynaklarıyla gelir.</summary>
public partial class AjanPenceresi : Window
{
    private readonly string _dizinKlasoru;
    private readonly DispatcherTimer _zamanlayici = new() { Interval = TimeSpan.FromSeconds(3) };
    private IYonetimBaglantisi? _baglanti;
    private bool _yenileniyor;
    private bool _kapandi;
    private DateTime _sonSoru = DateTime.MinValue;

    private sealed record KaynakSatiri(AjanKaynagi Kaynak)
    {
        public override string ToString() => $"[{Kaynak.No}] {Kaynak.Ad} — {Kaynak.Etiket}";
    }

    private sealed record SoruSatiri(SoruKaydi Kayit)
    {
        public string Soru => Kayit.Soru;
        public string DurumMetni => Kayit.Durum switch
        {
            IsSabitleri.Bekliyor => $"Sırada: {Kayit.Sira}. soru",
            IsSabitleri.Calisiyor => "Ajan çalışıyor",
            IsSabitleri.Bitti => "Bitti",
            IsSabitleri.Hata => "Hata",
            IsSabitleri.Iptal => "İptal",
            _ => Kayit.Durum,
        };
    }

    public AjanPenceresi(string dizinKlasoru)
    {
        InitializeComponent();
        _dizinKlasoru = dizinKlasoru;
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
            DurumMetni.Text = "Atölye servisi bulunamadı. Ajan için Atölye bilgisayarı açık olmalıdır. Normal arama çalışmaya devam eder.";
            SorDugmesi.IsEnabled = false;
            return;
        }
        DurumMetni.Text = $"Bağlantı: {_baglanti.Ad}. Ajan yalnız dizindeki belgelerde arar. Cevabı kaynaktan doğrulayın.";
        await YenileAsync();
    }

    private static YonetimKomutu YeniKomut(string eylem) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Zaman = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        Eylem = eylem,
        Kullanici = Kimlik.Varsayilan.KullaniciAdi(),
    };

    /// <summary>Açık soru varsa yoklar. Açık soru yoksa servise istek gitmez.</summary>
    private async Task YenileAsync()
    {
        if (_baglanti == null || _yenileniyor)
        {
            return;
        }
        _yenileniyor = true;
        try
        {
            KomutSonucu sonuc = await _baglanti.GonderAsync(YeniKomut(Eylemler.SoruListele));
            if (_kapandi || sonuc.Sorular == null)
            {
                return;
            }
            long? secili = (SoruListesi.SelectedItem as SoruSatiri)?.Kayit.Id;
            var satirlar = sonuc.Sorular.Select(s => new SoruSatiri(s)).ToList();
            SoruListesi.ItemsSource = satirlar;
            SoruListesi.SelectedItem = satirlar.FirstOrDefault(s => s.Kayit.Id == secili) ?? satirlar.FirstOrDefault();
            bool acik = sonuc.Sorular.Any(s => s.Durum is IsSabitleri.Bekliyor or IsSabitleri.Calisiyor);
            // Yeni soru gönderilirken yola çıkmış eski liste yoklamayı durdurmasın.
            if (acik || DateTime.UtcNow - _sonSoru < TimeSpan.FromSeconds(10))
            {
                _zamanlayici.Start();
            }
            else
            {
                _zamanlayici.Stop();
            }
        }
        catch (ObjectDisposedException)
        {
            // Pencere istek sürerken kapandı.
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException or IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            SonucMetni.Text = "Atölye servisine ulaşılamadı: " + e.Message;
        }
        finally
        {
            _yenileniyor = false;
        }
    }

    private async void Sor_Click(object sender, RoutedEventArgs e) => await SorAsync();

    private async void SoruKutusu_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await SorAsync();
        }
    }

    private async Task SorAsync()
    {
        string soru = SoruKutusu.Text.Trim();
        if (_baglanti == null || soru.Length == 0)
        {
            return;
        }
        var komut = YeniKomut(Eylemler.SoruSor);
        komut.Soru = soru;
        SorDugmesi.IsEnabled = false;
        try
        {
            KomutSonucu sonuc = await _baglanti.GonderAsync(komut);
            if (_kapandi)
            {
                return;
            }
            SonucMetni.Text = sonuc.Mesaj;
            if (sonuc.Basarili)
            {
                SoruKutusu.Clear();
                SoruListesi.SelectedItem = null;
                _sonSoru = DateTime.UtcNow;
                _zamanlayici.Start();
                await YenileAsync();
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            SonucMetni.Text = "Soru gönderilemedi: " + ex.Message;
        }
        finally
        {
            if (!_kapandi)
            {
                SorDugmesi.IsEnabled = true;
            }
        }
    }

    private void SoruListesi_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var satir = SoruListesi.SelectedItem as SoruSatiri;
        CevapKutusu.Text = satir?.Kayit.Cevap ?? (satir == null ? string.Empty : satir.DurumMetni);
        KaynakListesi.ItemsSource = satir?.Kayit.Kaynaklar.Select(k => new KaynakSatiri(k)).ToList();
        IptalDugmesi.IsEnabled = satir?.Kayit.Durum is IsSabitleri.Bekliyor or IsSabitleri.Calisiyor;
    }

    private async void Iptal_Click(object sender, RoutedEventArgs e)
    {
        if (_baglanti == null || SoruListesi.SelectedItem is not SoruSatiri satir)
        {
            return;
        }
        var komut = YeniKomut(Eylemler.SoruIptal);
        komut.IsId = satir.Kayit.Id;
        try
        {
            KomutSonucu sonuc = await _baglanti.GonderAsync(komut);
            if (!_kapandi)
            {
                SonucMetni.Text = sonuc.Mesaj;
                await YenileAsync();
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            SonucMetni.Text = "İptal gönderilemedi: " + ex.Message;
        }
    }

    /// <summary>Dosyayı çalıştırmaz, yalnız klasörde seçili gösterir.</summary>
    private void KaynakListesi_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (KaynakListesi.SelectedItem is KaynakSatiri satir && File.Exists(satir.Kaynak.Yol))
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select," + satir.Kaynak.Yol }, UseShellExecute = false });
        }
    }
}
