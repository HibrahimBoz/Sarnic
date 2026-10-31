namespace Portal.Uygulama;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using Portal.Cekirdek;
using Portal.Uygulama.Temalar;

/// <summary>
/// Tek video oynatıcı penceresi ve transkript paneli (T11B).
/// </summary>
public partial class OynaticiPenceresi : Window
{
    private readonly LibVlcOynatici _oynatici;
    private readonly DispatcherTimer _zamanlayici;
    private readonly DispatcherTimer _tamEkranKontrolZamanlayici;
    private readonly List<EfektifSegment> _efektifSegmentler = [];
    private readonly ObservableCollection<TranskriptSatirGorunumu> _goruntulenenSegmentler = [];

    private string? _geciciSrtYolu;
    private string? _ekliGeciciSrtYolu;
    private int _ekliGeciciSrtParcaId = -1;
    private int _tamEkranOncekiSpu = -1;
    private bool _tamEkranSrtParcasiSecildi;
    private EfektifSegment? _oncekiAktifSegment;
    private bool _videoVar;
    private bool _tamEkran;
    private bool _programatikSecimYapiliyor;
    private double _toplamSureSn;
    private bool _kapatildi;

    private readonly DispatcherTimer _yoklamaZamanlayici;
    private readonly ParolaGecikmeSayaci _parolaSayaci = ParolaGecikmeSayaci.Paylasilan;
    private bool _duzenlemeAcik;
    private bool _oturumParolaDogrulandi;
    private bool _yoklamaGuncelleniyor;
    private DateTime _sonKlasorMtimeUtc = DateTime.MinValue;
    private int _sonDuzeltmeSayisi = -1;
    private MetinAramaMotoru? _aramaMotoru;
    private string? _kokKlasor;
    private Transkript? _hamTranskript;

    public DersKaydi Kayit { get; }
    public double BaslangicSn { get; }
    public IOynatici Oynatici => _oynatici;
    public Portal.Cekirdek.Dizin.DizinKaynagi? Dizin { get; init; }

    public OynaticiPenceresi()
        : this(new DersKaydi(string.Empty, string.Empty, string.Empty, null), 0, null)
    {
    }

    public OynaticiPenceresi(DersKaydi kayit, double baslangicSn = 0)
        : this(kayit, baslangicSn, null)
    {
    }

    public OynaticiPenceresi(DersKaydi kayit, double baslangicSn, MetinAramaMotoru? aramaMotoru)
    {
        Kayit = kayit ?? new DersKaydi(string.Empty, string.Empty, string.Empty, null);
        BaslangicSn = baslangicSn;
        _aramaMotoru = aramaMotoru;
        _kokKlasor = !string.IsNullOrWhiteSpace(Kayit.KlasorYolu)
            ? Kayit.KlasorYolu
            : Directory.GetCurrentDirectory();

        InitializeComponent();

        Title = !string.IsNullOrWhiteSpace(Kayit.Ad) ? $"{UrunAdi.Etkin} — Oynatıcı — {Kayit.Ad}" : $"{UrunAdi.Etkin} — Oynatıcı";
        Loaded += (_, _) => BelgeleriYukleAsync();

        _oynatici = new LibVlcOynatici();
        _oynatici.MediaPlayer.EncounteredError += OnMediaPlayerEncounteredError;
        _oynatici.MediaPlayer.LengthChanged += OnMediaPlayerLengthChanged;
        Kontrol.Oynatici = _oynatici;
        Kontrol.OynatDuraklatIstendi += (_, _) => OynatDuraklat();
        Kontrol.TamEkranIstendi += (_, _) => TamEkranGecis();
        VideoGosterici.MediaPlayer = _oynatici.MediaPlayer;

        TranskriptListesi.ItemsSource = _goruntulenenSegmentler;

        DuzenlemeKontrolu.Ders = Kayit;
        DuzenlemeKontrolu.KokKlasor = _kokKlasor;
        DuzenlemeKontrolu.AramaMotoru = _aramaMotoru;
        DuzenlemeKontrolu.DuzenlemeKaydedildi += OnDuzenlemeKaydedildi;

        // 250 ms aralıklı zaman senkronizasyon zamanlayıcısı
        _zamanlayici = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _zamanlayici.Tick += Zamanlayici_Tick;

        // 30 sn aralıklı başka kullanıcıların düzeltmelerini yoklama zamanlayıcısı
        _yoklamaZamanlayici = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _yoklamaZamanlayici.Tick += YoklamaZamanlayici_Tick;
        _tamEkranKontrolZamanlayici = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _tamEkranKontrolZamanlayici.Tick += (_, _) =>
        {
            _tamEkranKontrolZamanlayici.Stop();
            if (_tamEkran && !Kontrol.AramaSurukleniyor) Kontrol.Visibility = Visibility.Collapsed;
        };

        // Transkript ve videoyu yükle
        TranskriptiYukle();
        VideoyuYukle(baslangicSn);

        _zamanlayici.Start();
        _yoklamaZamanlayici.Start();
    }

