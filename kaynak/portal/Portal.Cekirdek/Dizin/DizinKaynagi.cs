namespace Portal.Cekirdek.Dizin;

using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

public enum DizinDurumu { Yok, Hazir, Hata }

/// <summary>
/// Bulgu SQLite dizin veritabanı anlık görüntüsünü yerel diske eşleyen ve salt-okur bağlantı sağlayan kaynak.
/// </summary>
public sealed class DizinKaynagi : IDisposable
{
    private static readonly CultureInfo TrCulture = new("tr-TR");

    private readonly SemaphoreSlim _yenilemeKilidi = new(1, 1);
    private readonly object _kilit = new();
    private readonly bool _dosyadanAcildi;
    private readonly string? _yerelKlasor;

    private volatile string? _etkinDosya;
    private volatile DizinDurumu _durum = DizinDurumu.Yok;
    private volatile string _ileti = "Dizin yükleniyor";
    private string? _sonBasariliIleti;
    private DateTime? _yayinZamani;

    /// <summary>
    /// Ağdaki veya yereldeki dizin klasörünü izleyen DizinKaynagi oluşturur.
    /// Kurucuda eşzamanlı yenileme yapılmaz; durum Yok ve ileti 'Dizin yükleniyor' olarak başlar.
    /// </summary>
    public DizinKaynagi(string dizinKlasoru, string? yerelKlasor = null)
    {
        DizinKlasoru = dizinKlasoru;
        _yerelKlasor = yerelKlasor ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Bulgu",
            "dizin");
        _dosyadanAcildi = false;
        _durum = DizinDurumu.Yok;
        _ileti = "Dizin yükleniyor";
    }

    private DizinKaynagi(string dbYolu, bool dosyadan)
    {
        DizinKlasoru = Path.GetDirectoryName(Path.GetFullPath(dbYolu)) ?? string.Empty;
        _dosyadanAcildi = dosyadan;
        _yerelKlasor = null;

        if (!File.Exists(dbYolu))
        {
            _durum = DizinDurumu.Hata;
            _ileti = "Veritabanı dosyası bulunamadı.";
            return;
        }

        string tamYol = Path.GetFullPath(dbYolu);
        var semaSonuc = SemayiDogrulaVeSay(tamYol);
        if (semaSonuc.Hata != null)
        {
            _durum = DizinDurumu.Hata;
            _ileti = semaSonuc.Hata;
            return;
        }

        _etkinDosya = tamYol;
        _yayinZamani = File.GetLastWriteTime(tamYol);
        _durum = DizinDurumu.Hazir;
        _sonBasariliIleti = $"{semaSonuc.DosyaSayisi.ToString("N0", TrCulture)} dosya, {semaSonuc.SayfaSayisi.ToString("N0", TrCulture)} sayfa";
        _ileti = _sonBasariliIleti;
    }

    /// <summary>
    /// Test veya araç kolaylığı için var olan bir .db dosyasını kopyalamadan salt-okur açar.
    /// </summary>
    public static DizinKaynagi DosyadanAc(string dbYolu)
    {
        return new DizinKaynagi(dbYolu, true);
    }

    public string DizinKlasoru { get; }

    public DizinDurumu Durum => _durum;

    public string Ileti => _ileti;

    public string? EtkinDosya => _etkinDosya;

    public DateTime? YayinZamani
    {
        get
        {
            lock (_kilit)
            {
                return _yayinZamani;
            }
        }
    }

    /// <summary>
    /// guncel.txt dosyasını denetler, yeni anlık görüntü varsa yerel klasöre kopyalar ve etkin yolu atomik günceller.
    /// Gövde arka planda (Task.Run) ve SemaphoreSlim kilidi altında çalışır.
    /// </summary>
    public async Task<bool> YenileAsync(CancellationToken iptal = default)
    {
        if (_dosyadanAcildi)
        {
            return false;
        }

        iptal.ThrowIfCancellationRequested();

        await _yenilemeKilidi.WaitAsync(iptal);
        try
        {
            return await Task.Run(() => YenileGovdesi(iptal), iptal);
        }
        finally
        {
            _yenilemeKilidi.Release();
        }
    }

    private bool YenileGovdesi(CancellationToken iptal)
    {
        iptal.ThrowIfCancellationRequested();

        string? guncelTxt = BulGuncelTxt();
        if (guncelTxt == null || !File.Exists(guncelTxt))
        {
            lock (_kilit)
            {
                if (_etkinDosya != null && File.Exists(_etkinDosya))
                {
                    _durum = DizinDurumu.Hazir;
                    _ileti = _sonBasariliIleti != null
                        ? $"{_sonBasariliIleti} (Uyarı: Dizin bulunamadı)"
                        : "Dizin bulunamadı.";
                }
                else
                {
                    _durum = DizinDurumu.Yok;
                    _ileti = "Dizin bulunamadı. Bulgu İşleyici'de dizinle.bat'ı çalıştırın.";
                }
            }

            return false;
        }

        string dbAdi;
        try
        {
            dbAdi = File.ReadAllText(guncelTxt).Trim();
        }
        catch (Exception ex)
        {
            lock (_kilit)
            {
                if (_etkinDosya != null && File.Exists(_etkinDosya))
                {
                    _durum = DizinDurumu.Hazir;
                    _ileti = _sonBasariliIleti != null
                        ? $"{_sonBasariliIleti} (Uyarı: guncel.txt okunamadı)"
                        : $"guncel.txt okunamadı: {ex.Message}";
                }
                else
                {
                    _durum = DizinDurumu.Hata;
                    _ileti = $"guncel.txt okunamadı: {ex.Message}";
                }
            }

            return false;
        }

        if (string.IsNullOrEmpty(dbAdi) ||
            Path.GetFileName(dbAdi) != dbAdi ||
            dbAdi.Contains(':') ||
            dbAdi.Contains('/') ||
            dbAdi.Contains('\\') ||
            dbAdi.Contains("..") ||
            !dbAdi.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
        {
            lock (_kilit)
            {
                if (_etkinDosya != null && File.Exists(_etkinDosya))
                {
                    _durum = DizinDurumu.Hazir;
                    _ileti = _sonBasariliIleti != null
                        ? $"{_sonBasariliIleti} (Uyarı: guncel.txt geçersiz dosya adı içeriyor)"
                        : "guncel.txt geçersiz dosya adı içeriyor.";
                }
                else
                {
                    _durum = DizinDurumu.Hata;
                    _ileti = "guncel.txt geçersiz dosya adı içeriyor.";
                }
            }

            return false;
        }

        string yayinKlasoru = Path.GetDirectoryName(guncelTxt)!;
        string kaynakDb = Path.Combine(yayinKlasoru, dbAdi);

        if (!File.Exists(kaynakDb))
        {
            lock (_kilit)
            {
                if (_etkinDosya != null && File.Exists(_etkinDosya))
                {
                    _durum = DizinDurumu.Hazir;
                    _ileti = _sonBasariliIleti != null
                        ? $"{_sonBasariliIleti} (Uyarı: Yayınlanan veritabanı bulunamadı)"
                        : "Yayınlanan veritabanı dosyası bulunamadı.";
                }
                else
                {
                    _durum = DizinDurumu.Hata;
                    _ileti = "Yayınlanan veritabanı dosyası bulunamadı.";
                }
            }

            return false;
        }

        Directory.CreateDirectory(_yerelKlasor!);
        string hedefDb = Path.Combine(_yerelKlasor!, dbAdi);

        var kaynakFi = new FileInfo(kaynakDb);
        bool ayniDosyaVar = File.Exists(hedefDb) && new FileInfo(hedefDb).Length == kaynakFi.Length;

        if (_etkinDosya == hedefDb && ayniDosyaVar)
        {
            if (_durum != DizinDurumu.Hazir)
            {
                var kurtarSema = SemayiDogrulaVeSay(hedefDb);
                if (kurtarSema.Hata == null)
                {
                    lock (_kilit)
                    {
                        _durum = DizinDurumu.Hazir;
                        _sonBasariliIleti = $"{kurtarSema.DosyaSayisi.ToString("N0", TrCulture)} dosya, {kurtarSema.SayfaSayisi.ToString("N0", TrCulture)} sayfa";
                        _ileti = _sonBasariliIleti;
                        _yayinZamani = File.GetLastWriteTime(hedefDb);
                    }
                }
            }
            else if (_sonBasariliIleti != null)
            {
                // Geçici hatadan sonra aynı anlık görüntü yeniden ulaşılabilir: eski uyarıyı temizle
                _ileti = _sonBasariliIleti;
            }
            return false;
        }

        if (!ayniDosyaVar)
        {
            string tmpYol = $"{hedefDb}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.Copy(kaynakDb, tmpYol, true);
                if (new FileInfo(tmpYol).Length != kaynakFi.Length)
                {
                    try { File.Delete(tmpYol); } catch { }
                    lock (_kilit)
                    {
                        if (_etkinDosya != null && File.Exists(_etkinDosya))
                        {
                            _durum = DizinDurumu.Hazir;
                            _ileti = _sonBasariliIleti != null
                                ? $"{_sonBasariliIleti} (Uyarı: Kopyalama boyutu uyuşmuyor)"
                                : "Kopyalama boyutu uyuşmuyor.";
                        }
                        else
                        {
                            _durum = DizinDurumu.Hata;
                            _ileti = "Kopyalama boyutu uyuşmuyor.";
                        }
                    }

                    return false;
                }

                File.Move(tmpYol, hedefDb, true);
            }
            catch (Exception ex)
            {
                try { if (File.Exists(tmpYol)) File.Delete(tmpYol); } catch { }
                lock (_kilit)
                {
                    if (_etkinDosya != null && File.Exists(_etkinDosya))
                    {
                        _durum = DizinDurumu.Hazir;
                        _ileti = _sonBasariliIleti != null
                            ? $"{_sonBasariliIleti} (Uyarı: Yerel kopya oluşturulamadı)"
                            : $"Yerel kopya oluşturulamadı: {ex.Message}";
                    }
                    else
                    {
                        _durum = DizinDurumu.Hata;
                        _ileti = $"Yerel kopya oluşturulamadı: {ex.Message}";
                    }
                }

                return false;
            }
        }

        var semaSonuc = SemayiDogrulaVeSay(hedefDb);
        if (semaSonuc.Hata != null)
        {
            lock (_kilit)
            {
                _durum = DizinDurumu.Hata;
                _ileti = semaSonuc.Hata;
            }

            return false;
        }

        lock (_kilit)
        {
            _etkinDosya = hedefDb;
            _yayinZamani = File.GetLastWriteTime(hedefDb);
            _durum = DizinDurumu.Hazir;
            _sonBasariliIleti = $"{semaSonuc.DosyaSayisi.ToString("N0", TrCulture)} dosya, {semaSonuc.SayfaSayisi.ToString("N0", TrCulture)} sayfa";
            _ileti = _sonBasariliIleti;
        }

        EskiYerelKopyalariTemizle(dbAdi);
        return true;
    }

    /// <summary>
    /// Etkin veritabanına salt-okur bağlantı açar. Çağıran Dispose eder.
    /// </summary>
    public SqliteConnection BaglantiAc()
    {
        string? dosya = _etkinDosya;
        if (_durum != DizinDurumu.Hazir || string.IsNullOrEmpty(dosya))
        {
            throw new InvalidOperationException("Dizin hazır durumda değil.");
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dosya,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };
        var baglanti = new SqliteConnection(builder.ConnectionString);
        baglanti.Open();
        return baglanti;
    }

    /// <summary>
    /// Dizindeki toplam dosya ve sayfa sayısını döner.
    /// </summary>
    public (long Dosya, long Sayfa) Sayimlar()
    {
        if (_durum != DizinDurumu.Hazir || string.IsNullOrEmpty(_etkinDosya))
        {
            return (0, 0);
        }

        try
        {
            using var baglanti = BaglantiAc();
            long dosya = CalistirSkalar(baglanti, "SELECT COUNT(*) FROM dosya;");
            long sayfa = CalistirSkalar(baglanti, "SELECT COUNT(*) FROM sayfa;");
            return (dosya, sayfa);
        }
        catch
        {
            return (0, 0);
        }
    }

    public void Dispose()
    {
        _yenilemeKilidi.Dispose();
    }

    private string? BulGuncelTxt()
    {
        if (string.IsNullOrEmpty(DizinKlasoru))
        {
            return null;
        }

        string yayinGuncel = Path.Combine(DizinKlasoru, "yayin", "guncel.txt");
        if (File.Exists(yayinGuncel))
        {
            return yayinGuncel;
        }

        string kokGuncel = Path.Combine(DizinKlasoru, "guncel.txt");
        if (File.Exists(kokGuncel))
        {
            return kokGuncel;
        }

        return null;
    }

    private static (long DosyaSayisi, long SayfaSayisi, string? Hata) SemayiDogrulaVeSay(string dbYolu)
    {
        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = dbYolu,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            };
            using var conn = new SqliteConnection(builder.ConnectionString);
            conn.Open();

            string? surum = null;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT deger FROM meta WHERE anahtar = 'surum';";
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    surum = reader.GetString(0);
                }
            }

            if (surum != "1")
            {
                return (0, 0, $"Desteklenmeyen şema sürümü: {surum ?? "yok"}. Beklenen: 1.");
            }

            long dosya = CalistirSkalar(conn, "SELECT COUNT(*) FROM dosya;");
            long sayfa = CalistirSkalar(conn, "SELECT COUNT(*) FROM sayfa;");
            return (dosya, sayfa, null);
        }
        catch (Exception ex)
        {
            return (0, 0, $"Veritabanı açılamadı veya şema sürümü okunamadı: {ex.Message}");
        }
    }

    private static long CalistirSkalar(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        object? obj = cmd.ExecuteScalar();
        if (obj is long l)
        {
            return l;
        }

        if (obj is int i)
        {
            return i;
        }

        if (obj != null && long.TryParse(obj.ToString(), out long parsed))
        {
            return parsed;
        }

        return 0;
    }

    private void EskiYerelKopyalariTemizle(string guncelDbAdi)
    {
        if (string.IsNullOrEmpty(_yerelKlasor) || !Directory.Exists(_yerelKlasor))
        {
            return;
        }

        try
        {
            var birSaatOnce = DateTime.UtcNow.AddHours(-1);

            foreach (string dosya in Directory.GetFiles(_yerelKlasor, "*.db"))
            {
                string ad = Path.GetFileName(dosya);
                if (!ad.Equals(guncelDbAdi, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(dosya) < birSaatOnce)
                        {
                            File.Delete(dosya);
                        }
                    }
                    catch
                    {
                        // Kilitliyse veya erişim hatası varsa yutulur
                    }
                }
            }

            foreach (string dosya in Directory.GetFiles(_yerelKlasor, "*.tmp"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(dosya) < birSaatOnce)
                    {
                        File.Delete(dosya);
                    }
                }
                catch
                {
                    // Hata yutulur
                }
            }
        }
        catch
        {
            // Temizlik hataları yutulur
        }
    }
}
