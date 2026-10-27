namespace Portal.Uygulama.Kontroller;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Portal.Uygulama;

public partial class VlcKontrolCubugu : UserControl
{
    public static readonly DependencyProperty OynaticiProperty =
        DependencyProperty.Register(nameof(Oynatici), typeof(LibVlcOynatici),
            typeof(VlcKontrolCubugu), new PropertyMetadata(null, OynaticiDegisti));

    private readonly DispatcherTimer _zamanlayici;
    private readonly ZamanKaydiraciDavranisi _aramaDavranisi;
    private bool Surukleniyor => _aramaDavranisi?.Surukleniyor == true;
    private bool _programatikGuncelleme;
    private bool _hariciZamanKullaniliyor;
    private bool _yuklendi;
    private bool _oynaticiOlaylarinaAbone;
    private double _hariciZaman;
    private double _hariciToplamSure;
    private string _seciliEnBoyOrani = "Varsayılan";
    private string? _seciliEkolayzer;

    public event EventHandler? OynatDuraklatIstendi;
    public event EventHandler? TamEkranIstendi;

    public LibVlcOynatici? Oynatici
    {
        get => (LibVlcOynatici?)GetValue(OynaticiProperty);
        set => SetValue(OynaticiProperty, value);
    }

    public bool AramaSurukleniyor => Surukleniyor;
    public double AramaZamani => AramaKaydiraci.Value;
    public double AramaAzami => AramaKaydiraci.Maximum;

    public void TamEkranDurumunuGuncelle(bool tamEkran)
    {
        TamEkranDugmesi.Content = tamEkran ? "\uE73F" : "\uE740";
        TamEkranDugmesi.ToolTip = tamEkran ? "Pencereli görünüm" : "Tam ekran";
    }

    public void ZamanBilgisiniGuncelle(double zaman, double toplamSure)
    {
        _hariciZamanKullaniliyor = true;
        _hariciZaman = double.IsFinite(zaman) ? Math.Max(0, zaman) : 0;
        _hariciToplamSure = double.IsFinite(toplamSure) ? Math.Max(0, toplamSure) : 0;
        Guncelle();
    }

    public VlcKontrolCubugu()
    {
        InitializeComponent();
        _aramaDavranisi = new ZamanKaydiraciDavranisi(AramaKaydiraci,
            () => _hariciZamanKullaniliyor ? _hariciToplamSure : Oynatici?.ToplamSure ?? 0,
            zaman => Oynatici?.Ara(zaman));
        _zamanlayici = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _zamanlayici.Tick += (_, _) => Guncelle();
    }

    private static void OynaticiDegisti(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        VlcKontrolCubugu kontrol = (VlcKontrolCubugu)d;
        if (kontrol._oynaticiOlaylarinaAbone && e.OldValue is LibVlcOynatici eskiOynatici)
        {
            kontrol.OynaticiOlaylarinaAboneOl(eskiOynatici, false);
        }

        if (kontrol._yuklendi && e.NewValue is LibVlcOynatici yeniOynatici)
        {
            kontrol.OynaticiOlaylarinaAboneOl(yeniOynatici, true);
        }

        kontrol._hariciZamanKullaniliyor = false;
        kontrol.Guncelle();
    }

    public void OynaticiBilgisiniGuncelle() => Guncelle();