    /// <summary>
    /// Transkript dosyasını okur ve düzeltmeler deposuyla birleştirerek listeyi doldurur.
    /// </summary>
    private void TranskriptiYukle()
    {
        _efektifSegmentler.Clear();
        _goruntulenenSegmentler.Clear();

        string? transkriptYolu = BulTranskriptYolu();
        if (string.IsNullOrWhiteSpace(transkriptYolu) || !File.Exists(transkriptYolu))
        {
            TranskriptDurumMetni.Text = "Transkript dosyası bulunamadı.";
            return;
        }

        try
        {
            _hamTranskript = TranskriptOkuyucu.Oku(transkriptYolu);
            if (string.IsNullOrEmpty(Kayit.JsonYolu))
            {
                var guncelKayit = new DersKaydi(Kayit.Ad, Kayit.KlasorYolu, transkriptYolu, Kayit.VideoYolu);
                DuzenlemeKontrolu.Ders = guncelKayit;
            }

            string duzeltmeKlasoru = DuzenlemeKontrolu.DuzeltmelerKlasoruBul();
            var depo = new DuzeltmeDeposu(duzeltmeKlasoru);
            var birlestirme = depo.Birlestir(_hamTranskript, duzeltmeKlasoru);

            foreach (var seg in birlestirme.Segmentler)
            {
                _efektifSegmentler.Add(seg);
            }

            FiltreyiUygula();
            IlkMtimeBelirle(duzeltmeKlasoru);

        }
        catch (Exception ex)
        {
            TranskriptDurumMetni.Text = $"Transkript okunamadı: {ex.Message}";
        }
    }

