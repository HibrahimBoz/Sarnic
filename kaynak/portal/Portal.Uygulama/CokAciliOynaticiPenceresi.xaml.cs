using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using Microsoft.Win32;
using Portal.Cekirdek;
using Portal.Uygulama.Kontroller;
using Portal.Uygulama.Temalar;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace Portal.Uygulama;

public partial class CokAciliOynaticiPenceresi : Window
{
    private readonly LibVLC _libVlc;
    private readonly CokAciliSaat _saat;
    private readonly DispatcherTimer _zamanlayici;
    private readonly Dictionary<string, KanalGorunumu> _kanallar;
    private readonly ObservableCollection<TranskriptSatiri> _transkript = [];
    private readonly double _sureSn;
    private readonly string _klasor;
    private readonly string? _transkriptYolu;
    private GorunumModu _gorunum = new GorunumModu.Izgara();
    private bool _kapatiliyor;
    private readonly ZamanKaydiraciDavranisi _aramaDavranisi;
    private readonly ParolaGecikmeSayaci _parolaSayaci = ParolaGecikmeSayaci.Paylasilan;
    private bool _programatikSecimYapiliyor;
    private readonly DispatcherTimer _yoklamaZamanlayici;
    private bool _duzenlemeAcik;
    private bool _parolaDogrulandi;
    private bool _yoklamaGuncelleniyor;
    private DateTime _sonKlasorMtimeUtc = DateTime.MinValue;
    private int _sonDuzeltmeSayisi = -1;
    private Transkript? _hamTranskript;
    private DersKaydi? _dersKaydi;
    private MetinAramaMotoru? _aramaMotoru;
    private bool _ceviriGoster = KokYapilandirma.CeviriGosterOku();

    public CokAciliOynaticiPenceresi(DersTanimi ders, double baslangicSanalSn = 0)
        : this(ders, baslangicSanalSn, null, null)
    {
    }

    public CokAciliOynaticiPenceresi(DersTanimi ders, double baslangicSanalSn, string? transkriptYolu)
        : this(ders, baslangicSanalSn, transkriptYolu, null)
    {
    }

    public CokAciliOynaticiPenceresi(DersTanimi ders, double baslangicSanalSn, string? transkriptYolu, MetinAramaMotoru? aramaMotoru)
    {
        ArgumentNullException.ThrowIfNull(ders);
        _aramaMotoru = aramaMotoru;
        InitializeComponent();
        Title = !string.IsNullOrWhiteSpace(ders.Ad) ? $"{UrunAdi.Etkin} — Oynatıcı — {ders.Ad}" : $"{UrunAdi.Etkin} — Oynatıcı";
        _libVlc = LibVlcAyarlari.Paylasilan;
        _saat = new CokAciliSaat(ders);
        _saat.Ara(baslangicSanalSn);
        _sureSn = double.IsFinite(ders.ToplamSureSn) ? Math.Max(0, ders.ToplamSureSn) : 0;
        ZamanKaydiraci.Maximum = _sureSn > 0 ? _sureSn : 1;
        _aramaDavranisi = new ZamanKaydiraciDavranisi(ZamanKaydiraci, () => _sureSn, ZamanKaydir);
        _klasor = Path.GetDirectoryName(ders.DosyaYolu ?? string.Empty) ?? string.Empty;
        _transkriptYolu = transkriptYolu;
        _dersKaydi = new DersKaydi(ders.Ad, _klasor, _transkriptYolu ?? string.Empty, null);
        _kanallar = new Dictionary<string, KanalGorunumu>(StringComparer.OrdinalIgnoreCase);

        DuzenlemeKontrolu.Ders = _dersKaydi;
        DuzenlemeKontrolu.KokKlasor = _klasor;
        DuzenlemeKontrolu.DuzeltmelerKlasoru = Path.Combine(_klasor, $"{ders.Ad}.duzeltmeler");
        DuzenlemeKontrolu.AramaMotoru = _aramaMotoru;
        DuzenlemeKontrolu.DuzenlemeKaydedildi += OnDuzenlemeKaydedildi;

        _yoklamaZamanlayici = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _yoklamaZamanlayici.Tick += YoklamaZamanlayici_Tick;
        _yoklamaZamanlayici.Start();

        foreach (var kanal in ders.Kanallar)
        {
            var player = new MediaPlayer(_libVlc)
            {
                Mute = !string.Equals(kanal.Ad, _saat.AktifSesKanali, StringComparison.OrdinalIgnoreCase)
            };
            var video = new VideoView { MediaPlayer = player };
            var hata = new TextBlock
            {
                Text = "kayıt yok",
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(74, 78, 86)),
                FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                TextAlignment = TextAlignment.Center,
                Padding = new Thickness(12),
                Visibility = Visibility.Collapsed
            };
            var etiket = new TextBlock
            {
                Text = kanal.Ad + (kanal.Guvenilir ? string.Empty : "  ·  senkron güvenilmez"),
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(190, 20, 22, 26)),
                Padding = new Thickness(8, 4, 8, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false
            };
            var tile = new Grid { Background = Brushes.Black, ClipToBounds = true };
            tile.Children.Add(video);
            tile.Children.Add(hata);
            tile.Children.Add(etiket);
            var state = new KanalGorunumu(kanal, player, video, tile, hata, etiket);
            player.EncounteredError += (_, _) => Dispatcher.InvokeAsync(() =>
            {
                state.HataVar = true;
                state.HataDosyasi = state.AktifAsilDosya ?? state.AktifDosya;
                state.BekleyenKonumSn = null;
                KanaliGriYap(state, "Oynatıcı hatası");
            });
            player.EndReached += (_, _) => Dispatcher.InvokeAsync(() => KanalBitisiniIsle(state));
            player.Playing += (_, _) => Dispatcher.InvokeAsync(() =>
            {
                if (!_kapatiliyor) BekleyenKonumuUygula(state);
            });
            _kanallar.Add(kanal.Ad, state);
            SesKanaliSecici.Items.Add(kanal.Ad);
        }

