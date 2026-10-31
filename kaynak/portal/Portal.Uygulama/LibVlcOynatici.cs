namespace Portal.Uygulama;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using Portal.Cekirdek;

/// <summary>
/// LibVLC tabanlı IOynatici uygulaması (T11B).
/// </summary>
public sealed class LibVlcOynatici : IOynatici, IDisposable
{
    private readonly bool _kendiLibVlc;
    private readonly bool _kendiPlayer;
    private bool _disposed;
    private bool _dongu;
    private long? _aZamaniMs;
    private long? _bZamaniMs;
    private bool _abAtlamaYapiliyor;
    private long? _oncekiZamanMs;

    public static string[] EnBoyOranlari { get; } =
        ["Varsayılan", "16:9", "4:3", "1:1", "16:10", "2.21:1", "2.35:1", "2.39:1", "5:4"];

    public static double[] HizOnayarlari { get; } =
        [0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 3, 4];

    public string[] Ekolayzer { get; } =
        ["Flat", "Classical", "Club", "Dance", "Full bass", "Full bass and treble",
         "Full treble", "Headphones", "Large hall", "Live", "Party", "Pop", "Reggae", "Rock",
         "Ska", "Soft", "Techno", "Vocal"];

    public LibVLC LibVlc { get; }
    public MediaPlayer MediaPlayer { get; }
    public Media? Medya { get; private set; }

    public LibVlcOynatici(LibVLC? libVlc = null, MediaPlayer? mediaPlayer = null)
    {
        if (libVlc == null)
        {
            LibVlc = LibVlcAyarlari.Paylasilan;
            _kendiLibVlc = false;
        }
        else
        {
            LibVlc = libVlc;
            _kendiLibVlc = false;
        }

        if (mediaPlayer == null)
        {
            MediaPlayer = new MediaPlayer(LibVlc);
            _kendiPlayer = true;
        }
        else
        {
            MediaPlayer = mediaPlayer;
            _kendiPlayer = false;
        }

        MediaPlayer.TimeChanged += OnTimeChanged;
        MediaPlayer.EndReached += OnEndReached;
    }

    /// <summary>
    /// Belirtilen dosya yolundan medyayı yükler.
    /// </summary>
    public void Yukle(string dosyaYolu)
    {
        if (string.IsNullOrWhiteSpace(dosyaYolu))
        {
            throw new ArgumentException("Dosya yolu boş olamaz.", nameof(dosyaYolu));
        }

        if (!File.Exists(dosyaYolu))
        {
            throw new FileNotFoundException("Video dosyası bulunamadı.", dosyaYolu);
        }

        var eskiMedya = Medya;
        if (eskiMedya != null) MediaPlayer.Stop();
        _aZamaniMs = null;
        _bZamaniMs = null;
        _oncekiZamanMs = null;

        var yeniMedya = new Media(LibVlc, VlcYolu.Hazirla(dosyaYolu), FromType.FromPath);
        MediaPlayer.Media = yeniMedya;
        Medya = yeniMedya;

        eskiMedya?.Dispose();
    }

    /// <summary>
    /// Oynatmayı başlatır veya duraklatır.
    /// </summary>
    public void OynatDuraklat()
    {
        if (MediaPlayer.IsPlaying)
        {
            MediaPlayer.Pause();
        }
        else
        {
            MediaPlayer.Play();
        }
    }

    /// <summary>
    /// Belirtilen saniyeye atlar.
    /// </summary>
    public void Ara(double saniye)
    {
        long ms = (long)Math.Max(0, Math.Round(saniye * 1000.0));
        MediaPlayer.Time = ms;
    }

    /// <summary>
    /// Anlık oynatma zamanı (saniye cinsinden).
    /// </summary>
    public double Zaman => MediaPlayer.Time >= 0 ? MediaPlayer.Time / 1000.0 : 0.0;

    /// <summary>
    /// Toplam süre (saniye cinsinden).
    /// </summary>
    public double ToplamSure => MediaPlayer.Length > 0 ? MediaPlayer.Length / 1000.0 : 0.0;

    /// <summary>
    /// Oynatma hızı (0.25 - 4.0 arasında).
    /// </summary>
    public double Hiz
    {
        get => MediaPlayer.Rate;
        set => MediaPlayer.SetRate((float)Math.Clamp(value, 0.25, 4.0));
    }

    /// <summary>
    /// Ses seviyesi (0 - 100 arasında).
    /// </summary>
    public int SesSeviyesi
    {
        get => MediaPlayer.Volume;
        set => MediaPlayer.Volume = Math.Clamp(value, 0, 100);
    }

    /// <summary>
    /// Şu anda oynatılıyor mu?
    /// </summary>
    public bool Oynatiliyor => MediaPlayer.IsPlaying;