    private string? BulTranskriptYolu()
    {
        if (!string.IsNullOrWhiteSpace(Kayit.JsonYolu) && File.Exists(Kayit.JsonYolu))
        {
            return Kayit.JsonYolu;
        }

        if (!string.IsNullOrWhiteSpace(Kayit.KlasorYolu) && Directory.Exists(Kayit.KlasorYolu))
        {
            string aday = Path.Combine(Kayit.KlasorYolu, Kayit.Ad + ".json");
            if (File.Exists(aday))
            {
                return aday;
            }

            try
            {
                var adaylar = Directory.GetFiles(Kayit.KlasorYolu, "*.json");
                foreach (var a in adaylar)
                {
                    string ad = Path.GetFileName(a);
                    if (!string.Equals(ad, "ders.json", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(ad, "parola.json", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(ad, "ayarlar.json", StringComparison.OrdinalIgnoreCase))
                    {
                        return a;
                    }
                }
            }
            catch
            {
                // Yut
            }
        }

        return null;
    }

    /// <summary>
    /// Video dosyasını yükler ve başlangıç saniyesinden (baslangicSn - 2) başlatır.
    /// Video yoksa kullanıcıya uyarı gösterir ve arayüzü yalnızca transkript moduna alır.
    /// </summary>
    private void VideoyuYukle(double baslangicSn)
    {
        double baslatmaSn = OynaticiYardimcisi.BaslangicZamaniHesapla(baslangicSn);

        string? videoYolu = BulVideoYolu();
        if (string.IsNullOrWhiteSpace(videoYolu) || !File.Exists(videoYolu))
        {
            _videoVar = false;
            VideoUyariMetni.Text = "Video dosyası bulunamadı — yalnızca transkript gösteriliyor.";
            VideoUyariKutusu.Visibility = Visibility.Visible;
            VideoGosterici.Visibility = Visibility.Collapsed;

            Kontrol.IsEnabled = false;
            return;
        }

        try
        {
            _videoVar = true;
            VideoUyariKutusu.Visibility = Visibility.Collapsed;
            VideoGosterici.Visibility = Visibility.Visible;

            _oynatici.Yukle(videoYolu);

            if (baslatmaSn > 0)
            {
                void OnPlaying(object? s, EventArgs e)
                {
                    _oynatici.MediaPlayer.Playing -= OnPlaying;
                    Dispatcher.InvokeAsync(() =>
                    {
                        if (_kapatildi || !_videoVar)
                        {
                            return;
                        }

                        _oynatici.Ara(baslatmaSn);
                        if (!_oynatici.Oynatiliyor)
                        {
                            _oynatici.MediaPlayer.Play();
                        }
                        Kontrol.OynaticiBilgisiniGuncelle();
                    });
                }

                _oynatici.MediaPlayer.Playing += OnPlaying;
            }

            _oynatici.MediaPlayer.Play();
        }
        catch (Exception ex)
        {
            _videoVar = false;
            VideoUyariMetni.Text = $"Video dosyası açılamadı:\n{ex.Message}";
            VideoUyariKutusu.Visibility = Visibility.Visible;
            VideoGosterici.Visibility = Visibility.Collapsed;

            Kontrol.IsEnabled = false;
        }
    }

    private void OnMediaPlayerEncounteredError(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            VideoUyariMetni.Text = "Video oynatılırken bir hata oluştu.";
            VideoUyariKutusu.Visibility = Visibility.Visible;
        });
    }

