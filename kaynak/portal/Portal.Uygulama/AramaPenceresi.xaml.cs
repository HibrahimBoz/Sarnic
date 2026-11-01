namespace Portal.Uygulama;

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Portal.Cekirdek;
using Portal.Cekirdek.Dizin;

/// <summary>
/// Sarnıç Arama penceresi: Video, dosya ve belge aramalarını birleşik sunar.
/// </summary>
public partial class AramaPenceresi : Window
{
    private string _kokKlasor;
    private IAramaMotoru _aramaMotoru;
    private DizinKaynagi? _dizinKaynagi;
    private readonly ArtCraftKatalogu _artCraftKatalogu = new();
    private readonly DispatcherTimer _dizinZamanlayici;
    private readonly DispatcherTimer _aramaZamanlayici;

    private CancellationTokenSource? _aramaCts;
    private CancellationTokenSource? _indekslemeCts;
    private Task? _indekslemeGorevi;

    private string _sonAramaSorgusu = string.Empty;
    private Siralama _dosyaSiralama = new(SiralamaAlani.Ilgi, false);

    private readonly ObservableCollection<DersKaydi> _dersler = [];
    private readonly ObservableCollection<SonucGorunumu> _videolar = [];
    private readonly ObservableCollection<DosyaGorunumu> _dosyalar = [];
    private readonly ObservableCollection<BelgeSonucGorunumu> _belgeler = [];
    private readonly ListCollectionView _belgelerGorunumu;

    private readonly ObservableCollection<DosyaGorunumu> _tumuDosyalar = [];
    private readonly ObservableCollection<BelgeSonucGorunumu> _tumuBelgeler = [];
    private readonly ObservableCollection<SonucGorunumu> _tumuVideolar = [];

    /// <summary>
    /// Geçerli kök klasör yolu.
    /// </summary>
    public string KokKlasor => _kokKlasor;

    public AramaPenceresi()
        : this(Directory.GetCurrentDirectory())
    {
    }

