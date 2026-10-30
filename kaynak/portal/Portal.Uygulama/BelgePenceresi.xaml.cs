namespace Portal.Uygulama;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;
using Portal.Cekirdek;
using Portal.Cekirdek.Dizin;

/// <summary>
/// Belge görüntüleyici penceresi: PDF veya görüntü belgelerini sayfa sayfa gösterir,
/// sayfa içi arama ve metin vurgulama sunar.
/// </summary>
public partial class BelgePenceresi : Window
{
    private static readonly HashSet<string> GoruntuUzantilari = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".gif", ".webp"
    };

    private const int EnFazlaOnbellek = 8;

    private readonly DizinKaynagi _kaynak;
    private readonly long _dosyaId;
    private readonly SayfaDeposu _sayfaDeposu;
    private readonly DosyaSonucu? _dosya;
    private readonly IReadOnlyList<SayfaKaydi> _tumSayfalar;
    private readonly List<int> _eslesenSayfalar = [];
    private readonly int _toplamSayfa;

    private int _aktifSayfaNo;
    private int _zoomYuzde = 100;
    private PdfDocument? _pdfBelgesi;
    private bool _goruntuDestekleniyor;
    private bool _tekGorselMi;

    private readonly Dictionary<(int Sayfa, int Zoom), BitmapSource> _lruOnbellek = [];
    private readonly LinkedList<(int Sayfa, int Zoom)> _lruSira = [];

    public BelgePenceresi(DizinKaynagi kaynak, long dosyaId, int sayfaNo, string? sorgu)
    {
        InitializeComponent();

        _kaynak = kaynak;
        _dosyaId = dosyaId;
        _sayfaDeposu = new SayfaDeposu(_kaynak);
        _dosya = _sayfaDeposu.Dosya(dosyaId);

        string baslikAd = _dosya?.Ad ?? dosyaId.ToString();
        Title = $"Bulgu — Belge — {baslikAd}";

        _tumSayfalar = _sayfaDeposu.Getir(dosyaId);
        _toplamSayfa = _tumSayfalar.Count > 0 ? _tumSayfalar.Count : Math.Max(1, _dosya?.SayfaSayisi ?? 1);
        _aktifSayfaNo = Math.Clamp(sayfaNo, 1, Math.Max(1, _toplamSayfa));

        SayfaNoKutusu.Text = _aktifSayfaNo.ToString();
        ToplamSayfaMetni.Text = $"/ {_toplamSayfa}";

        if (!string.IsNullOrWhiteSpace(sorgu))
        {
            BelgeAramaKutusu.Text = sorgu;
        }
    }

    private async void Pencere_Loaded(object sender, RoutedEventArgs e)
    {
        await GoruntuSisteminiHazirlaAsync();
        EslesenSayfalariBul();
        await SayfaGosterAsync(_aktifSayfaNo);
    }

    private async Task GoruntuSisteminiHazirlaAsync()
    {
        if (_dosya == null)
        {
            SolBolmeyiGizle();
            return;
        }

        string uzanti = (_dosya.Uzanti ?? Path.GetExtension(_dosya.Yol)).TrimStart('.').ToLowerInvariant();
        bool isPdf = uzanti == "pdf";
        bool isImage = GoruntuUzantilari.Contains("." + uzanti);

        string? pdfYolu = null;
        if (isPdf)
        {
            string? aranabilir = BelgeYollari.AranabilirPdf(_kaynak.DizinKlasoru, _dosya.Sha);
            pdfYolu = aranabilir ?? _dosya.Yol;
        }
        else if (isImage)
        {
            string? aranabilir = BelgeYollari.AranabilirPdf(_kaynak.DizinKlasoru, _dosya.Sha);
            if (!string.IsNullOrWhiteSpace(aranabilir))
            {
                pdfYolu = aranabilir;
            }
        }

        if (!string.IsNullOrWhiteSpace(pdfYolu) && File.Exists(pdfYolu))
        {
            try
            {
                string yerelYol = await Task.Run(() => KopyaHazirla(pdfYolu, _dosyaId));
                var storageFile = await StorageFile.GetFileFromPathAsync(yerelYol);
                _pdfBelgesi = await PdfDocument.LoadFromFileAsync(storageFile);
                _goruntuDestekleniyor = true;
                _tekGorselMi = false;
            }
            catch (Exception ex)
            {
                SolHataMetni.Text = "PDF belgesi açılamadı:\n" + ex.Message;
                SolHataKutusu.Visibility = Visibility.Visible;
                _goruntuDestekleniyor = false;
            }
        }
        else if (isImage && File.Exists(_dosya.Yol))
        {
            _goruntuDestekleniyor = true;
            _tekGorselMi = true;
        }
        else
        {
            SolBolmeyiGizle();
        }
    }

    private static string KopyaHazirla(string kaynakYol, long dosyaId)
    {
        string yerelKlasor = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Bulgu",
            "gorunum");
        Directory.CreateDirectory(yerelKlasor);

        string dosyaAdi = $"{dosyaId}_{Path.GetFileName(kaynakYol)}";
        string hedefYol = Path.Combine(yerelKlasor, dosyaAdi);

        var srcFi = new FileInfo(kaynakYol);
        bool kopyala = true;
        if (File.Exists(hedefYol))
        {
            var dstFi = new FileInfo(hedefYol);
            if (dstFi.Length == srcFi.Length && dstFi.LastWriteTimeUtc == srcFi.LastWriteTimeUtc)
            {
                kopyala = false;
            }
        }

        if (kopyala)
        {
            File.Copy(kaynakYol, hedefYol, true);
            File.SetLastWriteTimeUtc(hedefYol, srcFi.LastWriteTimeUtc);
        }

        return hedefYol;
    }

    private void SolBolmeyiGizle()
    {
        SolKolon.Width = new GridLength(0);
        SolKolon.MinWidth = 0;
        BolucuKolon.Width = new GridLength(0);
        Bolucu.Visibility = Visibility.Collapsed;
        _goruntuDestekleniyor = false;
    }

    private async Task SayfaGosterAsync(int sayfaNo)
    {
        sayfaNo = Math.Clamp(sayfaNo, 1, Math.Max(1, _toplamSayfa));
        _aktifSayfaNo = sayfaNo;

        SayfaNoKutusu.Text = sayfaNo.ToString();
        ToplamSayfaMetni.Text = $"/ {_toplamSayfa}";
        OncekiSayfaDugmesi.IsEnabled = sayfaNo > 1;
        SonrakiSayfaDugmesi.IsEnabled = sayfaNo < _toplamSayfa;

        MetniGoster(sayfaNo);
        await SayfaGoruntusunuGosterAsync(sayfaNo);
        EslesmeDurumunuGuncelle();
    }

    private async Task SayfaGoruntusunuGosterAsync(int sayfaNo)
    {
        if (!_goruntuDestekleniyor) return;

        SolHataKutusu.Visibility = Visibility.Collapsed;
        SayfaGoruntusu.Visibility = Visibility.Visible;

        if (_tekGorselMi && _dosya != null)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(_dosya.Yol);
                bmp.EndInit();
                bmp.Freeze();
                SayfaGoruntusu.Source = bmp;
                double oran = _zoomYuzde / 100.0;
                SayfaGoruntusu.LayoutTransform = new ScaleTransform(oran, oran);
            }
            catch (Exception ex)
            {
                SolHataMetni.Text = "Görüntü dosyası yüklenemedi:\n" + ex.Message;
                SolHataKutusu.Visibility = Visibility.Visible;
                SayfaGoruntusu.Visibility = Visibility.Collapsed;
            }
            return;
        }

        if (_pdfBelgesi != null)
        {
            if (sayfaNo < 1 || sayfaNo > (int)_pdfBelgesi.PageCount)
            {
                SayfaGoruntusu.Source = null;
                return;
            }

            var onbellekli = OnbellektenAl(sayfaNo, _zoomYuzde);
            if (onbellekli != null)
            {
                SayfaGoruntusu.LayoutTransform = Transform.Identity;
                SayfaGoruntusu.Source = onbellekli;
                return;
            }

            try
            {
                using var page = _pdfBelgesi.GetPage((uint)(sayfaNo - 1));
                double zoom = _zoomYuzde / 100.0;
                uint destWidth = (uint)Math.Max(50, Math.Round(page.Size.Width * zoom));
                var renderOptions = new PdfPageRenderOptions
                {
                    DestinationWidth = destWidth
                };

                using var memStream = new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(memStream, renderOptions);
                memStream.Seek(0);

                using var netStream = memStream.AsStreamForRead();
                var ms = new MemoryStream();
                await netStream.CopyToAsync(ms);
                ms.Position = 0;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();

                OnbellegeEkle(sayfaNo, _zoomYuzde, bmp);
                SayfaGoruntusu.LayoutTransform = Transform.Identity;
                SayfaGoruntusu.Source = bmp;
            }
            catch (Exception ex)
            {
                SolHataMetni.Text = "Sayfa görüntüsü işlenemedi:\n" + ex.Message;
                SolHataKutusu.Visibility = Visibility.Visible;
                SayfaGoruntusu.Visibility = Visibility.Collapsed;
            }
        }
    }

    private BitmapSource? OnbellektenAl(int sayfa, int zoom)
    {
        if (_lruOnbellek.TryGetValue((sayfa, zoom), out var bmp))
        {
            _lruSira.Remove((sayfa, zoom));
            _lruSira.AddFirst((sayfa, zoom));
            return bmp;
        }
        return null;
    }

    private void OnbellegeEkle(int sayfa, int zoom, BitmapSource bmp)
    {
        if (_lruOnbellek.ContainsKey((sayfa, zoom)))
        {
            _lruSira.Remove((sayfa, zoom));
        }
        else if (_lruSira.Count >= EnFazlaOnbellek)
        {
            var son = _lruSira.Last!.Value;
            _lruSira.RemoveLast();
            _lruOnbellek.Remove(son);
        }

        _lruSira.AddFirst((sayfa, zoom));
        _lruOnbellek[(sayfa, zoom)] = bmp;
    }

    private void MetniGoster(int sayfaNo)
    {
        var sayfa = _tumSayfalar.FirstOrDefault(s => s.No == sayfaNo)
                    ?? _sayfaDeposu.Getir(_dosyaId, sayfaNo);

        if (sayfa == null)
        {
            OcrBilgiKutusu.Visibility = Visibility.Collapsed;
            SayfaMetniBlok.Inlines.Clear();
            SayfaMetniBlok.Text = "(Bu sayfa için metin bulunamadı)";
            return;
        }

        if (string.Equals(sayfa.Kaynak, "ocr", StringComparison.OrdinalIgnoreCase))
        {
            OcrBilgiKutusu.Visibility = Visibility.Visible;
            if (sayfa.Guven.HasValue)
            {
                int yuzde = sayfa.Guven.Value <= 1.0
                    ? (int)Math.Round(sayfa.Guven.Value * 100)
                    : (int)Math.Round(sayfa.Guven.Value);
                OcrBilgiMetni.Text = $"OCR · güven %{yuzde}";
            }
            else
            {
                OcrBilgiMetni.Text = "OCR";
            }
        }
        else
        {
            OcrBilgiKutusu.Visibility = Visibility.Collapsed;
        }

        SayfaMetniBlok.Inlines.Clear();
        string metin = sayfa.Metin ?? string.Empty;
        string aranan = BelgeAramaKutusu.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(aranan))
        {
            SayfaMetniBlok.Inlines.Add(new Run(metin));
            return;
        }

        string[] kelimeler = TrNormalizasyon.Kelimeler(aranan);
        if (kelimeler.Length == 0)
        {
            SayfaMetniBlok.Inlines.Add(new Run(metin));
            return;
        }

        var vurgular = MetinEsleme.Vurgula(metin, kelimeler);
        if (vurgular.Length == 0)
        {
            SayfaMetniBlok.Inlines.Add(new Run(metin));
            return;
        }

        int mevcut = 0;
        foreach (var (bas, uz) in vurgular)
        {
            if (bas < 0 || uz <= 0 || bas >= metin.Length) continue;

            int duzgunBas = Math.Max(mevcut, bas);
            if (duzgunBas > mevcut)
            {
                SayfaMetniBlok.Inlines.Add(new Run(metin.Substring(mevcut, duzgunBas - mevcut)));
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
                SayfaMetniBlok.Inlines.Add(run);
                mevcut = son;
            }
            else
            {
                mevcut = Math.Max(mevcut, duzgunBas);
            }
        }

        if (mevcut < metin.Length)
        {
            SayfaMetniBlok.Inlines.Add(new Run(metin.Substring(mevcut)));
        }
    }

    private void BelgeAramaKutusu_TextChanged(object sender, TextChangedEventArgs e)
    {
        EslesenSayfalariBul();
        MetniGoster(_aktifSayfaNo);
    }

    private void EslesenSayfalariBul()
    {
        _eslesenSayfalar.Clear();
        string aranan = BelgeAramaKutusu.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(aranan))
        {
            EslesmeDurumMetni.Text = string.Empty;
            return;
        }

        string[] kelimeler = TrNormalizasyon.Kelimeler(aranan);
        if (kelimeler.Length == 0)
        {
            EslesmeDurumMetni.Text = string.Empty;
            return;
        }

        foreach (var sayfa in _tumSayfalar)
        {
            var vurgular = MetinEsleme.Vurgula(sayfa.Metin, kelimeler);
            if (vurgular.Length > 0)
            {
                _eslesenSayfalar.Add(sayfa.No);
            }
        }

        EslesmeDurumunuGuncelle();
    }

    private void EslesmeDurumunuGuncelle()
    {
        if (_eslesenSayfalar.Count == 0)
        {
            string aranan = BelgeAramaKutusu.Text?.Trim() ?? string.Empty;
            EslesmeDurumMetni.Text = string.IsNullOrWhiteSpace(aranan) ? string.Empty : "Eşleşme yok";
            return;
        }

        int sira = _eslesenSayfalar.IndexOf(_aktifSayfaNo);
        if (sira >= 0)
        {
            EslesmeDurumMetni.Text = $"{sira + 1} / {_eslesenSayfalar.Count}";
        }
        else
        {
            EslesmeDurumMetni.Text = $"{_eslesenSayfalar.Count} sayfa";
        }
    }

    private async void SonrakiEslesme()
    {
        if (_eslesenSayfalar.Count == 0) return;

        int sonraki = _eslesenSayfalar.FirstOrDefault(n => n > _aktifSayfaNo);
        if (sonraki == 0)
        {
            sonraki = _eslesenSayfalar[0];
        }

        await SayfaGosterAsync(sonraki);
    }

    private async void OncekiEslesme()
    {
        if (_eslesenSayfalar.Count == 0) return;

        int onceki = _eslesenSayfalar.LastOrDefault(n => n < _aktifSayfaNo);
        if (onceki == 0)
        {
            onceki = _eslesenSayfalar[^1];
        }

        await SayfaGosterAsync(onceki);
    }

    private async void OncekiSayfaDugmesi_Click(object sender, RoutedEventArgs e)
    {
        if (_aktifSayfaNo > 1)
        {
            await SayfaGosterAsync(_aktifSayfaNo - 1);
        }
    }

    private async void SonrakiSayfaDugmesi_Click(object sender, RoutedEventArgs e)
    {
        if (_aktifSayfaNo < _toplamSayfa)
        {
            await SayfaGosterAsync(_aktifSayfaNo + 1);
        }
    }

    private async void SayfaNoKutusu_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (int.TryParse(SayfaNoKutusu.Text, out int no))
            {
                await SayfaGosterAsync(no);
            }
            else
            {
                SayfaNoKutusu.Text = _aktifSayfaNo.ToString();
            }
            e.Handled = true;
        }
    }

    private async void YakinlastirmaKaydiraci_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;

        _zoomYuzde = (int)Math.Round(YakinlastirmaKaydiraci.Value);
        if (YakinlastirmaMetni != null)
        {
            YakinlastirmaMetni.Text = $"%{_zoomYuzde}";
        }

        await SayfaGoruntusunuGosterAsync(_aktifSayfaNo);
    }

    private void BelgeAramaKutusu_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SonrakiEslesme();
            e.Handled = true;
        }
    }

    private void OncekiEslesmeDugmesi_Click(object sender, RoutedEventArgs e)
    {
        OncekiEslesme();
    }

    private void SonrakiEslesmeDugmesi_Click(object sender, RoutedEventArgs e)
    {
        SonrakiEslesme();
    }

    private void AsliniAcDugmesi_Click(object sender, RoutedEventArgs e)
    {
        if (_dosya == null || string.IsNullOrWhiteSpace(_dosya.Yol) || !File.Exists(_dosya.Yol))
        {
            MessageBox.Show(this, "Özgün dosya bulunamadı.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_dosya.Yol) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Dosya açılamadı:\n" + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void KlasorunuAcDugmesi_Click(object sender, RoutedEventArgs e)
    {
        if (_dosya == null || string.IsNullOrWhiteSpace(_dosya.Yol)) return;

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_dosya.Yol}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Klasör açılamadı:\n" + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Pencere_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.PageUp)
        {
            if (_aktifSayfaNo > 1)
            {
                _ = SayfaGosterAsync(_aktifSayfaNo - 1);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.PageDown)
        {
            if (_aktifSayfaNo < _toplamSayfa)
            {
                _ = SayfaGosterAsync(_aktifSayfaNo + 1);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            BelgeAramaKutusu.Focus();
            BelgeAramaKutusu.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.F3)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                OncekiEslesme();
            }
            else
            {
                SonrakiEslesme();
            }
            e.Handled = true;
        }
    }

    private void Pencere_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _pdfBelgesi = null;
        _lruOnbellek.Clear();
        _lruSira.Clear();
    }
}