    private void OnMediaPlayerLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            double sure = e.Length / 1000.0;
            if (sure > 0)
            {
                _toplamSureSn = sure;
                Kontrol.OynaticiBilgisiniGuncelle();
            }
        });
    }

    private string? BulVideoYolu()
    {
        if (!string.IsNullOrWhiteSpace(Kayit.VideoYolu) && File.Exists(Kayit.VideoYolu))
        {
            return Kayit.VideoYolu;
        }

        if (!string.IsNullOrWhiteSpace(Kayit.KlasorYolu) && Directory.Exists(Kayit.KlasorYolu))
        {
            string[] uzantilar = [".mp4", ".mkv", ".avi", ".mov", ".webm"];
            foreach (var uzt in uzantilar)
            {
                string aday = Path.Combine(Kayit.KlasorYolu, Kayit.Ad + uzt);
                if (File.Exists(aday))
                {
                    return aday;
                }
            }

            try
            {
                var adaylar = Directory.GetFiles(Kayit.KlasorYolu)
                    .Where(f => uzantilar.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                    .ToList();
                if (adaylar.Count > 0)
                {
                    return adaylar[0];
                }
            }
            catch
            {
                // Yut
            }
        }

        return null;
    }

    /// <summary>
    /// 250 ms DispatcherTimer senkronizasyonu: zaman çubuğu, zaman metni ve aktif segment takibi.
    /// </summary>
    private void Zamanlayici_Tick(object? sender, EventArgs e)
    {
        if (_kapatildi) return;

        double anlikSn = _videoVar ? _oynatici.Zaman : Kontrol.AramaZamani;
        double toplamSn = _videoVar ? _oynatici.ToplamSure : 0;
        if (toplamSn > 0)
        {
            _toplamSureSn = toplamSn;
        }

        if (!_videoVar && !Kontrol.AramaSurukleniyor)
        {
            Kontrol.ZamanBilgisiniGuncelle(anlikSn, toplamSn);
        }

        // Aktif segmenti bul ve vurgula
        var aktif = OynaticiYardimcisi.AktifSegmentBul(_efektifSegmentler, anlikSn);
        if (aktif != _oncekiAktifSegment)
        {
            _oncekiAktifSegment = aktif;
            TranskriptSatirGorunumu? aktifGorunum = null;

            foreach (var oge in _goruntulenenSegmentler)
            {
                bool eslesiyor = aktif != null && oge.Segment.No == aktif.No;
                oge.Vurgulu = eslesiyor;
                if (eslesiyor)
                {
                    aktifGorunum = oge;
                }
            }

            if (aktifGorunum != null && OtoKaydirKutusu.IsChecked == true)
            {
                TranskriptListesi.ScrollIntoView(aktifGorunum);
            }
        }
    }

    /// <summary>
    /// Transkript panel içi arama kutusuna göre segmentleri filtreler.
    /// </summary>
    private void TranskriptAramaKutusu_TextChanged(object sender, TextChangedEventArgs e)
    {
        FiltreyiUygula();
    }

    private void FiltreyiUygula()
    {
        string arama = TranskriptAramaKutusu.Text?.Trim() ?? string.Empty;
        var filtrelenmis = OynaticiYardimcisi.SegmentFiltrele(_efektifSegmentler, arama);

        _goruntulenenSegmentler.Clear();
        foreach (var seg in filtrelenmis)
        {
            var vm = new TranskriptSatirGorunumu(seg)
            {
                Vurgulu = _oncekiAktifSegment != null && seg.No == _oncekiAktifSegment.No
            };
            _goruntulenenSegmentler.Add(vm);
        }

        TranskriptDurumMetni.Text = string.IsNullOrWhiteSpace(arama)
            ? $"{_goruntulenenSegmentler.Count} segment"
            : $"{_goruntulenenSegmentler.Count} / {_efektifSegmentler.Count} segment bulundu";
    }

    private void OynatDuraklat()
    {
        if (!_videoVar) return;

        Kontrol.Oynatici?.OynatDuraklat();
    }

    private void ZamanAtla(double deltaSn)
    {
        if (!_videoVar || Kontrol.Oynatici is not { } oynatici) return;

        double simdi = oynatici.Zaman;
        double azami = _toplamSureSn > 0 ? _toplamSureSn : oynatici.ToplamSure;
        double hedef = OynaticiYardimcisi.ZamanAtla(simdi, deltaSn, azami);
        oynatici.Ara(hedef);
    }

    /// <summary>
    /// Transkript satırına tıklandığında ilgili saniyeye atlar (tıklama = atla).
    /// </summary>
    private void TranskriptListesi_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        SegmenteAtla();
    }

    private void TranskriptListesi_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_yoklamaGuncelleniyor || _programatikSecimYapiliyor) return;
        SegmenteAtla();
    }

    private void ProgramatikSecimYap(TranskriptSatirGorunumu secim)
    {
        _programatikSecimYapiliyor = true;
        try
        {
            TranskriptListesi.SelectedItem = secim;
        }
        finally
        {
            _programatikSecimYapiliyor = false;
        }
    }

    private void TranskriptListesi_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SegmenteAtla();
            e.Handled = true;
        }
    }

    private void SegmenteAtla()
    {
        if (TranskriptListesi.SelectedItem is TranskriptSatirGorunumu satir)
        {
            if (_duzenlemeAcik)
            {
                DuzenlemeKontrolu.SegmentYukle(satir.Segment, Kayit.Ad, _hamTranskript?.KaynakOzet);
            }

            if (_videoVar)
            {
                Kontrol.Oynatici?.Ara(satir.Segment.Bas);
            }
            else
            {
                Kontrol.ZamanBilgisiniGuncelle(
                    Math.Clamp(satir.Segment.Bas, 0, Kontrol.AramaAzami),
                    Kontrol.AramaAzami);
            }
        }
    }

    private void TamEkranGecis()
    {
        if (_tamEkran)
        {
            TamEkrandanCik();
        }
        else
        {
            TamEkranModunaGec();
        }
    }

    private void TamEkranModunaGec()
    {
        if (_tamEkran) return;
        PencereYerlesimi.TamEkraniAyarla(this, true);
        _tamEkran = PencereYerlesimi.GetTamEkran(this);
        if (!_tamEkran) return;
        _tamEkranOncekiSpu = _oynatici.MediaPlayer.Spu;
        _tamEkranSrtParcasiSecildi = false;

        Kontrol.Visibility = Visibility.Visible;
        Kontrol.TamEkranDurumunuGuncelle(true);
        TamEkranKontrolleriniGizlemeZamanlayicisiniBaslat();

        // Tam ekranda efektif metinden geçici .srt üretip seçili altyazı olarak ekle
        AltyaziyiTamEkranaYukle();
    }

    private void TamEkrandanCik()
    {
        if (!_tamEkran) return;
        _tamEkran = false;

        PencereYerlesimi.TamEkraniAyarla(this, false);

        _tamEkranKontrolZamanlayici.Stop();
        Kontrol.Visibility = Visibility.Visible;
        Kontrol.TamEkranDurumunuGuncelle(false);

        if (_tamEkranSrtParcasiSecildi &&
            _oynatici.MediaPlayer.Spu == _ekliGeciciSrtParcaId)
        {
            int geriYuklenecekSpu = _tamEkranOncekiSpu == _ekliGeciciSrtParcaId
                ? -1
                : _tamEkranOncekiSpu;
            _oynatici.MediaPlayer.SetSpu(geriYuklenecekSpu);
        }
        _tamEkranSrtParcasiSecildi = false;
    }

    /// <summary>
    /// Tam ekranda efektif metinden geçici .srt üretip seçili altyazı olarak ekler.
    /// </summary>
    private void AltyaziyiTamEkranaYukle()
    {
        try
        {
            if (_oynatici.Medya != null && _efektifSegmentler.Count > 0)
            {
                if (string.IsNullOrEmpty(_geciciSrtYolu) || !File.Exists(_geciciSrtYolu))
                {
                    _geciciSrtYolu = SrtUretici.GeciciSrtDosyasiOlustur(_efektifSegmentler);
                }

                if (string.Equals(_geciciSrtYolu, _ekliGeciciSrtYolu, StringComparison.OrdinalIgnoreCase))
                {
                    if (_ekliGeciciSrtParcaId >= 0)
                    {
                        _oynatici.MediaPlayer.SetSpu(_ekliGeciciSrtParcaId);
                        _tamEkranSrtParcasiSecildi = true;
                    }
                    return;
                }

                if (_tamEkran &&
                    (!_tamEkranSrtParcasiSecildi || _oynatici.MediaPlayer.Spu != _ekliGeciciSrtParcaId))
                {
                    _tamEkranOncekiSpu = _oynatici.MediaPlayer.Spu;
                }

                var srtUri = new Uri(_geciciSrtYolu).AbsoluteUri;
                if (_oynatici.MediaPlayer.AddSlave(MediaSlaveType.Subtitle, srtUri, true))
                {
                    _ekliGeciciSrtYolu = _geciciSrtYolu;
                    _ekliGeciciSrtParcaId = _oynatici.MediaPlayer.Spu;
                    _tamEkranSrtParcasiSecildi = true;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Altyazı ekleme hatası: {ex.Message}");
        }
    }

    private void GeciciSrtYolunuSifirla()
    {
        string? eskiYol = _geciciSrtYolu;
        _geciciSrtYolu = null;
        if (string.IsNullOrEmpty(eskiYol)) return;

        try
        {
            if (File.Exists(eskiYol))
            {
                File.Delete(eskiYol);
            }

        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Eski altyazı dosyası silinemedi: {ex.Message}");
        }
    }

    private void VideoAlani_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1)
        {
            TamEkranGecis();
        }
        else
        {
            OynatDuraklat();
        }

        e.Handled = true;
    }

    private void VideoAlani_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Kontrol.Oynatici is { } oynatici)
        {
            oynatici.SesSeviyesi = Math.Clamp(oynatici.SesSeviyesi + Math.Sign(e.Delta) * 5, 0, 100);
        }
        e.Handled = true;
    }

    private void Pencere_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_tamEkran && Kontrol.Visibility != Visibility.Visible)
        {
            Kontrol.Visibility = Visibility.Visible;
        }

        if (_tamEkran)
        {
            TamEkranKontrolleriniGizlemeZamanlayicisiniBaslat();
        }
    }

    private void TamEkranKontrolleriniGizlemeZamanlayicisiniBaslat()
    {
        _tamEkranKontrolZamanlayici.Stop();
        if (_tamEkran)
        {
            _tamEkranKontrolZamanlayici.Start();
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        bool yaziyor = Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase;
        if (e.Key == Key.F11)
        {
            TamEkranGecis();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (_tamEkran)
            {
                TamEkrandanCik();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Space)
        {
            if (!yaziyor)
            {
                OynatDuraklat();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Left)
        {
            if (!yaziyor)
            {
                ZamanAtla(-10.0);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Right)
        {
            if (!yaziyor)
            {
                ZamanAtla(10.0);
                e.Handled = true;
            }
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase ||
            Keyboard.FocusedElement is Slider && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            return;
        }

        if (Kontrol.KisayolIsle(e.Key))
        {
            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_kapatildi) return;
        _kapatildi = true;

        _zamanlayici.Stop();
        _yoklamaZamanlayici.Stop();
        _tamEkranKontrolZamanlayici.Stop();
        _oynatici.MediaPlayer.EncounteredError -= OnMediaPlayerEncounteredError;
        _oynatici.MediaPlayer.LengthChanged -= OnMediaPlayerLengthChanged;

        try
        {
            VideoGosterici.MediaPlayer = null;
        }
        catch
        {
            // Yut
        }

        _oynatici.Dispose();
        GeciciSrtTemizle();
    }

    private void GeciciSrtTemizle()
    {
        try
        {
            if (!string.IsNullOrEmpty(_geciciSrtYolu) && File.Exists(_geciciSrtYolu))
            {
                File.Delete(_geciciSrtYolu);
                _geciciSrtYolu = null;
            }
        }
        catch
        {
            // Temizlik hatasını yut
        }
    }

    /// <summary>
    /// Düzenle düğmesine tıklandığında parola kontrolü yapar ve paneli açıp kapatır.
    /// </summary>
    private void Duzenle_Click(object sender, RoutedEventArgs e)
    {
        if (_duzenlemeAcik)
        {
            _duzenlemeAcik = false;
            DuzenlemeKontrolu.Visibility = Visibility.Collapsed;
            DuzenleDugmesi.Content = "Düzenle";
            return;
        }

        if (!DuzenlemeKontrolu.Dogrula(this, ref _oturumParolaDogrulandi, _parolaSayaci))
        {
            return;
        }

        _duzenlemeAcik = true;
        DuzenlemeKontrolu.Visibility = Visibility.Visible;
        DuzenleDugmesi.Content = "Düzenlemeyi Kapat";

        if (TranskriptListesi.SelectedItem is TranskriptSatirGorunumu secili)
        {
            DuzenlemeKontrolu.SegmentYukle(secili.Segment, Kayit.Ad, _hamTranskript?.KaynakOzet);
        }
        else if (_goruntulenenSegmentler.Count > 0)
        {
            ProgramatikSecimYap(_goruntulenenSegmentler[0]);
            DuzenlemeKontrolu.SegmentYukle(_goruntulenenSegmentler[0].Segment, Kayit.Ad, _hamTranskript?.KaynakOzet);
        }
    }

    /// <summary>
    /// Düzeltme kaydedildiğinde transkripti yeniler ve seçili segmenti günceller.
    /// </summary>
    private void OnDuzenlemeKaydedildi(EfektifSegment seg)
    {
        TranskriptiYukle();
        if (_tamEkran)
        {
            GeciciSrtYolunuSifirla();
            AltyaziyiTamEkranaYukle();
        }

        var guncel = _goruntulenenSegmentler.FirstOrDefault(s => s.Segment.No == seg.No);
        if (guncel != null)
        {
            ProgramatikSecimYap(guncel);
            DuzenlemeKontrolu.SegmentYukle(guncel.Segment, Kayit.Ad, _hamTranskript?.KaynakOzet);
        }
    }

    /// <summary>
    /// 30 sn aralıklarla düzeltmeler klasörünü yoklar; değişiklik varsa transkript ve aramayı yeniler.
    /// </summary>
    private void YoklamaZamanlayici_Tick(object? sender, EventArgs e)
    {
        if (_kapatildi) return;
        YoklaVeGuncelle();
    }

    private void YoklaVeGuncelle()
    {
        _yoklamaGuncelleniyor = true;
        try
        {
            string duzeltmeKlasoru = DuzenlemeKontrolu.DuzeltmelerKlasoruBul();
            if (!Directory.Exists(duzeltmeKlasoru))
            {
                if (_sonDuzeltmeSayisi > 0)
                {
                    _sonKlasorMtimeUtc = DateTime.MinValue;
                    _sonDuzeltmeSayisi = 0;
                    TranskriptiYukle();
                    DersiYenileMotoru();
                }
                return;
            }

            var di = new DirectoryInfo(duzeltmeKlasoru);
            var files = di.GetFiles("*.json");
            DateTime sonMtime = di.LastWriteTimeUtc;
            foreach (var f in files)
            {
                if (f.LastWriteTimeUtc > sonMtime) sonMtime = f.LastWriteTimeUtc;
            }

            bool degisti = (_sonDuzeltmeSayisi >= 0 && files.Length != _sonDuzeltmeSayisi) ||
                           (sonMtime > _sonKlasorMtimeUtc);

            if (degisti)
            {
                _sonKlasorMtimeUtc = sonMtime;
                _sonDuzeltmeSayisi = files.Length;

                int seciliNo = (TranskriptListesi.SelectedItem is TranskriptSatirGorunumu oncekiSecili)
                    ? oncekiSecili.Segment.No
                    : (DuzenlemeKontrolu.SeciliSegment?.No ?? -1);

                TranskriptiYukle();
                DersiYenileMotoru();

                if (seciliNo >= 0)
                {
                    var guncel = _goruntulenenSegmentler.FirstOrDefault(s => s.Segment.No == seciliNo);
                    if (guncel != null)
                    {
                        ProgramatikSecimYap(guncel);
                        if (_duzenlemeAcik)
                        {
                            bool taslakVar = DuzenlemeKontrolu.TaslakVarMi;
                            DuzenlemeKontrolu.SegmentYukle(guncel.Segment, Kayit.Ad, _hamTranskript?.KaynakOzet, taslagiKoru: taslakVar);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            TranskriptDurumMetni.Text = "Düzeltmeler yenilenemedi: " + ex.Message;
        }
        finally
        {
            _yoklamaGuncelleniyor = false;
        }
    }

    private void IlkMtimeBelirle(string duzeltmeKlasoru)
    {
        try
        {
            if (Directory.Exists(duzeltmeKlasoru))
            {
                var di = new DirectoryInfo(duzeltmeKlasoru);
                var files = di.GetFiles("*.json");
                DateTime sonMtime = di.LastWriteTimeUtc;
                foreach (var f in files)
                {
                    if (f.LastWriteTimeUtc > sonMtime) sonMtime = f.LastWriteTimeUtc;
                }
                _sonKlasorMtimeUtc = sonMtime;
                _sonDuzeltmeSayisi = files.Length;
            }
            else
            {
                _sonKlasorMtimeUtc = DateTime.MinValue;
                _sonDuzeltmeSayisi = 0;
            }
        }
        catch (Exception ex)
        {
            TranskriptDurumMetni.Text = "Düzeltme klasörü okunamadı: " + ex.Message;
        }
    }

    private void DersiYenileMotoru()
    {
        try
        {
            var kayit = DuzenlemeKontrolu.Ders ?? Kayit;
            if (_aramaMotoru != null)
            {
                _aramaMotoru.DersiYenile(kayit);
            }
            else
            {
                string kok = !string.IsNullOrEmpty(_kokKlasor) ? _kokKlasor : kayit.KlasorYolu;
                using var motor = new MetinAramaMotoru(new IndeksOnbellek(), new DuzeltmeDeposu(kok));
                motor.DersiYenile(kayit);
            }
        }
        catch (Exception ex)
        {
            TranskriptDurumMetni.Text = "Arama indeksi yenilenemedi: " + ex.Message;
        }
    }

    private List<Portal.Cekirdek.Dizin.DosyaSonucu> _dersBelgeleri = [];

    private async void BelgeleriYukleAsync()
    {
        if (Dizin == null || Dizin.Durum != Portal.Cekirdek.Dizin.DizinDurumu.Hazir || string.IsNullOrWhiteSpace(Kayit.KlasorYolu))
        {
            BelgelerDugmesi.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            var motor = new Portal.Cekirdek.Dizin.DosyaAramaMotoru(Dizin);
            var belgeler = await Task.Run(() => motor.DersBelgeleri(Kayit.KlasorYolu));
            _dersBelgeleri = belgeler.ToList();
            if (_dersBelgeleri.Count > 0)
            {
                BelgelerDugmesi.Content = $"Belgeler ({_dersBelgeleri.Count})";
                BelgelerDugmesi.Visibility = Visibility.Visible;
            }
            else
            {
                BelgelerDugmesi.Visibility = Visibility.Collapsed;
            }
        }
        catch
        {
            BelgelerDugmesi.Visibility = Visibility.Collapsed;
        }
    }

    private void BelgelerDugmesi_Click(object sender, RoutedEventArgs e)
    {
        if (_dersBelgeleri.Count == 0 || Dizin == null) return;

        var menu = new ContextMenu();
        foreach (var belge in _dersBelgeleri)
        {
            var item = new MenuItem { Header = belge.Ad, Tag = belge };
            item.Click += (s, ev) =>
            {
                if (item.Tag is Portal.Cekirdek.Dizin.DosyaSonucu d && Dizin != null)
                {
                    var pencere = new BelgePenceresi(Dizin, d.Id, 1, null)
                    {
                        Owner = this
                    };
                    pencere.Show();
                }
            };
            menu.Items.Add(item);
        }
        menu.PlacementTarget = BelgelerDugmesi;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }
}

/// <summary>
/// Transkript listesinde gösterilen satır görünüm modeli.
/// </summary>
public sealed class TranskriptSatirGorunumu : INotifyPropertyChanged
{
    private bool _vurgulu;

    public EfektifSegment Segment { get; }
    public string ZamanMetni => ZamanBicim.Bicimle(Segment.Bas);
    public string Metin => Segment.Metin;
    public bool Cakisma => Segment.Cakisma;

    public bool Vurgulu
    {
        get => _vurgulu;
        set
        {
            if (_vurgulu != value)
            {
                _vurgulu = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Vurgulu)));
            }
        }
    }

    public TranskriptSatirGorunumu(EfektifSegment segment)
    {
        Segment = segment;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