    public AramaPenceresi(string kokKlasor)
    {
        InitializeComponent();
        Title = $"{UrunAdi.Etkin} — Arama";
        _kokKlasor = string.IsNullOrWhiteSpace(kokKlasor) ? Directory.GetCurrentDirectory() : kokKlasor;
        _aramaMotoru = new MetinAramaMotoru(new IndeksOnbellek(), new DuzeltmeDeposu(_kokKlasor));

        DersListesi.ItemsSource = _dersler;
        SonucListesi.ItemsSource = _videolar;
        DosyaListesi.ItemsSource = _dosyalar;

        _belgelerGorunumu = (ListCollectionView)CollectionViewSource.GetDefaultView(_belgeler);
        _belgelerGorunumu.GroupDescriptions.Add(new PropertyGroupDescription(nameof(BelgeSonucGorunumu.DosyaId)));
        BelgeListesi.ItemsSource = _belgelerGorunumu;

        TumuDosyalarListesi.ItemsSource = _tumuDosyalar;
        TumuBelgelerListesi.ItemsSource = _tumuBelgeler;
        TumuVideolarListesi.ItemsSource = _tumuVideolar;

        DurumKullanici.Text = $"Kullanıcı: {Kimlik.Varsayilan.KullaniciAdi()}";
        DurumKok.Text = $"Kök: {_kokKlasor}";
        DurumBilgisi.Text = "Hazır";

        _aramaZamanlayici = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _aramaZamanlayici.Tick += AramaZamanlayici_Tick;

        DizinKaynagiKur();
        _ = Task.Run(() => _ = _artCraftKatalogu.Uygulamalar);

        _dizinZamanlayici = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(60)
        };
        _dizinZamanlayici.Tick += async (_, _) =>
        {
            if (_dizinKaynagi != null)
            {
                try
                {
                    bool degisti = await _dizinKaynagi.YenileAsync();
                    DizinDurumunuGuncelle();
                    if (degisti && !string.IsNullOrWhiteSpace(_sonAramaSorgusu))
                    {
                        await AramayiCalistirAsync();
                    }
                }
                catch
                {
                    // Hataları yut
                }
            }
        };
        _dizinZamanlayici.Start();
    }

    private void DizinKaynagiKur()
    {
        try
        {
            _dizinKaynagi?.Dispose();
            string dizinYolu = DizinAyari.Coz(_kokKlasor);
            _dizinKaynagi = new DizinKaynagi(dizinYolu);
            _ = Task.Run(async () =>
            {
                try
                {
                    bool degisti = await _dizinKaynagi.YenileAsync();
                    Dispatcher.Invoke(() =>
                    {
                        DizinDurumunuGuncelle();
                        if (degisti && !string.IsNullOrWhiteSpace(_sonAramaSorgusu))
                        {
                            _ = AramayiCalistirAsync();
                        }
                    });
                }
                catch
                {
                    // Yut
                }
            });
        }
        catch
        {
            // Kaynak oluşturma hatasını yut
        }
        DizinDurumunuGuncelle();
    }

    private void DizinDurumunuGuncelle()
    {
        if (_dizinKaynagi == null)
        {
            DurumDizin.Text = "Dizin: Yok";
            DurumDizin.Foreground = (Brush)FindResource("SolukMetinFircasi");
            return;
        }

        if (_dizinKaynagi.Durum == DizinDurumu.Hazir)
        {
            string yayin = _dizinKaynagi.YayinZamani.HasValue
                ? _dizinKaynagi.YayinZamani.Value.ToString("dd.MM.yyyy HH:mm")
                : "-";
            DurumDizin.Text = $"Dizin: {_dizinKaynagi.Ileti} — {yayin}";
            DurumDizin.Foreground = (Brush)FindResource("SolukMetinFircasi");
        }
        else
        {
            DurumDizin.Text = string.IsNullOrWhiteSpace(_dizinKaynagi.Ileti)
                ? "Dizin hazır değil"
                : _dizinKaynagi.Ileti;
            DurumDizin.Foreground = (Brush)FindResource("HataFircasi");
        }
    }

    private async void Pencere_Loaded(object sender, RoutedEventArgs e)
    {
        await IndeksleAsync();
    }

    private async Task IndeksleAsync()
    {
        if (_indekslemeCts != null)
        {
            _indekslemeCts.Cancel();
            if (_indekslemeGorevi != null)
            {
                try
                {
                    await _indekslemeGorevi;
                }
                catch
                {
                    // İptal hatasını yut
                }
            }
            _indekslemeCts.Dispose();
            _indekslemeCts = null;
        }

        var cts = new CancellationTokenSource();
        _indekslemeCts = cts;
        var iptal = cts.Token;

        IlerlemeCubugu.Visibility = Visibility.Visible;
        IlerlemeCubugu.IsIndeterminate = false;
        IlerlemeCubugu.Value = 0;
        DurumBilgisi.Text = "Dersler taranıyor...";

        try
        {
            var bulunanlar = await Task.Run(() => Tarayici.Tara(_kokKlasor), iptal);
            iptal.ThrowIfCancellationRequested();

            _dersler.Clear();
            foreach (var ders in bulunanlar)
            {
                _dersler.Add(ders);
            }

            DersListesiBaslik.Text = $"Dersler ({_dersler.Count})";
            DurumDersSayisi.Text = $"Ders: {_dersler.Count}";

            if (_dersler.Count == 0)
            {
                DurumBilgisi.Text = "Kök klasörde ders kaydı bulunamadı.";
                IlerlemeCubugu.Visibility = Visibility.Collapsed;
                return;
            }

            DurumBilgisi.Text = "İndeksleniyor...";
            var ilerleme = new Progress<int>(yuzde =>
            {
                if (iptal.IsCancellationRequested) return;

                Dispatcher.Invoke(() =>
                {
                    IlerlemeCubugu.Value = yuzde;
                    DurumBilgisi.Text = $"İndeksleniyor... %{yuzde}";
                });
            });

            var dersSnapshot = _dersler.ToList();
            var motor = _aramaMotoru;
            _indekslemeGorevi = Task.Run(async () => await motor.IndeksleAsync(dersSnapshot, ilerleme, iptal), iptal);
            await _indekslemeGorevi;

            iptal.ThrowIfCancellationRequested();

            DurumBilgisi.Text = motor.AtlananDersSayisi > 0
                ? $"{motor.AtlananDersSayisi} ders atlandı"
                : "Hazır";
        }
        catch (OperationCanceledException)
        {
            // İptal edildiğinde durumu bozma
        }
        catch (Exception ex)
        {
            if (!iptal.IsCancellationRequested)
            {
                DurumBilgisi.Text = "İndeksleme hatası: " + ex.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(_indekslemeCts, cts))
            {
                IlerlemeCubugu.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void AjanButonu_Click(object sender, RoutedEventArgs e)
    {
        string depo = _dizinKaynagi?.DizinKlasoru ?? DizinAyari.Coz(_kokKlasor);
        new AjanPenceresi(depo) { Owner = this }.Show();
    }

    private void YonetimButonu_Click(object sender, RoutedEventArgs e)
    {
        string depo = _dizinKaynagi?.DizinKlasoru ?? DizinAyari.Coz(_kokKlasor);
        new YonetimPenceresi(depo) { Owner = this }.Show();
    }

    private async void KokDegistirButonu_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Ders videoları kök klasörünü seçin",
            Multiselect = false,
            InitialDirectory = Directory.Exists(_kokKlasor) ? _kokKlasor : string.Empty
        };

        if (dialog.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            await KokDegistirAsync(dialog.FolderName);
        }
    }

    /// <summary>
    /// Kök klasörü günceller, ayarı kaydeder ve arama indeksini yeniden oluşturur.
    /// </summary>
    public async Task KokDegistirAsync(string yeniKok)
    {
        if (string.IsNullOrWhiteSpace(yeniKok) || !KokYapilandirma.KokErisilebilirMi(yeniKok))
        {
            MessageBox.Show(
                this,
                $"Kök klasöre erişilemiyor: {yeniKok}",
                "Kök Klasör Hatası",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        try
        {
            KokYapilandirma.Kaydet(yeniKok);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Ayar kaydedilemedi:\n{ex.Message}",
                "Hata",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            return;
        }

        if (_indekslemeCts != null)
        {
            _indekslemeCts.Cancel();
            if (_indekslemeGorevi != null)
            {
                try
                {
                    await _indekslemeGorevi;
                }
                catch
                {
                    // İptal hatasını yut
                }
            }
            _indekslemeCts.Dispose();
            _indekslemeCts = null;
        }

        _kokKlasor = yeniKok;
        DurumKok.Text = $"Kök: {_kokKlasor}";

        if (_aramaMotoru is IDisposable disp)
        {
            disp.Dispose();
        }
        _aramaMotoru = new MetinAramaMotoru(new IndeksOnbellek(), new DuzeltmeDeposu(_kokKlasor));

        DizinKaynagiKur();
        _ = Task.Run(() => _ = _artCraftKatalogu.Uygulamalar);

        AramaKutusu.Text = string.Empty;
        _videolar.Clear();
        _dosyalar.Clear();
        _belgeler.Clear();
        _tumuVideolar.Clear();
        _tumuDosyalar.Clear();
        _tumuBelgeler.Clear();
        SekmeSayilariniGuncelle();
        SonucSayisiMetni.Text = "Arama Sonuçları";

        await IndeksleAsync();
    }

    private void AramaKutusu_TextChanged(object sender, TextChangedEventArgs e)
    {
        _aramaZamanlayici.Stop();
        _aramaZamanlayici.Start();
    }

    private async void AramaZamanlayici_Tick(object? sender, EventArgs e)
    {
        _aramaZamanlayici.Stop();
        await AramayiCalistirAsync();
    }

    private async Task AramayiCalistirAsync()
    {
        if (_aramaCts != null)
        {
            _aramaCts.Cancel();
            _aramaCts.Dispose();
            _aramaCts = null;
        }

        string sorgu = AramaKutusu.Text?.Trim() ?? string.Empty;
        _sonAramaSorgusu = sorgu;

        if (string.IsNullOrWhiteSpace(sorgu))
        {
            _videolar.Clear();
            _dosyalar.Clear();
            _belgeler.Clear();
            _tumuVideolar.Clear();
            _tumuDosyalar.Clear();
            _tumuBelgeler.Clear();
            SekmeSayilariniGuncelle();
            SonucSayisiMetni.Text = "Arama Sonuçları";
            if (_indekslemeCts == null)
            {
                IlerlemeCubugu.Visibility = Visibility.Collapsed;
                IlerlemeCubugu.IsIndeterminate = false;
            }
            return;
        }

        var cts = new CancellationTokenSource();
        _aramaCts = cts;
        var iptal = cts.Token;

        IlerlemeCubugu.Visibility = Visibility.Visible;
        IlerlemeCubugu.IsIndeterminate = true;

        try
        {
            var birlesik = new BirlesikArama(_aramaMotoru, _dizinKaynagi);
            var sonuclar = await birlesik.AraAsync(sorgu, turBasinaEnFazla: 1000, iptal: iptal, dosyaSiralama: _dosyaSiralama);
            iptal.ThrowIfCancellationRequested();

            _videolar.Clear();
            foreach (var v in sonuclar.Videolar)
            {
                _videolar.Add(new SonucGorunumu(v));
            }

            _dosyalar.Clear();
            foreach (var d in sonuclar.Dosyalar)
            {
                _dosyalar.Add(new DosyaGorunumu(d));
            }

            _belgeler.Clear();
            foreach (var b in sonuclar.Belgeler)
            {
                _belgeler.Add(new BelgeSonucGorunumu(b));
            }

            _tumuVideolar.Clear();
            foreach (var v in _videolar.Take(10)) _tumuVideolar.Add(v);

            _tumuDosyalar.Clear();
            foreach (var d in _dosyalar.Take(10)) _tumuDosyalar.Add(d);

            _tumuBelgeler.Clear();
            foreach (var b in _belgeler.Take(10)) _tumuBelgeler.Add(b);

            SekmeSayilariniGuncelle();
            SonucSayisiMetni.Text = $"Arama Sonuçları ({_videolar.Count})";
        }
        catch (OperationCanceledException)
        {
            // İptal hatasını yut
        }
        catch (Exception ex)
        {
            if (!iptal.IsCancellationRequested)
            {
                DurumBilgisi.Text = "Arama hatası: " + ex.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(_aramaCts, cts) || _aramaCts == null)
            {
                if (_indekslemeCts == null)
                {
                    IlerlemeCubugu.Visibility = Visibility.Collapsed;
                    IlerlemeCubugu.IsIndeterminate = false;
                }
            }
        }
    }

    private void SekmeSayilariniGuncelle()
    {
        int vSayisi = _videolar.Count;
        int dSayisi = _dosyalar.Count;
        int bSayisi = _belgeler.Count;
        int tSayisi = vSayisi + dSayisi + bSayisi;

        SekmeTumuBaslik.Text = $"Tümü ({tSayisi})";
        SekmeDosyalarBaslik.Text = $"Dosyalar ({dSayisi})";
        SekmeBelgelerBaslik.Text = $"Belgeler ({bSayisi})";
        SekmeVideolarBaslik.Text = $"Videolar ({vSayisi})";

        TumuDosyalarDugmesi.Content = $"Tümünü göster ({dSayisi})";
        TumuBelgelerDugmesi.Content = $"Tümünü göster ({bSayisi})";
        TumuVideolarDugmesi.Content = $"Tümünü göster ({vSayisi})";
    }

    private void TumuDosyalarDugmesi_Click(object sender, RoutedEventArgs e)
    {
        AnaSekmeler.SelectedItem = TabDosyalar;
    }

    private void TumuBelgelerDugmesi_Click(object sender, RoutedEventArgs e)
    {
        AnaSekmeler.SelectedItem = TabBelgeler;
    }

    private void TumuVideolarDugmesi_Click(object sender, RoutedEventArgs e)
    {
        AnaSekmeler.SelectedItem = TabVideolar;
    }

    private void TumuDosyaOgesi_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is DosyaGorunumu dg)
        {
            DosyaAc(dg.Yol);
        }
    }

    private void TumuBelgeOgesi_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is BelgeSonucGorunumu bg)
        {
            BelgeyiAc(bg);
        }
    }

    private void TumuVideoOgesi_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is SonucGorunumu sg)
        {
            SonucuAc(sg);
        }
    }

    private async void DosyaSutunBaslik_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header || header.Column == null)
            return;

        string? baslik = header.Column.Header?.ToString();
        if (string.IsNullOrWhiteSpace(baslik)) return;

        SiralamaAlani yeniAlan = baslik switch
        {
            "Ad" => SiralamaAlani.Ad,
            "Klasör" => SiralamaAlani.Yol,
            "Boyut" => SiralamaAlani.Boyut,
            "Değişme" => SiralamaAlani.Tarih,
            _ => SiralamaAlani.Ilgi
        };

        if (_dosyaSiralama.Alan == yeniAlan)
        {
            _dosyaSiralama = new Siralama(yeniAlan, !_dosyaSiralama.Azalan);
        }
        else
        {
            _dosyaSiralama = new Siralama(yeniAlan, false);
        }

        if (_dizinKaynagi == null || _dizinKaynagi.Durum != DizinDurumu.Hazir)
            return;

        await AramayiCalistirAsync();
    }

    private void DosyaListesi_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DosyaListesi.SelectedItem is DosyaGorunumu dg)
        {
            DosyaAc(dg.Yol);
        }
    }

    private void DosyaListesi_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DosyaListesi.SelectedItem is DosyaGorunumu dg)
        {
            DosyaAc(dg.Yol);
            e.Handled = true;
        }
        else if (e.Key == Key.Up && DosyaListesi.SelectedIndex <= 0)
        {
            AramaKutusu.Focus();
            e.Handled = true;
        }
    }

    private void DosyaListesi_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep)
        {
            var item = ItemsControl.ContainerFromElement(DosyaListesi, dep) as ListViewItem;
            if (item != null)
            {
                item.IsSelected = true;
                item.Focus();
            }
        }
    }

    private void BelgeListesi_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep)
        {
            var item = ItemsControl.ContainerFromElement(BelgeListesi, dep) as ListViewItem;
            if (item != null)
            {
                item.IsSelected = true;
                item.Focus();
            }
        }
    }

    private void DosyaContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
        {
            return;
        }

        MenuItem? duzenleMenu = null;
        MenuItem? kopyaMenu = null;
        MenuItem? belgeMenu = null;

        foreach (var item in menu.Items)
        {
            if (item is MenuItem mi)
            {
                if (string.Equals(mi.Name, "DosyaMenuDuzenle", StringComparison.Ordinal) ||
                    string.Equals(mi.Header?.ToString(), "Düzenle", StringComparison.OrdinalIgnoreCase))
                {
                    duzenleMenu = mi;
                }
                else if (string.Equals(mi.Name, "DosyaMenuDuzenlenebilirKopya", StringComparison.Ordinal) ||
                         mi.Header?.ToString()?.StartsWith("Düzenlenebilir", StringComparison.OrdinalIgnoreCase) == true)
                {
                    kopyaMenu = mi;
                }
                else if (string.Equals(mi.Header?.ToString(), "Belgede göster", StringComparison.OrdinalIgnoreCase))
                {
                    belgeMenu = mi;
                }
            }
        }

        if (DosyaListesi.SelectedItem is not DosyaGorunumu dg)
        {
            foreach (var item in menu.Items)
            {
                if (item is MenuItem mi)
                {
                    mi.IsEnabled = false;
                }
            }
            if (duzenleMenu != null)
            {
                duzenleMenu.Items.Clear();
                duzenleMenu.Header = "Düzenle";
                duzenleMenu.Tag = null;
            }
            if (kopyaMenu != null)
            {
                kopyaMenu.Header = "Düzenlenebilir kopyayı aç";
                kopyaMenu.Tag = null;
                kopyaMenu.Visibility = Visibility.Collapsed;
            }
            if (belgeMenu != null)
            {
                belgeMenu.Visibility = Visibility.Collapsed;
            }
            return;
        }

        foreach (var item in menu.Items)
        {
            if (item is MenuItem mi)
            {
                mi.IsEnabled = true;
            }
        }

        if (belgeMenu != null)
        {
            belgeMenu.Visibility = string.Equals(dg.Durum, "tamam", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (duzenleMenu != null)
        {
            duzenleMenu.Items.Clear();
            duzenleMenu.Header = "Düzenle";
            duzenleMenu.Tag = null;
            var apps = _artCraftKatalogu.UzantiIcin(dg.Uzanti);
            if (apps.Count > 0)
            {
                foreach (var app in apps)
                {
                    var item = new MenuItem { Header = $"{app.Ad} {app.Surum}" };
                    item.Click += (_, _) => ArtCraftIleAc(app, dg.Yol, dg.Ad);
                    duzenleMenu.Items.Add(item);
                }
            }
            else
            {
                duzenleMenu.Items.Add(new MenuItem { Header = "ArtCraft uygulaması bulunamadı", IsEnabled = false });
            }
        }

        if (kopyaMenu != null)
        {
            kopyaMenu.Tag = null;
            string? depo = _dizinKaynagi?.DizinKlasoru ?? DizinAyari.Coz(_kokKlasor);
            string? duzenlenebilirYol = !string.IsNullOrWhiteSpace(dg.Sha) && !string.IsNullOrWhiteSpace(depo)
                ? BelgeYollari.Duzenlenebilir(depo, dg.Sha)
                : null;

            if (!string.IsNullOrWhiteSpace(duzenlenebilirYol))
            {
                string kopyaUzanti = Path.GetExtension(duzenlenebilirYol);
                var kopyaApps = _artCraftKatalogu.UzantiIcin(kopyaUzanti);
                var ilkApp = kopyaApps.FirstOrDefault();
                if (ilkApp != null)
                {
                    kopyaMenu.Header = $"Düzenlenebilir kopyayı aç ({ilkApp.Ad})";
                    kopyaMenu.Tag = (ilkApp, duzenlenebilirYol, dg.Ad);
                    kopyaMenu.Visibility = Visibility.Visible;
                }
                else
                {
                    kopyaMenu.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                kopyaMenu.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void DosyaMenuDuzenlenebilirKopya_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is ValueTuple<ArtCraftUygulamasi, string, string> bilgi)
        {
            DuzenlenebilirKopyayiAc(bilgi.Item1, bilgi.Item2, bilgi.Item3);
        }
    }

    private void BelgeContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
        {
            return;
        }

        MenuItem? duzenleMenu = null;
        MenuItem? kopyaMenu = null;

        foreach (var item in menu.Items)
        {
            if (item is MenuItem mi)
            {
                if (string.Equals(mi.Name, "BelgeMenuDuzenle", StringComparison.Ordinal) ||
                    string.Equals(mi.Header?.ToString(), "Düzenle", StringComparison.OrdinalIgnoreCase))
                {
                    duzenleMenu = mi;
                }
                else if (string.Equals(mi.Name, "BelgeMenuDuzenlenebilirKopya", StringComparison.Ordinal) ||
                         mi.Header?.ToString()?.StartsWith("Düzenlenebilir", StringComparison.OrdinalIgnoreCase) == true)
                {
                    kopyaMenu = mi;
                }
            }
        }

        if (BelgeListesi.SelectedItem is not BelgeSonucGorunumu bg)
        {
            foreach (var item in menu.Items)
            {
                if (item is MenuItem mi)
                {
                    mi.IsEnabled = false;
                }
            }
            if (duzenleMenu != null)
            {
                duzenleMenu.Items.Clear();
                duzenleMenu.Header = "Düzenle";
                duzenleMenu.Tag = null;
            }
            if (kopyaMenu != null)
            {
                kopyaMenu.Header = "Düzenlenebilir kopyayı aç";
                kopyaMenu.Tag = null;
                kopyaMenu.Visibility = Visibility.Collapsed;
            }
            return;
        }

        foreach (var item in menu.Items)
        {
            if (item is MenuItem mi)
            {
                mi.IsEnabled = true;
            }
        }

        if (duzenleMenu != null)
        {
            duzenleMenu.Items.Clear();
            duzenleMenu.Header = "Düzenle";
            duzenleMenu.Tag = null;
            string uzanti = Path.GetExtension(bg.Yol);
            var apps = _artCraftKatalogu.UzantiIcin(uzanti);
            if (apps.Count > 0)
            {
                foreach (var app in apps)
                {
                    var item = new MenuItem { Header = $"{app.Ad} {app.Surum}" };
                    item.Click += (_, _) => ArtCraftIleAc(app, bg.Yol, bg.Ad);
                    duzenleMenu.Items.Add(item);
                }
            }
            else
            {
                duzenleMenu.Items.Add(new MenuItem { Header = "ArtCraft uygulaması bulunamadı", IsEnabled = false });
            }
        }

        if (kopyaMenu != null)
        {
            kopyaMenu.Tag = null;
            string? depo = _dizinKaynagi?.DizinKlasoru ?? DizinAyari.Coz(_kokKlasor);
            string? duzenlenebilirYol = !string.IsNullOrWhiteSpace(bg.Sha) && !string.IsNullOrWhiteSpace(depo)
                ? BelgeYollari.Duzenlenebilir(depo, bg.Sha)
                : null;

            if (!string.IsNullOrWhiteSpace(duzenlenebilirYol))
            {
                string kopyaUzanti = Path.GetExtension(duzenlenebilirYol);
                var kopyaApps = _artCraftKatalogu.UzantiIcin(kopyaUzanti);
                var ilkApp = kopyaApps.FirstOrDefault();
                if (ilkApp != null)
                {
                    kopyaMenu.Header = $"Düzenlenebilir kopyayı aç ({ilkApp.Ad})";
                    kopyaMenu.Tag = (ilkApp, duzenlenebilirYol, bg.Ad);
                    kopyaMenu.Visibility = Visibility.Visible;
                }
                else
                {
                    kopyaMenu.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                kopyaMenu.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void BelgeMenuAc_Click(object sender, RoutedEventArgs e)
    {
        if (BelgeListesi.SelectedItem is BelgeSonucGorunumu bg)
        {
            BelgeyiAc(bg);
        }
    }

    private void BelgeMenuAsliniAc_Click(object sender, RoutedEventArgs e)
    {
        if (BelgeListesi.SelectedItem is BelgeSonucGorunumu bg)
        {
            DosyaAc(bg.Yol);
        }
    }

    private void BelgeMenuKlasor_Click(object sender, RoutedEventArgs e)
    {
        if (BelgeListesi.SelectedItem is BelgeSonucGorunumu bg)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{bg.Yol}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Klasör açılamadı:\n" + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void BelgeMenuKopyala_Click(object sender, RoutedEventArgs e)
    {
        if (BelgeListesi.SelectedItem is BelgeSonucGorunumu bg)
        {
            try
            {
                Clipboard.SetText(bg.Yol);
            }
            catch
            {
                // Pano hatasını yut
            }
        }
    }

    private void BelgeMenuDuzenlenebilirKopya_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is ValueTuple<ArtCraftUygulamasi, string, string> bilgi)
        {
            DuzenlenebilirKopyayiAc(bilgi.Item1, bilgi.Item2, bilgi.Item3);
        }
    }

    private void ArtCraftIleAc(ArtCraftUygulamasi app, string dosyaYolu, string ozgunAd)
    {
        if (!File.Exists(dosyaYolu))
        {
            MessageBox.Show(this, "Dosya bulunamadı:\n" + dosyaYolu, "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var sonuc = MessageBox.Show(
            this,
            "Arşivdeki özgün dosyayı açıyorsunuz. Kaydederseniz arşivdeki dosya değişir.\n\nEvet: kopyada düzenle (önerilen)\nHayır: özgün dosyayı düzenle\nİptal: vazgeç",
            "Özgün dosya düzenlenecek",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);

        if (sonuc == MessageBoxResult.Cancel)
        {
            return;
        }

        string acilacakYol = dosyaYolu;
        if (sonuc == MessageBoxResult.Yes)
        {
            try
            {
                acilacakYol = ArtCraftKatalogu.DuzenlenebilirKopyaHazirla(dosyaYolu, ozgunAd);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Kopya hazırlanamadı:\n" + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        try
        {
            Process.Start(ArtCraftKatalogu.AcmaBilgisi(app, acilacakYol));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Uygulama başlatılamadı:\n" + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DuzenlenebilirKopyayiAc(ArtCraftUygulamasi app, string duzenlenebilirYol, string ozgunAd)
    {
        if (!File.Exists(duzenlenebilirYol))
        {
            MessageBox.Show(this, "Düzenlenebilir kopya dosyası bulunamadı:\n" + duzenlenebilirYol, "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string kopya;
        try
        {
            kopya = ArtCraftKatalogu.DuzenlenebilirKopyaHazirla(duzenlenebilirYol, ozgunAd);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Kopya hazırlanamadı:\n" + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            Process.Start(ArtCraftKatalogu.AcmaBilgisi(app, kopya));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Uygulama başlatılamadı:\n" + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DosyaMenuAc_Click(object sender, RoutedEventArgs e)
    {
        if (DosyaListesi.SelectedItem is DosyaGorunumu dg)
        {
            DosyaAc(dg.Yol);
        }
    }

    private void DosyaMenuKlasor_Click(object sender, RoutedEventArgs e)
    {
        if (DosyaListesi.SelectedItem is DosyaGorunumu dg)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{dg.Yol}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Klasör açılamadı:\n" + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void DosyaMenuKopyala_Click(object sender, RoutedEventArgs e)
    {
        if (DosyaListesi.SelectedItem is DosyaGorunumu dg)
        {
            try
            {
                Clipboard.SetText(dg.Yol);
            }
            catch
            {
                // Pano hatasını yut
            }
        }
    }

    private void DosyaMenuBelge_Click(object sender, RoutedEventArgs e)
    {
        if (DosyaListesi.SelectedItem is DosyaGorunumu dg && _dizinKaynagi != null)
        {
            try
            {
                var ayrismis = DosyaSorgusu.Ayristir(_sonAramaSorgusu);
                string belgeSorgusu = ayrismis.Icerik ?? ayrismis.DuzMetin;
                var pencere = new BelgePenceresi(_dizinKaynagi, dg.Id, 1, belgeSorgusu, _artCraftKatalogu)
                {
                    Owner = this
                };
                pencere.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Belge açılamadı:\n" + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void DosyaAc(string yol)
    {
        if (string.IsNullOrWhiteSpace(yol) || !File.Exists(yol))
        {
            MessageBox.Show(this, "Dosya bulunamadı:\n" + yol, "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(yol) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Dosya açılamadı:\n" + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BelgeListesi_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (BelgeListesi.SelectedItem is BelgeSonucGorunumu bg)
        {
            BelgeyiAc(bg);
        }
    }

    private void BelgeListesi_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (BelgeListesi.SelectedItem is BelgeSonucGorunumu bg)
        {
            BelgeyiAc(bg);
        }
    }

    private void BelgeListesi_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && BelgeListesi.SelectedItem is BelgeSonucGorunumu bg)
        {
            BelgeyiAc(bg);
            e.Handled = true;
        }
        else if (e.Key == Key.Up && BelgeListesi.SelectedIndex <= 0)
        {
            AramaKutusu.Focus();
            e.Handled = true;
        }
    }

    private void BelgeyiAc(BelgeSonucGorunumu bg)
    {
        if (_dizinKaynagi == null) return;

        try
        {
            var ayrismis = DosyaSorgusu.Ayristir(_sonAramaSorgusu);
            string belgeSorgusu = ayrismis.Icerik ?? ayrismis.DuzMetin;
            var pencere = new BelgePenceresi(_dizinKaynagi, bg.DosyaId, bg.SayfaNo, belgeSorgusu, _artCraftKatalogu,
                bg.Sonuc.CevirideEslesti)
            {
                Owner = this
            };
            pencere.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Belge açılamadı:\n" + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AramaKutusu_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            if (AnaSekmeler.SelectedItem == TabDosyalar)
            {
                if (DosyaListesi.Items.Count > 0)
                {
                    if (DosyaListesi.SelectedIndex < 0) DosyaListesi.SelectedIndex = 0;
                    DosyaListesi.Focus();
                    e.Handled = true;
                }
            }
            else if (AnaSekmeler.SelectedItem == TabBelgeler)
            {
                if (BelgeListesi.Items.Count > 0)
                {
                    if (BelgeListesi.SelectedIndex < 0) BelgeListesi.SelectedIndex = 0;
                    BelgeListesi.Focus();
                    e.Handled = true;
                }
            }
            else if (AnaSekmeler.SelectedItem == TabVideolar)
            {
                if (SonucListesi.Items.Count > 0)
                {
                    if (SonucListesi.SelectedIndex < 0)
                    {
                        SonucListesi.SelectedIndex = 0;
                    }
                    SonucListesi.Focus();
                    var item = (ListViewItem?)SonucListesi.ItemContainerGenerator.ContainerFromIndex(SonucListesi.SelectedIndex);
                    item?.Focus();
                    e.Handled = true;
                }
            }
        }
        else if (e.Key == Key.Enter)
        {
            if (AnaSekmeler.SelectedItem == TabDosyalar)
            {
                if (DosyaListesi.SelectedItem is DosyaGorunumu dg)
                {
                    DosyaAc(dg.Yol);
                    e.Handled = true;
                }
                else if (DosyaListesi.Items.Count > 0)
                {
                    DosyaListesi.SelectedIndex = 0;
                    if (DosyaListesi.SelectedItem is DosyaGorunumu first) DosyaAc(first.Yol);
                    e.Handled = true;
                }
            }
            else if (AnaSekmeler.SelectedItem == TabBelgeler)
            {
                if (BelgeListesi.SelectedItem is BelgeSonucGorunumu bg)
                {
                    BelgeyiAc(bg);
                    e.Handled = true;
                }
                else if (BelgeListesi.Items.Count > 0)
                {
                    BelgeListesi.SelectedIndex = 0;
                    if (BelgeListesi.SelectedItem is BelgeSonucGorunumu first) BelgeyiAc(first);
                    e.Handled = true;
                }
            }
            else if (AnaSekmeler.SelectedItem == TabVideolar)
            {
                if (SonucListesi.SelectedItem != null)
                {
                    SeciliSonucuAc();
                    e.Handled = true;
                }
                else if (SonucListesi.Items.Count > 0)
                {
                    SonucListesi.SelectedIndex = 0;
                    SeciliSonucuAc();
                    e.Handled = true;
                }
            }
        }
    }

    private void SonucListesi_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SeciliSonucuAc();
            e.Handled = true;
        }
        else if (e.Key == Key.Up && SonucListesi.SelectedIndex <= 0)
        {
            AramaKutusu.Focus();
            e.Handled = true;
        }
    }

    private void SonucListesi_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        SeciliSonucuAc();
    }

    private void DersListesi_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SeciliDersiAc();
            e.Handled = true;
        }
    }

    private void DersListesi_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        SeciliDersiAc();
    }

    private void SeciliSonucuAc()
    {
        if (SonucListesi.SelectedItem is not SonucGorunumu oge) return;
        SonucuAc(oge);
    }

    private void SonucuAc(SonucGorunumu oge)
    {
        var ders = _dersler.FirstOrDefault(d => string.Equals(d.JsonYolu, oge.Sonuc.JsonYolu, StringComparison.OrdinalIgnoreCase))
                ?? _dersler.FirstOrDefault(d => string.Equals(d.Ad, oge.DersAd, StringComparison.OrdinalIgnoreCase));

        if (ders == null)
        {
            string klasor = Path.GetDirectoryName(oge.Sonuc.JsonYolu) ?? _kokKlasor;
            ders = new DersKaydi(oge.DersAd, klasor, oge.Sonuc.JsonYolu, null);
        }

        double baslangicSn = Math.Max(0, oge.BasSn - 2.0);
        DersPenceresiniAc(ders, baslangicSn, oge.Sonuc.CevirideEslesti);
    }

    private void SeciliDersiAc()
    {
        if (DersListesi.SelectedItem is not DersKaydi ders) return;
        DersPenceresiniAc(ders, 0.0);
    }

    private void DersPenceresiniAc(DersKaydi ders, double baslangicSn, bool ceviriGorunumu = false)
    {
        try
        {
            string dersJson = Path.Combine(ders.KlasorYolu, "ders.json");
            Window pencere;
            if (File.Exists(dersJson))
            {
                var dersTanimi = DersTanimiOkuyucu.Oku(dersJson);
                var cokAcili = new CokAciliOynaticiPenceresi(dersTanimi, baslangicSn, ders.JsonYolu, _aramaMotoru as MetinAramaMotoru);
                if (ceviriGorunumu)
                {
                    cokAcili.CeviriGorunumuneGec();
                }

                pencere = cokAcili;
            }
            else
            {
                pencere = new OynaticiPenceresi(ders, baslangicSn, _aramaMotoru as MetinAramaMotoru, ceviriGorunumu)
                {
                    Dizin = _dizinKaynagi
                };
            }

            pencere.Owner = this;
            pencere.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Ders açılırken hata oluştu:\n" + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _aramaZamanlayici.Stop();
        _dizinZamanlayici.Stop();

        if (_aramaCts != null)
        {
            _aramaCts.Cancel();
            _aramaCts.Dispose();
            _aramaCts = null;
        }

        if (_indekslemeCts != null)
        {
            _indekslemeCts.Cancel();
            _indekslemeCts.Dispose();
            _indekslemeCts = null;
        }

        _dizinKaynagi?.Dispose();
        _dizinKaynagi = null;

        if (_aramaMotoru is IDisposable disp)
        {
            disp.Dispose();
        }
    }
}

/// <summary>
/// Dosya kataloğu sonucu satırının görünüm modeli.
/// </summary>
public sealed class DosyaGorunumu(DosyaSonucu sonuc)
{
    public DosyaSonucu Sonuc { get; } = sonuc;
    public long Id => Sonuc.Id;
    public string Yol => Sonuc.Yol;
    public string Ad => Sonuc.Ad;
    public string Klasor => Sonuc.Klasor;
    public string Uzanti => Sonuc.Uzanti;
    public long Boyut => Sonuc.Boyut;
    public string BoyutMetni => BoyutBicimle(Sonuc.Boyut);
    public DateTime Degisme => Sonuc.Degisme;
    public string DegismeMetni => Sonuc.Degisme.ToString("dd.MM.yyyy HH:mm");
    public string Tur => Sonuc.Tur;
    public string Durum => Sonuc.Durum;
    public int? SayfaSayisi => Sonuc.SayfaSayisi;
    public string? Ders => Sonuc.Ders;
    public string? Sha => Sonuc.Sha;

    private static string BoyutBicimle(long bayt)
    {
        if (bayt < 1024) return $"{bayt} B";
        if (bayt < 1024 * 1024) return $"{(bayt / 1024.0):F1} KB";
        if (bayt < 1024 * 1024 * 1024) return $"{(bayt / (1024.0 * 1024.0)):F1} MB";
        return $"{(bayt / (1024.0 * 1024.0 * 1024.0)):F2} GB";
    }
}

/// <summary>
/// Belge içeriği sonucu satırının görünüm modeli.
/// </summary>
public sealed class BelgeSonucGorunumu(IcerikSonucu sonuc)
{
    public IcerikSonucu Sonuc { get; } = sonuc;
    public long DosyaId => Sonuc.DosyaId;
    public string Yol => Sonuc.Yol;
    public string Ad => Sonuc.Ad;
    public string Klasor { get; } = Path.GetDirectoryName(sonuc.Yol) ?? string.Empty;
    public int SayfaNo => Sonuc.SayfaNo;
    public string Etiket => Sonuc.Etiket;
    public string Kaynak => Sonuc.Kaynak;
    public double? Guven => Sonuc.Guven;
    public string Alinti => Sonuc.Alinti;
    public (int Bas, int Uzunluk)[] Vurgular => Sonuc.Vurgular;
    public string? Ders => Sonuc.Ders;
    public string? Sha => Sonuc.Sha;

    public Visibility DersRozetiGorunurluk => !string.IsNullOrWhiteSpace(Ders) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CeviriRozetiGorunurluk => Sonuc.CevirideEslesti ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OcrGorunur => string.Equals(Kaynak, "ocr", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
    public string OcrMetni
    {
        get
        {
            if (!Guven.HasValue) return "OCR";
            int yuzde = Guven.Value <= 1.0 ? (int)Math.Round(Guven.Value * 100) : (int)Math.Round(Guven.Value);
            return $"OCR %{yuzde}";
        }
    }
}

/// <summary>
/// Video arama sonucu satırının görünüm modeli.
/// </summary>
public sealed class SonucGorunumu(AramaSonucu sonuc)
{
    public AramaSonucu Sonuc { get; } = sonuc;
    public string DersAd => Sonuc.DersAd;
    public double BasSn => Sonuc.BasSn;
    public string ZamanMetni => ZamanBicim.Bicimle(Sonuc.BasSn);
    public string Metin => Sonuc.Metin;
    public (int Bas, int Uzunluk)[] Vurgular => Sonuc.Vurgular;
    public Visibility CeviriRozetiGorunurluk => Sonuc.CevirideEslesti ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>
/// Arama sonuçlarında eşleşen kelimeleri sarı kalın vurguyla gösteren bağlı özellik yardımcısı.
/// </summary>
public static class VurguYardimcisi
{
    public static readonly DependencyProperty VurguBilgisiProperty =
        DependencyProperty.RegisterAttached(
            "VurguBilgisi",
            typeof(object),
            typeof(VurguYardimcisi),
            new PropertyMetadata(null, OnVurguBilgisiChanged));

    public static object? GetVurguBilgisi(DependencyObject obj) =>
        obj.GetValue(VurguBilgisiProperty);

    public static void SetVurguBilgisi(DependencyObject obj, object? value) =>
        obj.SetValue(VurguBilgisiProperty, value);

    private static void OnVurguBilgisiChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;

        tb.Inlines.Clear();
        if (e.NewValue == null) return;

        string? metin = null;
        (int Bas, int Uzunluk)[]? vurgular = null;

        if (e.NewValue is SonucGorunumu sg)
        {
            metin = sg.Metin;
            vurgular = sg.Vurgular;
        }
        else if (e.NewValue is BelgeSonucGorunumu bg)
        {
            metin = bg.Alinti;
            vurgular = bg.Vurgular;
        }

        if (string.IsNullOrEmpty(metin)) return;

        if (vurgular == null || vurgular.Length == 0)
        {
            tb.Inlines.Add(new Run(metin));
            return;
        }

        int mevcut = 0;
        foreach (var (bas, uz) in vurgular)
        {
            if (bas < 0 || uz <= 0 || bas >= metin.Length) continue;

            int duzgunBas = Math.Max(mevcut, bas);
            if (duzgunBas > mevcut)
            {
                tb.Inlines.Add(new Run(metin.Substring(mevcut, duzgunBas - mevcut)));
            }

            int son = Math.Min(bas + uz, metin.Length);
            if (son > duzgunBas)
            {
                var run = new Run(metin.Substring(duzgunBas, son - duzgunBas))
                {
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 180, 50)),
                    Background = new SolidColorBrush(Color.FromArgb(90, 255, 136, 0))
                };
                tb.Inlines.Add(run);
                mevcut = son;
            }
            else
            {
                mevcut = Math.Max(mevcut, duzgunBas);
            }
        }

        if (mevcut < metin.Length)
        {
            tb.Inlines.Add(new Run(metin.Substring(mevcut)));
        }
    }
}