        SesKanaliSecici.SelectedItem = _saat.AktifSesKanali;
        TranskriptListesi.ItemsSource = _transkript;
        TranskriptiYukle(ders, _transkriptYolu);
        GorunumuUygula();
        TumKanallariGuncelle(true);

        _zamanlayici = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _zamanlayici.Tick += (_, _) => ZamanlayiciTiki();
        _zamanlayici.Start();
        if (_saat.Oynatiliyor)
        {
            OynatDurdurDugmesi.Content = "\uE769";
        }
    }

    public CokAciliOynaticiPenceresi(DersKaydi kayit, double baslangicSanalSn = 0)
        : this(kayit, baslangicSanalSn, null)
    {
    }

    public CokAciliOynaticiPenceresi(DersKaydi kayit, double baslangicSanalSn, MetinAramaMotoru? aramaMotoru)
        : this(DersKaydindanTanima(kayit), baslangicSanalSn, kayit.JsonYolu, aramaMotoru)
    {
        _dersKaydi = kayit;
        DuzenlemeKontrolu.Ders = _dersKaydi;
    }

    /// <summary>
    /// Arama akışından veya taranan ders kaydından pencereyi açar.
    /// Ders klasöründe ders.json varsa çok açılı oynatıcı penceresi açılır; yoksa tekli oynatıcı açılır.
    /// </summary>
    public static Window Ac(DersKaydi kayit, double baslangicSanalSn = 0, MetinAramaMotoru? aramaMotoru = null,
        bool baslangictaCeviri = false)
    {
        ArgumentNullException.ThrowIfNull(kayit);
        string dersJson = Path.Combine(kayit.KlasorYolu, "ders.json");
        if (File.Exists(dersJson))
        {
            var ders = DersTanimiOkuyucu.Oku(dersJson);
            var pencere = new CokAciliOynaticiPenceresi(ders, baslangicSanalSn, kayit.JsonYolu, aramaMotoru);
            if (baslangictaCeviri)
            {
                pencere.CeviriGorunumuneGec();
            }

            pencere.Show();
            return pencere;
        }

        // ders.json yoksa tek video modunda oynatıcı açılır
        var tekliPencere = new OynaticiPenceresi(kayit, baslangicSanalSn, aramaMotoru, baslangictaCeviri);
        tekliPencere.Show();
        return tekliPencere;
    }

    private static DersTanimi DersKaydindanTanima(DersKaydi kayit)
    {
        ArgumentNullException.ThrowIfNull(kayit);
        string dersJson = Path.Combine(kayit.KlasorYolu, "ders.json");
        if (File.Exists(dersJson))
        {
            return DersTanimiOkuyucu.Oku(dersJson);
        }

        var kanal = new Kanal(
            kayit.Ad,
            0.0,
            0.0,
            true,
            kayit.VideoYolu != null
                ? [new ParcaTanimi(kayit.VideoYolu, 0.0, 86400.0, 0.0, "video")]
                : []
        );
        return new DersTanimi(
            1,
            kayit.Ad,
            kayit.Ad,
            [kanal],
            new SenkronBilgisi("tekli", 0.0, 1.0, true),
            Array.Empty<ElleIsaret>(),
            kayit.JsonYolu
        );
    }

    private void TranskriptiYukle(DersTanimi ders, string? transkriptYolu)
    {
        string? json = null;

        // 1. Ders kaydının belirttiği veya doğrudan iletilen transkript yolu
        if (!string.IsNullOrWhiteSpace(transkriptYolu) && File.Exists(transkriptYolu))
        {
            json = transkriptYolu;
        }

        // 2. Ders adı ile eşleşen veya klasör adından türetilen JSON dosyası
        if (json == null && !string.IsNullOrWhiteSpace(_klasor) && Directory.Exists(_klasor))
        {
            string adayAdJson = Path.Combine(_klasor, ders.Ad + ".json");
            if (File.Exists(adayAdJson))
            {
                json = adayAdJson;
            }
            else
            {
                string klasorAdi = Path.GetFileName(_klasor);
                string ek = klasorAdi.EndsWith("_islenmis", StringComparison.OrdinalIgnoreCase)
                    ? "_islenmis"
                    : klasorAdi.EndsWith("_videoperisi", StringComparison.OrdinalIgnoreCase)
                        ? "_videoperisi"
                        : string.Empty;
                if (ek.Length > 0)
                {
                    string beklenen = klasorAdi.Substring(0, klasorAdi.Length - ek.Length);
                    string adayVpJson = Path.Combine(_klasor, beklenen + ".json");
                    if (File.Exists(adayVpJson))
                    {
                        json = adayVpJson;
                    }
                }
            }
        }

        // 3. Klasördeki diğer JSON'lar arasından transkript formatına uyanı ara
        if (json == null && !string.IsNullOrWhiteSpace(_klasor) && Directory.Exists(_klasor))
        {
            try
            {
                var adaylar = Directory.GetFiles(_klasor, "*.json")
                    .Where(yol =>
                    {
                        string dosyaAdi = Path.GetFileName(yol);
                        return !string.Equals(dosyaAdi, "ders.json", StringComparison.OrdinalIgnoreCase) &&
                               !string.Equals(dosyaAdi, "parola.json", StringComparison.OrdinalIgnoreCase) &&
                               !string.Equals(dosyaAdi, "ayarlar.json", StringComparison.OrdinalIgnoreCase) &&
                               !string.Equals(dosyaAdi, "elle-ofsetler.json", StringComparison.OrdinalIgnoreCase);
                    });

                foreach (var aday in adaylar)
                {
                    try
                    {
                        var t = TranskriptOkuyucu.Oku(aday);
                        if (t.Segmentler.Count > 0)
                        {
                            json = aday;
                            break;
                        }
                    }
                    catch
                    {
                        // Geçersiz adayları atla
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                TranskriptDurumu.Text = "Transkript dosyaları listelenemedi: " + ex.Message;
                return;
            }
        }

        if (json == null)
        {
            TranskriptDurumu.Text = "Bu ders için transkript bulunamadı.";
            return;
        }

        try
        {
            var transkript = TranskriptOkuyucu.Oku(json);
            _hamTranskript = transkript;
            _dersKaydi = new DersKaydi(ders.Ad, _klasor, json, null);
            DuzenlemeKontrolu.Ders = _dersKaydi;
            TranskriptiGuncelle();
            IlkMtimeBelirle(DuzenlemeKontrolu.DuzeltmelerKlasoruBul());
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            TranskriptDurumu.Text = "Transkript okunamadı: " + ex.Message;
        }
    }

    private void TranskriptiGuncelle()
    {
        if (_hamTranskript == null) return;
        _transkript.Clear();
        string duzeltmeKlasoru = DuzenlemeKontrolu.DuzeltmelerKlasoruBul();
        var depo = new DuzeltmeDeposu(duzeltmeKlasoru);
        var birlestirme = depo.Birlestir(_hamTranskript, duzeltmeKlasoru);
        bool turkce = _ceviriGoster && _hamTranskript.CeviriVar;
        foreach (var ef in birlestirme.Segmentler)
        {
            string rozet = ef.Cakisma ? "  [ÇAKIŞMA]" : string.Empty;
            string metin = turkce && !string.IsNullOrWhiteSpace(ef.Ceviri) ? ef.Ceviri : ef.Metin;
            _transkript.Add(new TranskriptSatiri(ef, ZamanBicimle(ef.Bas) + "  " + metin + rozet));
        }
        TranskriptDurumu.Text = _transkript.Count == 0 ? "Transkriptte gösterilecek segment yok." : string.Empty;
        CeviriArayuzunuGuncelle();
    }

    /// <summary>
    /// Arama sonucu çeviriden eşleştiyse pencere Türkçe görünümde açılır (kullanıcı ayarı değişmez).
    /// </summary>
    public void CeviriGorunumuneGec()
    {
        _ceviriGoster = true;
        TranskriptiGuncelle();
    }

    private void CeviriArayuzunuGuncelle()
    {
        bool ceviriVar = _hamTranskript?.CeviriVar == true;
        CeviriGecisDugmesi.Visibility = ceviriVar ? Visibility.Visible : Visibility.Collapsed;
        CeviriGecisDugmesi.IsChecked = _ceviriGoster;
        CeviriUyariMetni.Visibility = ceviriVar && _ceviriGoster ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CeviriGecisDugmesi_Click(object sender, RoutedEventArgs e)
    {
        _ceviriGoster = CeviriGecisDugmesi.IsChecked == true;
        KokYapilandirma.CeviriGosterKaydet(_ceviriGoster);
        TranskriptiGuncelle();
    }

    private void Duzenle_Click(object sender, RoutedEventArgs e)
    {
        if (_duzenlemeAcik)
        {
            _duzenlemeAcik = false;
            DuzenlemeKontrolu.Visibility = Visibility.Collapsed;
            DuzenleDugmesi.Content = "Düzenle";
            return;
        }

        if (!DuzenlemeKontrolu.Dogrula(this, ref _parolaDogrulandi, _parolaSayaci))
        {
            return;
        }

        _duzenlemeAcik = true;
        DuzenlemeKontrolu.Visibility = Visibility.Visible;
        DuzenleDugmesi.Content = "Düzenlemeyi Kapat";

        if (TranskriptListesi.SelectedItem is TranskriptSatiri secili && secili.Segment is EfektifSegment ef)
        {
            DuzenlemeKontrolu.SegmentYukle(ef, _saat.Ders.Ad, _hamTranskript?.KaynakOzet);
        }
        else if (_transkript.Count > 0 && _transkript[0].Segment is EfektifSegment ilkEf)
        {
            TranskriptListesi.SelectedItem = _transkript[0];
            DuzenlemeKontrolu.SegmentYukle(ilkEf, _saat.Ders.Ad, _hamTranskript?.KaynakOzet);
        }
    }

    private void OnDuzenlemeKaydedildi(EfektifSegment seg)
    {
        TranskriptiGuncelle();

        var guncel = _transkript.FirstOrDefault(s => s.Segment.No == seg.No);
        if (guncel != null)
        {
            _programatikSecimYapiliyor = true;
            try
            {
                TranskriptListesi.SelectedItem = guncel;
            }
            finally
            {
                _programatikSecimYapiliyor = false;
            }
            if (guncel.Segment is EfektifSegment ef)
            {
                DuzenlemeKontrolu.SegmentYukle(ef, _saat.Ders.Ad, _hamTranskript?.KaynakOzet);
            }
        }
    }

    private void YoklamaZamanlayici_Tick(object? sender, EventArgs e)
    {
        if (_kapatiliyor) return;
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
                    TranskriptiGuncelle();
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

                int seciliNo = (TranskriptListesi.SelectedItem is TranskriptSatiri oncekiSecili)
                    ? oncekiSecili.Segment.No
                    : (DuzenlemeKontrolu.SeciliSegment?.No ?? -1);

                TranskriptiGuncelle();
                DersiYenileMotoru();

                if (seciliNo >= 0)
                {
                    var guncel = _transkript.FirstOrDefault(s => s.Segment.No == seciliNo);
                    if (guncel != null)
                    {
                        TranskriptListesi.SelectedItem = guncel;
                        if (_duzenlemeAcik && guncel.Segment is EfektifSegment ef)
                        {
                            bool taslakVar = DuzenlemeKontrolu.TaslakVarMi;
                            DuzenlemeKontrolu.SegmentYukle(ef, _saat.Ders.Ad, _hamTranskript?.KaynakOzet, taslagiKoru: taslakVar);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            TranskriptDurumu.Text = "Düzeltmeler yenilenemedi: " + ex.Message;
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
            TranskriptDurumu.Text = "Düzeltme klasörü okunamadı: " + ex.Message;
        }
    }

    private void DersiYenileMotoru()
    {
        try
        {
            var kayit = _dersKaydi ?? DuzenlemeKontrolu.Ders;
            if (kayit != null)
            {
                if (_aramaMotoru != null)
                {
                    _aramaMotoru.DersiYenile(kayit);
                }
                else
                {
                    using var motor = new MetinAramaMotoru(new IndeksOnbellek(), new DuzeltmeDeposu(_klasor));
                    motor.DersiYenile(kayit);
                }
            }
        }
        catch (Exception ex)
        {
            TranskriptDurumu.Text = "Arama indeksi yenilenemedi: " + ex.Message;
        }
    }

    private void ZamanlayiciTiki()
    {
        if (_kapatiliyor) return;
        TumKanallariGuncelle(false);
        double simdi = _saat.Simdi();
        if (!_aramaDavranisi.Surukleniyor)
        {
            ZamanKaydiraci.Value = Math.Clamp(simdi, ZamanKaydiraci.Minimum, Math.Max(ZamanKaydiraci.Minimum, _sureSn));
        }
        ZamanMetni.Text = $"{ZamanBicimle(simdi)} / {ZamanBicimle(_sureSn)}";
    }

    private void TumKanallariGuncelle(bool zorla)
    {
        foreach (var state in _kanallar.Values)
        {
            TekKanaliGuncelle(state, zorla);
        }
    }

    private void TekKanaliGuncelle(KanalGorunumu state, bool zorla)
    {
        double sanalSn = _saat.Simdi();
        try
        {
            var (dosya, hedefYerelSn, kayitVar) = state.Kanal.SanalDenYerele(sanalSn, _saat.ElleOfsetAl(state.Kanal.Ad));
            if (!kayitVar || string.IsNullOrWhiteSpace(dosya) || !File.Exists(dosya))
            {
                HazirlaSonrakiParca(state, sanalSn);
                KanaliGriYap(state, kayitVar ? "Video dosyası bulunamadı" : "kayıt yok");
                return;
            }

            var (kaynak, onizlemeMi, onizlemeYerelBas) = VideoKaynaginiSec(state, dosya);
            if (state.HataVar)
            {
                if (string.Equals(state.HataDosyasi, dosya, StringComparison.OrdinalIgnoreCase))
                {
                    KanaliGriYap(state, "Oynatıcı hatası");
                    return;
                }
                state.HataVar = false;
                state.HataDosyasi = null;
            }

            bool parcaDegisti = !string.Equals(state.AktifAsilDosya, dosya, StringComparison.OrdinalIgnoreCase);
            bool dosyaDegisti = !string.Equals(state.AktifDosya, kaynak, StringComparison.OrdinalIgnoreCase);
            bool oncedenGriydi = state.GriKutusu.Visibility == Visibility.Visible;

            if (parcaDegisti || dosyaDegisti)
            {
                state.OnizlemeMi = onizlemeMi;
                state.OnizlemeYerelBas = onizlemeYerelBas;
                double hedefKaynakSn = onizlemeMi ? Math.Max(0, hedefYerelSn - onizlemeYerelBas) : hedefYerelSn;
                MedyayiAc(state, kaynak, dosya, hedefKaynakSn, _saat.Oynatiliyor);
                return;
            }
            else
            {
                state.GriKutusu.Visibility = Visibility.Collapsed;
                state.Video.Visibility = Visibility.Visible;

                if (zorla || oncedenGriydi)
                {
                    Konumlandir(state, onizlemeMi ? Math.Max(0, hedefYerelSn - onizlemeYerelBas) : hedefYerelSn, _saat.Oynatiliyor);
                    return;
                }
            }

            if (state.BekleyenKonumSn.HasValue)
            {
                BekleyenKonumuUygula(state);
                return;
            }

            double oynaticiYerelSn = state.Oynatici.Time / 1000.0;
            double kararYerelSn = state.OnizlemeMi
                ? oynaticiYerelSn + state.OnizlemeYerelBas
                : oynaticiYerelSn;
            var eylem = _saat.Karar(state.Kanal, kararYerelSn);
            switch (eylem)
            {
                case Eylem.HafifDuzelt hafif:
                    state.Oynatici.SetRate((float)(_saat.Hiz * hafif.HizKatsayisi));
                    break;
                case Eylem.Atla atla:
                    double hedefOynaticiSn = state.OnizlemeMi
                        ? Math.Max(0, atla.HedefYerelSn - state.OnizlemeYerelBas)
                        : atla.HedefYerelSn;
                    state.Oynatici.Time = (long)(hedefOynaticiSn * 1000);
                    state.Oynatici.SetRate((float)_saat.Hiz);
                    break;
                case Eylem.Gizle gizle:
                    KanaliGriYap(state, gizle.Neden ?? "kayıt yok");
                    return;
                default:
                    if (!_saat.HisterezisAktifMi(state.Kanal.Ad))
                    {
                        state.Oynatici.SetRate((float)_saat.Hiz);
                    }
                    break;
            }

            state.Oynatici.Mute = _saat.KanalSessizMi(state.Kanal);
            HazirlaSonrakiParca(state, sanalSn);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            KanaliGriYap(state, "Kanal açılamadı: " + ex.Message);
        }
    }

    private (string dosya, bool onizlemeMi, double onizlemeYerelBas) VideoKaynaginiSec(
        KanalGorunumu state, string asılDosya)
    {
        var kanal = state.Kanal;
        bool odakli = _gorunum switch
        {
            GorunumModu.Odak odak => string.Equals(odak.BuyukKanal, kanal.Ad, StringComparison.OrdinalIgnoreCase),
            GorunumModu.Tek tek => string.Equals(tek.Kanal, kanal.Ad, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
        if (odakli || string.IsNullOrWhiteSpace(_klasor)) return (asılDosya, false, 0);

        if (!state.OnizlemeOnbellek.TryGetValue(asılDosya, out var onizleme))
        {
            onizleme = (null, 0);
            var parca = kanal.Parcalar.FirstOrDefault(p =>
                p.KayitVar && string.Equals(p.Dosya, asılDosya, StringComparison.OrdinalIgnoreCase));
            if (parca != null)
            {
                try
                {
                    string[] adaylar = Directory.GetFiles(_klasor, kanal.Ad + "_onizleme*.mp4")
                        .OrderBy(yol => yol, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    string kaynakAdi = Path.GetFileNameWithoutExtension(asılDosya);
                    var parcaOzelAdaylar = adaylar.Where(yol =>
                        Path.GetFileNameWithoutExtension(yol).Contains(kaynakAdi, StringComparison.OrdinalIgnoreCase)).ToArray();
                    string? aday = parcaOzelAdaylar.Length == 1
                        ? parcaOzelAdaylar[0]
                        : adaylar.Length == 1 && kanal.Parcalar.Count(p => p.KayitVar) == 1
                            ? adaylar[0]
                            : null;

                    double eslemeSn = parca.YerelBas + (parca.YerelSon - parca.YerelBas) / 2;
                    int eslesenParcaSayisi = kanal.Parcalar.Count(p => p.KayitVar &&
                        eslemeSn >= p.YerelBas && eslemeSn <= p.YerelSon);
                    if (aday != null && eslesenParcaSayisi == 1 &&
                        string.Equals(kanal.OnizlemeDosyasiniEslestir(aday, eslemeSn), asılDosya, StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(aday))
                    {
                        onizleme = (aday, parca.YerelBas);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    onizleme = (null, 0);
                }
            }
            state.OnizlemeOnbellek.Add(asılDosya, onizleme);
        }

        return onizleme.Item1 == null
            ? (asılDosya, false, 0)
            : (onizleme.Item1, true, onizleme.Item2);
    }

    private void MedyayiAc(KanalGorunumu state, string dosya, string asilDosya, double yerelSn, bool oynat)
    {
        Media yeni;
        if (string.Equals(state.HazirDosya, dosya, StringComparison.OrdinalIgnoreCase) && state.HazirMedya != null)
        {
            yeni = state.HazirMedya;
            state.HazirMedya = null;
            state.HazirDosya = null;
        }
        else
        {
            yeni = new Media(_libVlc, VlcYolu.Hazirla(dosya), FromType.FromPath);
        }

        var eski = state.AktifMedya;
        if (eski != null) state.Oynatici.Stop();
        state.Oynatici.Media = yeni;
        state.AktifMedya = yeni;
        state.AktifDosya = dosya;
        state.AktifAsilDosya = asilDosya;
        state.GriKutusu.Visibility = Visibility.Collapsed;
        state.Video.Visibility = Visibility.Visible;
        state.Oynatici.Mute = _saat.KanalSessizMi(state.Kanal);
        Konumlandir(state, yerelSn, oynat);
        eski?.Dispose();
    }

    private static void Konumlandir(KanalGorunumu state, double yerelSn, bool oynat)
    {
        state.BekleyenKonumSn = yerelSn;
        state.BekleyenDuraklat = !oynat;
        BekleyenKonumuUygula(state);
        if (state.BekleyenKonumSn.HasValue || oynat && !state.Oynatici.IsPlaying)
            state.Oynatici.Play();
    }

    private static void BekleyenKonumuUygula(KanalGorunumu state)
    {
        if (state.BekleyenKonumSn is not double zaman || !state.Oynatici.IsSeekable ||
            state.Oynatici.State is not (VLCState.Playing or VLCState.Paused)) return;

        state.Oynatici.Time = Math.Max(0, (long)(zaman * 1000));
        state.BekleyenKonumSn = null;
        if (state.BekleyenDuraklat)
        {
            state.Oynatici.SetPause(true);
            state.BekleyenDuraklat = false;
        }
    }

    private void HazirlaSonrakiParca(KanalGorunumu state, double sanalSn)
    {
        var parcalar = state.Kanal.Parcalar;
        double kanalSn = state.Kanal.KanalZamaniHesapla(sanalSn, _saat.ElleOfsetAl(state.Kanal.Ad));
        int index = parcalar.ToList().FindIndex(p => p.SanalBas <= kanalSn && p.SanalSon >= kanalSn && p.KayitVar);
        if (index < 0 || index + 1 >= parcalar.Count) return;
        var sonraki = parcalar[index + 1];
        if (!sonraki.KayitVar || string.IsNullOrWhiteSpace(sonraki.Dosya)) return;
        double kalan = state.Kanal.SanalZamanHesapla(sonraki.SanalBas, _saat.ElleOfsetAl(state.Kanal.Ad)) - sanalSn;
        if (kalan > 5 || kalan < 0) return;
        string kaynak = VideoKaynaginiSec(state, sonraki.Dosya).dosya;
        if (!File.Exists(kaynak) || string.Equals(kaynak, state.AktifDosya, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(kaynak, state.HazirDosya, StringComparison.OrdinalIgnoreCase)) return;
        state.HazirMedya?.Dispose();
        state.HazirMedya = new Media(_libVlc, VlcYolu.Hazirla(kaynak), FromType.FromPath);
        state.HazirDosya = kaynak;
    }

    private void KanalBitisiniIsle(KanalGorunumu state)
    {
        if (_kapatiliyor) return;
        TekKanaliGuncelle(state, true);
    }

    private static void KanaliGriYap(KanalGorunumu state, string neden)
    {
        try
        {
            if (state.Oynatici.IsPlaying)
            {
                state.Oynatici.Pause();
            }
        }
        catch
        {
            // Oynatıcı hata durumundaysa pause hatasını yut
        }
        state.Oynatici.Mute = true;
        state.GriKutusu.Text = neden;
        state.GriKutusu.Visibility = Visibility.Visible;
        state.Video.Visibility = Visibility.Collapsed;
    }

    private void GorunumuUygula()
    {
        KarolariEbeveynlerindenCikar();
        VideoIzgarasi.Children.Clear();
        VideoIzgarasi.RowDefinitions.Clear();
        VideoIzgarasi.ColumnDefinitions.Clear();
        var sirali = _kanallar.Values.ToList();
        if (sirali.Count == 0) return;
        foreach (var state in sirali)
        {
            Grid.SetRow(state.Karo, 0);
            Grid.SetColumn(state.Karo, 0);
            Grid.SetRowSpan(state.Karo, 1);
            Grid.SetColumnSpan(state.Karo, 1);
        }

        if (_gorunum is GorunumModu.Tek tek)
        {
            var secili = BulKanal(tek.Kanal) ?? sirali[0];
            VideoIzgarasi.RowDefinitions.Add(new RowDefinition());
            VideoIzgarasi.ColumnDefinitions.Add(new ColumnDefinition());
            VideoIzgarasi.Children.Add(secili.Karo);
            return;
        }
        if (_gorunum is GorunumModu.Odak odak)
        {
            var buyuk = BulKanal(odak.BuyukKanal) ?? sirali[0];
            var kalan = sirali.Where(k => !ReferenceEquals(k, buyuk)).ToList();
            if (kalan.Count == 0)
            {
                VideoIzgarasi.RowDefinitions.Add(new RowDefinition());
                VideoIzgarasi.ColumnDefinitions.Add(new ColumnDefinition());
                VideoIzgarasi.Children.Add(buyuk.Karo);
                return;
            }
            VideoIzgarasi.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            VideoIzgarasi.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            VideoIzgarasi.RowDefinitions.Add(new RowDefinition());
            VideoIzgarasi.RowDefinitions.Add(new RowDefinition());
            Grid.SetRowSpan(buyuk.Karo, 2);
            VideoIzgarasi.Children.Add(buyuk.Karo);
            var kucukler = new StackPanel { Orientation = Orientation.Vertical };
            foreach (var k in kalan) kucukler.Children.Add(k.Karo);
            Grid.SetColumn(kucukler, 1);
            Grid.SetRowSpan(kucukler, 2);
            VideoIzgarasi.Children.Add(kucukler);
            return;
        }

        bool tekSatir = sirali.Count == 2 || sirali.Count == 3;
        int satir = tekSatir ? 1 : (int)Math.Ceiling(sirali.Count / 2.0);
        int sutun = tekSatir ? sirali.Count : Math.Min(2, sirali.Count);
        for (int i = 0; i < satir; i++) VideoIzgarasi.RowDefinitions.Add(new RowDefinition());
        for (int i = 0; i < sutun; i++) VideoIzgarasi.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < sirali.Count; i++)
        {
            var karo = sirali[i].Karo;
            Grid.SetRow(karo, tekSatir ? 0 : i / sutun);
            Grid.SetColumn(karo, tekSatir ? i : i % sutun);
            VideoIzgarasi.Children.Add(karo);
        }
    }

    private KanalGorunumu? BulKanal(string ad) => _kanallar.TryGetValue(ad, out var state) ? state : null;

    private void OynatDurdur_Click(object sender, RoutedEventArgs e)
    {
        if (_saat.Oynatiliyor)
        {
            _saat.Duraklat();
            foreach (var state in _kanallar.Values) state.Oynatici.Pause();
            OynatDurdurDugmesi.Content = "\uE768";
        }
        else
        {
            _saat.Oynat();
            TumKanallariGuncelle(false);
            foreach (var state in _kanallar.Values)
            {
                if (state.GriKutusu.Visibility != Visibility.Visible) state.Oynatici.Play();
            }
            OynatDurdurDugmesi.Content = "\uE769";
        }
    }

    private void ZamanKaydir(double zaman)
    {
        KanalHatalariniTemizle();
        _saat.Ara(zaman);
        TumKanallariGuncelle(true);
        if (_saat.Oynatiliyor)
        {
            foreach (var state in _kanallar.Values)
            {
                if (state.GriKutusu.Visibility != Visibility.Visible) state.Oynatici.Play();
            }
        }
    }

    private void TranskriptListesi_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_yoklamaGuncelleniyor || _programatikSecimYapiliyor) return;
        if (TranskriptListesi.SelectedItem is TranskriptSatiri satir)
        {
            if (_duzenlemeAcik && satir.Segment is EfektifSegment ef)
            {
                DuzenlemeKontrolu.SegmentYukle(ef, _saat.Ders.Ad, _hamTranskript?.KaynakOzet);
            }

            KanalHatalariniTemizle();
            _saat.Ara(satir.Segment.Bas);
            ZamanKaydiraci.Value = Math.Clamp(satir.Segment.Bas, 0, Math.Max(1, _sureSn));
            TumKanallariGuncelle(true);
            if (_saat.Oynatiliyor)
            {
                foreach (var state in _kanallar.Values)
                    if (state.GriKutusu.Visibility != Visibility.Visible) state.Oynatici.Play();
            }
        }

    }

    private void KanalHatalariniTemizle()
    {
        foreach (var state in _kanallar.Values)
        {
            state.HataVar = false;
            state.HataDosyasi = null;
        }
    }

    private void Izgara_Click(object sender, RoutedEventArgs e) => GorunumDegistir(new GorunumModu.Izgara());
    private void UcIzgara_Click(object sender, RoutedEventArgs e)
    {
        GorunumDegistir(new GorunumModu.Izgara());
        IzgarayiYatayYap(3);
    }
    private void IkiIzgara_Click(object sender, RoutedEventArgs e)
    {
        GorunumDegistir(new GorunumModu.Izgara());
        IzgarayiYatayYap(2);
    }
    private void Odak_Click(object sender, RoutedEventArgs e) => GorunumDegistir(new GorunumModu.Odak(_saat.AktifSesKanali ?? _kanallar.Keys.FirstOrDefault() ?? string.Empty));
    private void Tek_Click(object sender, RoutedEventArgs e) => GorunumDegistir(new GorunumModu.Tek(_saat.AktifSesKanali ?? _kanallar.Keys.FirstOrDefault() ?? string.Empty));

    private void IzgarayiYatayYap(int kanalSayisi)
    {
        int gorunen = Math.Min(kanalSayisi, _kanallar.Count);
        KarolariEbeveynlerindenCikar();
        VideoIzgarasi.Children.Clear();
        VideoIzgarasi.RowDefinitions.Clear();
        VideoIzgarasi.ColumnDefinitions.Clear();
        VideoIzgarasi.RowDefinitions.Add(new RowDefinition());
        for (int i = 0; i < gorunen; i++) VideoIzgarasi.ColumnDefinitions.Add(new ColumnDefinition());
        int index = 0;
        foreach (var state in _kanallar.Values.Take(gorunen))
        {
            Grid.SetRow(state.Karo, 0);
            Grid.SetColumn(state.Karo, index++);
            VideoIzgarasi.Children.Add(state.Karo);
        }
    }

    private void KarolariEbeveynlerindenCikar()
    {
        foreach (var state in _kanallar.Values)
        {
            if (state.Karo.Parent is Panel ebeveyn)
            {
                ebeveyn.Children.Remove(state.Karo);
            }
        }
    }

    private void GorunumDegistir(GorunumModu gorunum)
    {
        _gorunum = gorunum;
        _saat.GorunumModuDegistir(gorunum);
        GorunumuUygula();
        TumKanallariGuncelle(false);
    }

    private void SesKanaliSecici_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SesKanaliSecici.SelectedItem is not string ad || _saat == null) return;
        _saat.SesKanaliSec(ad);
        foreach (var state in _kanallar.Values) state.Oynatici.Mute = _saat.KanalSessizMi(state.Kanal);
    }

    private void ElleHizala_Click(object sender, RoutedEventArgs e)
    {
        var adlar = _kanallar.Keys.ToList();
        if (adlar.Count < 2) return;
        var pencere = new ElleHizalaPenceresi(adlar, Ad => AnlikSanalZaman(Ad), (Ad, referansSanala, kanalSanala) =>
        {
            var kanal = _kanallar[Ad].Kanal;
            double ekOfset = _saat.ElleOfsetAl(Ad) +
                (kanalSanala - referansSanala) * (1.0 + kanal.DriftPpm * 1e-6);
            double yeniOfset = Math.Clamp(ekOfset, -86400, 86400);
            _saat.ElleOfsetUygula(Ad, yeniOfset);
            TumKanallariGuncelle(true);
        }) { Owner = this };
        pencere.ShowDialog();
    }

    private double AnlikSanalZaman(string kanalAdi)
    {
        var state = _kanallar[kanalAdi];
        double yerelSn = state.Oynatici.Time / 1000.0;
        if (state.OnizlemeMi)
        {
            yerelSn += state.OnizlemeYerelBas;
        }
        double ekOfset = _saat.ElleOfsetAl(kanalAdi);

        // Önizleme videosu oynatılıyorsa asıl parçanın dosyasını eşle
        string? asilDosya = state.AktifAsilDosya ?? state.Kanal.OnizlemeDosyasiniEslestir(state.AktifDosya, yerelSn);

        if (!string.IsNullOrWhiteSpace(asilDosya))
        {
            try
            {
                return state.Kanal.YereldenSanala(asilDosya, yerelSn, ekOfset);
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
            {
                var parca = state.Kanal.Parcalar.FirstOrDefault(p => string.Equals(p.Dosya, asilDosya, StringComparison.OrdinalIgnoreCase));
                if (parca?.Dosya != null)
                {
                    double kirp = Math.Clamp(yerelSn, parca.YerelBas, parca.YerelSon);
                    try { return state.Kanal.YereldenSanala(parca.Dosya, kirp, ekOfset); } catch { }
                }
            }
        }

        return _saat.Simdi();
    }

    private void OfsetDisaAktar_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "JSON dosyası (*.json)|*.json", FileName = "elle-ofsetler.json" };
        if (dialog.ShowDialog(this) != true) return;
        var aktarim = new
        {
            ders = _saat.Ders.Ad,
            ders_json_degistirildi = false,
            elle_ofsetler = _saat.EllesOfsetler
        };
        try
        {
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(aktarim, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Ofsetler dışa aktarılamadı: " + ex.Message, "Dışa aktarma hatası",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Pencere_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11 || e.Key == Key.Escape && PencereYerlesimi.GetTamEkran(this))
        {
            TamEkran_Click(sender, e);
            e.Handled = true;
            return;
        }
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;
        if (e.Key == Key.G) { GorunumDegistir(new GorunumModu.Izgara()); e.Handled = true; return; }
        if (e.Key == Key.O) { Odak_Click(sender, e); e.Handled = true; return; }
        int kanalNo = e.Key switch { Key.D1 or Key.NumPad1 => 1, Key.D2 or Key.NumPad2 => 2, Key.D3 or Key.NumPad3 => 3, Key.D4 or Key.NumPad4 => 4, _ => 0 };
        if (kanalNo > 0 && kanalNo <= _kanallar.Count)
        {
            GorunumDegistir(new GorunumModu.Tek(_kanallar.Keys.ElementAt(kanalNo - 1)));
            e.Handled = true;
        }
    }

    private void TamEkran_Click(object sender, RoutedEventArgs e)
    {
        PencereYerlesimi.TamEkraniAyarla(this, !PencereYerlesimi.GetTamEkran(this));
        TamEkranDugmesi.Content = PencereYerlesimi.GetTamEkran(this) ? "Pencereli görünüm" : "Tam ekran";
    }

    private void Pencere_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_kapatiliyor) return;
        _kapatiliyor = true;
        _zamanlayici.Stop();
        _yoklamaZamanlayici.Stop();
        foreach (var state in _kanallar.Values)
        {
            state.Oynatici.Stop();
            state.Oynatici.Media = null;
            state.AktifMedya?.Dispose();
            state.HazirMedya?.Dispose();
            state.Oynatici.Dispose();
        }
    }

    private static string ZamanBicimle(double saniye)
    {
        var sure = TimeSpan.FromSeconds(Math.Max(0, saniye));
        return sure.TotalHours >= 1
            ? $"{(int)sure.TotalHours:00}:{sure.Minutes:00}:{sure.Seconds:00}"
            : $"{(int)sure.TotalMinutes:00}:{sure.Seconds:00}";
    }

    private sealed class KanalGorunumu(Kanal kanal, LibVLCSharp.Shared.MediaPlayer oynatici, VideoView video, Grid karo,
        TextBlock griKutusu, TextBlock etiket)
    {
        public Kanal Kanal { get; } = kanal;
        public LibVLCSharp.Shared.MediaPlayer Oynatici { get; } = oynatici;
        public VideoView Video { get; } = video;
        public Grid Karo { get; } = karo;
        public TextBlock GriKutusu { get; } = griKutusu;
        public TextBlock Etiket { get; } = etiket;
        public Media? AktifMedya { get; set; }
        public string? AktifDosya { get; set; }
        public string? AktifAsilDosya { get; set; }
        public bool HataVar { get; set; }
        public string? HataDosyasi { get; set; }
        public double? BekleyenKonumSn { get; set; }
        public bool BekleyenDuraklat { get; set; }
        public bool OnizlemeMi { get; set; }
        public double OnizlemeYerelBas { get; set; }
        public Dictionary<string, (string? Dosya, double YerelBas)> OnizlemeOnbellek { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Media? HazirMedya { get; set; }
        public string? HazirDosya { get; set; }
    }

    private sealed record TranskriptSatiri(Segment Segment, string Gosterim);

    private sealed class ElleHizalaPenceresi : Window
    {
        private readonly ComboBox _kanalA;
        private readonly ComboBox _kanalB;
        private readonly Func<string, double> _sanalZaman;
        private readonly Action<string, double, double> _uygula;
        private double? _isaretA;
        private double? _isaretB;
        private readonly TextBlock _durum;

        public ElleHizalaPenceresi(IReadOnlyList<string> kanallar, Func<string, double> sanalZaman,
            Action<string, double, double> uygula)
        {
            Title = "Kanalları elle hizala";
            Width = 420;
            Height = 250;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            _sanalZaman = sanalZaman;
            _uygula = uygula;
            _kanalA = new ComboBox { ItemsSource = kanallar, SelectedIndex = 0, Margin = new Thickness(0, 4, 0, 8) };
            _kanalB = new ComboBox { ItemsSource = kanallar, SelectedIndex = kanallar.Count > 1 ? 1 : 0, Margin = new Thickness(0, 4, 0, 8) };
            _durum = new TextBlock { Text = "Aynı anı gösterirken her iki kanalda da işaret alın.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
            var panel = new StackPanel { Margin = new Thickness(14) };
            panel.Children.Add(new TextBlock { Text = "Referans kanal" });
            panel.Children.Add(_kanalA);
            panel.Children.Add(new Button { Content = "Şimdiki zamanı al", Padding = new Thickness(8, 4, 8, 4), HorizontalAlignment = HorizontalAlignment.Left });
            var isaretA = (Button)panel.Children[2];
            isaretA.Click += (_, _) => { _isaretA = _sanalZaman((string)_kanalA.SelectedItem); DurumuGuncelle(); };
            panel.Children.Add(new TextBlock { Text = "Hizalanacak kanal" });
            panel.Children.Add(_kanalB);
            var satir = new StackPanel { Orientation = Orientation.Horizontal };
            var isaretB = new Button { Content = "Şimdiki zamanı al", Padding = new Thickness(8, 4, 8, 4) };
            isaretB.Click += (_, _) => { _isaretB = _sanalZaman((string)_kanalB.SelectedItem); DurumuGuncelle(); };
            var kaydet = new Button { Content = "Kaydet", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
            kaydet.Click += (_, _) => Kaydet();
            satir.Children.Add(isaretB);
            satir.Children.Add(kaydet);
            panel.Children.Add(satir);
            panel.Children.Add(_durum);
            Content = panel;
        }

        private void DurumuGuncelle()
        {
            _durum.Text = _isaretA.HasValue && _isaretB.HasValue
                ? "İki işaret alındı. Kaydet, yalnız bu oturumun ofsetini günceller."
                : "Aynı anı gösterirken her iki kanalda da işaret alın.";
        }

        private void Kaydet()
        {
            if (!_isaretA.HasValue || !_isaretB.HasValue ||
                _kanalA.SelectedItem is not string kanalA || _kanalB.SelectedItem is not string kanalB ||
                string.Equals(kanalA, kanalB, StringComparison.OrdinalIgnoreCase))
            {
                _durum.Text = "İki farklı kanalda da işaret alın.";
                return;
            }
            _uygula(kanalB, _isaretA.Value, _isaretB.Value);
            DialogResult = true;
        }
    }
}