    public IReadOnlyList<(int Id, string Ad)> SesParcalari
    {
        get
        {
            try
            {
                return MediaPlayer.AudioTrackDescription?
                    .Select(parca => (parca.Id, parca.Name ?? string.Empty))
                    .ToArray() ?? Array.Empty<(int Id, string Ad)>();
            }
            catch
            {
                return Array.Empty<(int Id, string Ad)>();
            }
        }
    }

    public int SesParcasi
    {
        get { try { return MediaPlayer.AudioTrack; } catch { return -1; } }
        set { try { MediaPlayer.SetAudioTrack(value); } catch { } }
    }

    public IReadOnlyList<(int Id, string Ad)> AltyaziParcalari
    {
        get
        {
            try
            {
                return MediaPlayer.SpuDescription?
                    .Select(parca => (parca.Id, parca.Name ?? string.Empty))
                    .ToArray() ?? Array.Empty<(int Id, string Ad)>();
            }
            catch
            {
                return Array.Empty<(int Id, string Ad)>();
            }
        }
    }

    public int AltyaziParcasi
    {
        get { try { return MediaPlayer.Spu; } catch { return -1; } }
        set { try { MediaPlayer.SetSpu(value); } catch { } }
    }

    public bool HaricAltyaziEkle(string yol)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(yol) &&
                MediaPlayer.AddSlave(MediaSlaveType.Subtitle, new Uri(Path.GetFullPath(yol)).AbsoluteUri, true);
        }
        catch
        {
            return false;
        }
    }

    public void EnBoyOraniSec(string oran)
    {
        string? libVlcOrani = oran switch
        {
            "2.21:1" => "221:100",
            "2.35:1" => "235:100",
            "2.39:1" => "239:100",
            "Varsayılan" => null,
            _ => oran
        };
        try { MediaPlayer.AspectRatio = libVlcOrani; }
        catch { }
    }

    public bool EkranaSigdir
    {
        get { try { return MediaPlayer.Scale == 0; } catch { return false; } }
        set { try { MediaPlayer.Scale = value ? 0 : 1.0f; } catch { } }
    }

    public void Kirp(string geometri)
    {
        try { MediaPlayer.CropGeometry = string.IsNullOrWhiteSpace(geometri) ? null : geometri; }
        catch { }
    }

    public void BicimKaldir(string mod)
    {
        try
        {
            string? secilen = mod switch
            {
                "Kapalı" => null,
                "Otomatik" => "auto",
                "blend" or "bob" or "linear" or "yadif" or "yadif2x" or "x" or "mean" or "discard" => mod,
                _ => null
            };
            if (mod is not ("Kapalı" or "Otomatik" or "blend" or "bob" or "linear" or "yadif" or "yadif2x" or "x" or "mean" or "discard"))
            {
                return;
            }
            MediaPlayer.SetDeinterlace(secilen);
        }
        catch { }
    }

    public double Parlaklik
    {
        get => AyarOku(VideoAdjustOption.Brightness);
        set => AyarYaz(VideoAdjustOption.Brightness, value);
    }

    public double Kontrast
    {
        get => AyarOku(VideoAdjustOption.Contrast);
        set => AyarYaz(VideoAdjustOption.Contrast, value);
    }

    public double Doygunluk
    {
        get => AyarOku(VideoAdjustOption.Saturation);
        set => AyarYaz(VideoAdjustOption.Saturation, value);
    }

    public double Ton
    {
        get => AyarOku(VideoAdjustOption.Hue);
        set => AyarYaz(VideoAdjustOption.Hue, value);
    }

    public double Gama
    {
        get => AyarOku(VideoAdjustOption.Gamma);
        set => AyarYaz(VideoAdjustOption.Gamma, value);
    }

    public bool Sessiz
    {
        get { try { return MediaPlayer.Mute; } catch { return false; } }
        set { try { MediaPlayer.Mute = value; } catch { } }
    }

    public long SesGecikmesiMs
    {
        get { try { return MediaPlayer.AudioDelay / 1000; } catch { return 0; } }
        set { try { MediaPlayer.SetAudioDelay(value * 1000); } catch { } }
    }

    public long AltyaziGecikmesiMs
    {
        get { try { return MediaPlayer.SpuDelay / 1000; } catch { return 0; } }
        set { try { MediaPlayer.SetSpuDelay(value * 1000); } catch { } }
    }

    public bool Dongu
    {
        get => _dongu;
        set => _dongu = value;
    }

    public string ABDurumu =>
        _aZamaniMs is null ? "A-B: Kapalı" :
        _bZamaniMs is null ? $"A: {_aZamaniMs.Value / 1000.0:0.0} sn" :
        $"A-B: {_aZamaniMs.Value / 1000.0:0.0}–{_bZamaniMs.Value / 1000.0:0.0} sn";

    public void ABIsaretle()
    {
        try
        {
            if (_aZamaniMs is null)
            {
                _aZamaniMs = Math.Max(0, MediaPlayer.Time);
                _bZamaniMs = null;
            }
            else if (_bZamaniMs is null)
            {
                long b = Math.Max(0, MediaPlayer.Time);
                if (b <= _aZamaniMs.Value)
                {
                    _aZamaniMs = b;
                    return;
                }

                _bZamaniMs = b;
            }
            else
            {
                _aZamaniMs = null;
                _bZamaniMs = null;
            }
        }
        catch { }
    }

    public void SonrakiKare()
    {
        try
        {
            if (!MediaPlayer.IsPlaying)
            {
                MediaPlayer.NextFrame();
                return;
            }

            MediaPlayer.Pause();
            _ = DuraklamaSonrasiKareyiIlerle();
        }
        catch { }
    }

    private async Task DuraklamaSonrasiKareyiIlerle()
    {
        try
        {
            await Task.Delay(100);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (!_disposed)
                    {
                        MediaPlayer.NextFrame();
                    }
                }
                catch { }
            });
        }
        catch { }
    }

    public void Dur()
    {
        try { MediaPlayer.Stop(); }
        catch { }
    }

    public bool AnlikGoruntuAl(string hedefDosya)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hedefDosya))
            {
                hedefDosya = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Pictures",
                    "VideoPortal",
                    $"video-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
            }

            string tamYol = Path.GetFullPath(hedefDosya);
            string? dizin = Path.GetDirectoryName(tamYol);
            if (!string.IsNullOrEmpty(dizin))
            {
                Directory.CreateDirectory(dizin);
            }

            return MediaPlayer.TakeSnapshot(0, tamYol, 0, 0);
        }
        catch
        {
            return false;
        }
    }

    public bool EkolayzerSec(string onAyar)
    {
        try
        {
            int indeks = Array.FindIndex(Ekolayzer,
                ad => string.Equals(ad, onAyar, StringComparison.OrdinalIgnoreCase));
            if (indeks < 0)
            {
                return false;
            }

            using var equalizer = new Equalizer((uint)indeks);
            return MediaPlayer.SetEqualizer(equalizer);
        }
        catch
        {
            return false;
        }
    }

    public bool EkolayzerKapat()
    {
        try { return MediaPlayer.UnsetEqualizer(); }
        catch { return false; }
    }

    private double AyarOku(VideoAdjustOption ayar)
    {
        try
        {
            MediaPlayer.SetAdjustInt(VideoAdjustOption.Enable, 1);
            return MediaPlayer.AdjustFloat(ayar);
        }
        catch
        {
            return ayar == VideoAdjustOption.Hue ? 0.0 : 1.0;
        }
    }

    private void AyarYaz(VideoAdjustOption ayar, double deger)
    {
        try
        {
            MediaPlayer.SetAdjustInt(VideoAdjustOption.Enable, 1);
            MediaPlayer.SetAdjustFloat(ayar, (float)deger);
        }
        catch { }
    }

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        long? onceki = _oncekiZamanMs;
        _oncekiZamanMs = e.Time;
        if (_bZamaniMs is long b && _aZamaniMs is long a && !_abAtlamaYapiliyor && e.Time >= b)
        {
            if (onceki is long oncekiZamani && oncekiZamani < b && Math.Abs(e.Time - b) < 750)
            {
                _abAtlamaYapiliyor = true;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        if (!_disposed && _aZamaniMs == a && _bZamaniMs == b)
                        {
                            Ara(a / 1000.0);
                        }
                    }
                    catch { }
                    finally { _abAtlamaYapiliyor = false; }
                });
            }
            else
            {
                _aZamaniMs = null;
                _bZamaniMs = null;
            }
        }
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        if (_dongu)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (!_disposed && _dongu)
                    {
                        MediaPlayer.Time = 0;
                        MediaPlayer.Play();
                    }
                }
                catch { }
            });
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        MediaPlayer.TimeChanged -= OnTimeChanged;
        MediaPlayer.EndReached -= OnEndReached;
        try
        {
            MediaPlayer.Stop();
            MediaPlayer.Media = null;
        }
        catch
        {
            // Oynatıcı durdurma hatasını yut
        }

        Medya?.Dispose();
        Medya = null;

        if (_kendiPlayer)
        {
            MediaPlayer.Dispose();
        }

        if (_kendiLibVlc)
        {
            LibVlc.Dispose();
        }
    }
}