    private void OynatmaDurumuDegisti(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(Guncelle);

    private void KontrolCubugu_Loaded(object sender, RoutedEventArgs e)
    {
        _yuklendi = true;
        if (Oynatici is { } oynatici)
        {
            OynaticiOlaylarinaAboneOl(oynatici, true);
        }
        UygulamaTemasiniUygula();
        _zamanlayici.Start();
        Guncelle();
    }

    private void KontrolCubugu_Unloaded(object sender, RoutedEventArgs e)
    {
        _yuklendi = false;
        _zamanlayici.Stop();
        if (Oynatici is { } oynatici)
        {
            OynaticiOlaylarinaAboneOl(oynatici, false);
        }
    }

    private void OynaticiOlaylarinaAboneOl(LibVlcOynatici oynatici, bool abone)
    {
        if (abone == _oynaticiOlaylarinaAbone)
        {
            return;
        }

        if (abone)
        {
            oynatici.MediaPlayer.Playing += OynatmaDurumuDegisti;
            oynatici.MediaPlayer.Paused += OynatmaDurumuDegisti;
            oynatici.MediaPlayer.Stopped += OynatmaDurumuDegisti;
        }
        else
        {
            oynatici.MediaPlayer.Playing -= OynatmaDurumuDegisti;
            oynatici.MediaPlayer.Paused -= OynatmaDurumuDegisti;
            oynatici.MediaPlayer.Stopped -= OynatmaDurumuDegisti;
        }

        _oynaticiOlaylarinaAbone = abone;
    }

    private void UygulamaTemasiniUygula()
    {
        string[] renkler =
        [
            "ArkaPlanFircasi", "YuzeyFircasi", "Yuzey2Fircasi", "KenarlikFircasi",
            "MetinFircasi", "SolukMetinFircasi", "VurguFircasi", "VurguHoverFircasi",
            "SimgeDugme", "VlcDugme", "VlcOynatDugmesi", "VlcSlider", "SimgeYaziTipi"
        ];
        foreach (string anahtar in renkler)
        {
            if (Application.Current?.TryFindResource(anahtar) is not null)
            {
                Resources.Remove(anahtar);
            }
        }
    }

    private void Guncelle()
    {
        LibVlcOynatici? oynatici = Oynatici;
        _programatikGuncelleme = true;
        try
        {
            if (_hariciZamanKullaniliyor)
            {
                double disToplam = Math.Max(0, _hariciToplamSure);
                double disZaman = Math.Clamp(_hariciZaman, 0, disToplam > 0 ? disToplam : double.MaxValue);
                AramaKaydiraci.Maximum = disToplam > 0 ? disToplam : 1;
                if (!Surukleniyor)
                {
                    AramaKaydiraci.Value = Math.Min(disZaman, AramaKaydiraci.Maximum);
                }

                GecenSure.Text = SureBiçimle(disZaman);
                ToplamSure.Text = disToplam > 0 ? SureBiçimle(disToplam) : "--:--";
                return;
            }

            if (oynatici is null)
            {
                AramaKaydiraci.Value = 0;
                AramaKaydiraci.Maximum = 1;
                GecenSure.Text = "00:00";
                ToplamSure.Text = "--:--";
                SesKaydiraci.Value = 0;
                OynatDuraklatDugmesi.Content = "\uE768";
                return;
            }

            double toplam = Math.Max(0, oynatici.ToplamSure);
            double zaman = Math.Clamp(oynatici.Zaman, 0, toplam > 0 ? toplam : double.MaxValue);
            AramaKaydiraci.Maximum = toplam > 0 ? toplam : 1;
            if (!Surukleniyor)
            {
                AramaKaydiraci.Value = Math.Min(zaman, AramaKaydiraci.Maximum);
            }

            GecenSure.Text = SureBiçimle(zaman);
            ToplamSure.Text = toplam > 0 ? SureBiçimle(toplam) : "--:--";
            SesKaydiraci.Value = Math.Clamp(oynatici.SesSeviyesi, 0, 100);
            OynatDuraklatDugmesi.Content = oynatici.Oynatiliyor ? "\uE769" : "\uE768";
            OynatDuraklatDugmesi.ToolTip = oynatici.Oynatiliyor ? "Duraklat" : "Oynat";
            HizDugmesi.Content = $"{oynatici.Hiz:0.##}×";
            SesDugmesi.Content = oynatici.Sessiz || oynatici.SesSeviyesi == 0 ? "\uE74F" : "\uE767";
            SesDugmesi.ToolTip = oynatici.Sessiz ? "Sesi aç" : "Sessiz";
        }
        catch
        {
            GecenSure.Text = "00:00";
            ToplamSure.Text = "--:--";
        }
        finally
        {
            _programatikGuncelleme = false;
        }
    }

    private static string SureBiçimle(double saniye)
    {
        if (!double.IsFinite(saniye) || saniye < 0)
        {
            saniye = 0;
        }

        TimeSpan sure = TimeSpan.FromSeconds(Math.Min(saniye, 1_000_000_000));
        return sure.TotalHours >= 1
            ? $"{(int)sure.TotalHours:00}:{sure.Minutes:00}:{sure.Seconds:00}"
            : $"{sure.Minutes:00}:{sure.Seconds:00}";
    }

    private void OynatDuraklat_Click(object sender, RoutedEventArgs e)
    {
        if (OynatDuraklatIstendi is null)
        {
            Oynatici?.OynatDuraklat();
        }
        else
        {
            OynatDuraklatIstendi.Invoke(this, EventArgs.Empty);
        }
        Guncelle();
    }

    private void Dur_Click(object sender, RoutedEventArgs e)
    {
        Oynatici?.Dur();
        Guncelle();
    }

    private void Geri_Click(object sender, RoutedEventArgs e) => ZamanAtla(-10);
    private void Ileri_Click(object sender, RoutedEventArgs e) => ZamanAtla(10);

    private void ZamanAtla(double saniye)
    {
        if (Oynatici is { } oynatici)
        {
            oynatici.Ara(Math.Max(0, oynatici.Zaman + saniye));
            Guncelle();
        }
    }

    private void SonrakiKare_Click(object sender, RoutedEventArgs e) => Oynatici?.SonrakiKare();

    private void AramaKaydiraci_DegerDegisti(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Surukleniyor && !_programatikGuncelleme)
        {
            GecenSure.Text = SureBiçimle(AramaKaydiraci.Value);
        }
    }

    private void AramaKaydiraci_FareHareketi(object sender, MouseEventArgs e)
    {
        Point nokta = e.GetPosition(AramaKaydiraci);
        double oran = Math.Clamp(nokta.X / Math.Max(1, AramaKaydiraci.ActualWidth), 0, 1);
        AramaKaydiraci.ToolTip = SureBiçimle(AramaKaydiraci.Maximum * oran);
        ToolTipService.SetPlacement(AramaKaydiraci, PlacementMode.Top);
    }

    private void SesKaydiraci_DegerDegisti(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_programatikGuncelleme && Oynatici is { } oynatici)
        {
            oynatici.SesSeviyesi = (int)Math.Round(SesKaydiraci.Value);
            Guncelle();
        }
    }

    private void Sessiz_Click(object sender, RoutedEventArgs e)
    {
        if (Oynatici is { } oynatici)
        {
            oynatici.Sessiz = !oynatici.Sessiz;
            Guncelle();
        }
    }

    private void SesParcasi_Click(object sender, RoutedEventArgs e)
    {
        ContextMenu menu = new();
        foreach ((int id, string ad) in Oynatici?.SesParcalari ?? Array.Empty<(int Id, string Ad)>())
        {
            menu.Items.Add(ParcaMenusu(ad, id == Oynatici?.SesParcasi,
                () => { if (Oynatici is { } o) o.SesParcasi = id; }));
        }

        if (menu.Items.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "Ses parçası yok", IsEnabled = false });
        }
        menu.Items.Add(new Separator());
        MenuItem ekolayzer = new() { Header = "Ekolayzer" };
        ekolayzer.Items.Add(new MenuItem
        {
            Header = "Kapalı",
            IsCheckable = true,
            IsChecked = _seciliEkolayzer is null,
            Command = new RelayCommand(() =>
            {
                if (Oynatici?.EkolayzerKapat() == true)
                {
                    _seciliEkolayzer = null;
                }
            })
        });
        foreach (string onAyar in Oynatici?.Ekolayzer ?? Array.Empty<string>())
        {
            ekolayzer.Items.Add(new MenuItem
            {
                Header = onAyar,
                IsCheckable = true,
                IsChecked = string.Equals(onAyar, _seciliEkolayzer, StringComparison.OrdinalIgnoreCase),
                Command = new RelayCommand(() =>
                {
                    if (Oynatici?.EkolayzerSec(onAyar) == true)
                    {
                        _seciliEkolayzer = onAyar;
                    }
                })
            });
        }
        if (ekolayzer.Items.Count > 0)
        {
            menu.Items.Add(ekolayzer);
        }
        menu.Items.Add(new MenuItem { Header = "Ses gecikmesini azalt", Command = new RelayCommand(() => GecikmeAyarla(altyazi: false, -100)) });
        menu.Items.Add(new MenuItem { Header = "Ses gecikmesini artır", Command = new RelayCommand(() => GecikmeAyarla(altyazi: false, 100)) });
        MenuAc(sender, menu);
    }

    private void Altyazi_Click(object sender, RoutedEventArgs e)
    {
        ContextMenu menu = new();
        menu.Items.Add(new MenuItem { Header = "Kapalı", IsCheckable = true, IsChecked = Oynatici?.AltyaziParcasi == -1,
            Command = new RelayCommand(() => { if (Oynatici is { } o) o.AltyaziParcasi = -1; }) });
        foreach ((int id, string ad) in Oynatici?.AltyaziParcalari.Where(parca => parca.Id != -1) ??
                 Array.Empty<(int Id, string Ad)>())
        {
            menu.Items.Add(ParcaMenusu(ad, id == Oynatici?.AltyaziParcasi,
                () => { if (Oynatici is { } o) o.AltyaziParcasi = id; }));
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Altyazı dosyası ekle...", Command = new RelayCommand(AltyaziDosyasiSec) });
        menu.Items.Add(new MenuItem { Header = "Altyazı gecikmesini azalt", Command = new RelayCommand(() => GecikmeAyarla(altyazi: true, -100)) });
        menu.Items.Add(new MenuItem { Header = "Altyazı gecikmesini artır", Command = new RelayCommand(() => GecikmeAyarla(altyazi: true, 100)) });
        MenuAc(sender, menu);
    }

    private static MenuItem ParcaMenusu(string ad, bool secili, Action eylem)
    {
        MenuItem oge = new() { Header = ad, IsCheckable = true, IsChecked = secili };
        oge.Click += (_, _) => eylem();
        return oge;
    }

    private void AltyaziDosyasiSec()
    {
        OpenFileDialog dialog = new()
        {
            Title = "Altyazı dosyası seçin",
            Filter = "Altyazı dosyaları|*.srt;*.ass;*.ssa;*.sub;*.vtt|Tüm dosyalar|*.*"
        };
        if (dialog.ShowDialog() == true)
        {
            Oynatici?.HaricAltyaziEkle(dialog.FileName);
        }
    }

    private void EnBoy_Click(object sender, RoutedEventArgs e)
    {
        ContextMenu menu = new();
        foreach (string oran in LibVlcOynatici.EnBoyOranlari)
        {
            menu.Items.Add(new MenuItem
            {
                Header = oran,
                IsCheckable = true,
                IsChecked = string.Equals(oran, _seciliEnBoyOrani, StringComparison.OrdinalIgnoreCase),
                Command = new RelayCommand(() =>
                {
                    _seciliEnBoyOrani = oran;
                    Oynatici?.EnBoyOraniSec(oran);
                })
            });
        }
        MenuAc(sender, menu);
    }

    private void Hiz_Click(object sender, RoutedEventArgs e)
    {
        ContextMenu menu = new();
        foreach (double hiz in LibVlcOynatici.HizOnayarlari)
        {
            menu.Items.Add(new MenuItem
            {
                Header = $"{hiz:0.##}×",
                IsCheckable = true,
                IsChecked = Math.Abs((Oynatici?.Hiz ?? 1) - hiz) < 0.001,
                Command = new RelayCommand(() => { if (Oynatici is { } o) o.Hiz = hiz; })
            });
        }
        MenuAc(sender, menu);
    }

    private void Dongu_Click(object sender, RoutedEventArgs e)
    {
        if (Oynatici is { } oynatici)
        {
            oynatici.Dongu = !oynatici.Dongu;
            DonguDugmesi.ToolTip = oynatici.Dongu ? "Döngüyü kapat" : "Döngü";
        }
    }

    private void AB_Click(object sender, RoutedEventArgs e)
    {
        Oynatici?.ABIsaretle();
        if (sender is FrameworkElement oge && Oynatici is { } oynatici)
        {
            oge.ToolTip = oynatici.ABDurumu;
        }
    }

    private void AnlikGoruntu_Click(object sender, RoutedEventArgs e)
    {
        Oynatici?.AnlikGoruntuAl(string.Empty);
    }

    private void TamEkran_Click(object sender, RoutedEventArgs e) =>
        TamEkranIstendi?.Invoke(this, EventArgs.Empty);

    private void GecikmeAyarla(bool altyazi, long miktar)
    {
        if (Oynatici is { } oynatici)
        {
            if (altyazi)
            {
                oynatici.AltyaziGecikmesiMs += miktar;
            }
            else
            {
                oynatici.SesGecikmesiMs += miktar;
            }
        }
    }

    private static void MenuAc(object sender, ContextMenu menu)
    {
        if (sender is FrameworkElement oge)
        {
            oge.ContextMenu = menu;
            menu.PlacementTarget = oge;
            menu.IsOpen = true;
        }
    }

    public bool KisayolIsle(Key key)
    {
        switch (key)
        {
            case Key.Up:
                if (Oynatici is { } yukari) yukari.SesSeviyesi = Math.Min(100, yukari.SesSeviyesi + 5);
                Guncelle();
                break;
            case Key.Down:
                if (Oynatici is { } asagi) asagi.SesSeviyesi = Math.Max(0, asagi.SesSeviyesi - 5);
                Guncelle();
                break;
            case Key.M:
                Sessiz_Click(this, new RoutedEventArgs());
                break;
            case Key.F:
                TamEkran_Click(this, new RoutedEventArgs());
                break;
            case Key.S:
                AnlikGoruntu_Click(this, new RoutedEventArgs());
                break;
            case Key.Oem4:
                HizAyarla(-1);
                break;
            case Key.Oem6:
                HizAyarla(1);
                break;
            default:
                return false;
        }
        return true;
    }

    private void HizAyarla(int yon)
    {
        if (Oynatici is not { } oynatici)
        {
            return;
        }

        int indeks = Array.FindIndex(LibVlcOynatici.HizOnayarlari,
            hiz => Math.Abs(hiz - oynatici.Hiz) < 0.001);
        indeks = Math.Clamp(indeks + yon, 0, LibVlcOynatici.HizOnayarlari.Length - 1);
        oynatici.Hiz = LibVlcOynatici.HizOnayarlari[indeks];
    }

    private sealed class RelayCommand(Action eylem) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => eylem();
    }
}
