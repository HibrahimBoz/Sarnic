namespace Portal.Cekirdek.Dizin;

using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

public enum DizinDurumu { Yok, Hazir, Hata }

/// <summary>
/// Bulgu SQLite dizin veritabanı anlık görüntüsünü yerel diske eşleyen ve salt-okur bağlantı sağlayan kaynak.
/// </summary>
public sealed class DizinKaynagi : IDisposable
{
    private static readonly CultureInfo TrCulture = new("tr-TR");

    private readonly object _kilit = new();
    private readonly bool _dosyadanAcildi;
    private readonly string? _yerelKlasor;

    private volatile string? _etkinDosya;
    private volatile DizinDurumu _durum = DizinDurumu.Yok;
    private volatile string _ileti = string.Empty;
    private DateTime? _yayinZamani;

    /// <summary>
    /// Ağdaki veya yereldeki dizin klasörünü izleyen DizinKaynagi oluşturur.
    /// </summary>
    public DizinKaynagi(string dizinKlasoru, string? yerelKlasor = null)
    {
        DizinKlasoru = dizinKlasoru;
        _yerelKlasor = yerelKlasor ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Bulgu",
            "dizin");
        _dosyadanAcildi = false;

        YenileAsync().GetAwaiter().GetResult();
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
        _ileti = $"{semaSonuc.DosyaSayisi.ToString("N0", TrCulture)} dosya, {semaSonuc.SayfaSayisi.ToString("N0", TrCulture)} sayfa";
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
    /// </summary>
    public Task<bool> YenileAsync(CancellationToken iptal = default)
    {
        if (_dosyadanAcildi)
        {
            return Task.FromResult(false);
        }

        iptal.ThrowIfCancellationRequested();

        string? guncelTxt = BulGuncelTxt();
        if (guncelTxt == null || !File.Exists(guncelTxt))
        {
            lock (_kilit)
            {
                _durum = DizinDurumu.Yok;
                _ileti = "Dizin bulunamadı. Bulgu İşleyici'de dizinle.bat'ı çalıştırın.";
            }

            return Task.FromResult(false);
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
                _durum = DizinDurumu.Hata;
                _ileti = $"guncel.txt okunamadı: {ex.Message}";
            }

            return Task.FromResult(false);
        }

        if (string.IsNullOrEmpty(dbAdi) ||
            dbAdi.Contains('/') ||
            dbAdi.Contains('\\') ||
            dbAdi.Contains(".."))
        {
            lock (_kilit)
            {
                _durum = DizinDurumu.Hata;
                _ileti = "guncel.txt geçersiz dosya adı içeriyor.";
            }

            return Task.FromResult(false);
        }

        string yayinKlasoru = Path.GetDirectoryName(guncelTxt)!;
        string kaynakDb = Path.Combine(yayinKlasoru, dbAdi);

        if (!File.Exists(kaynakDb))
        {
            lock (_kilit)
            {
                _durum = DizinDurumu.Hata;
                _ileti = "Yayınlanan veritabanı dosyası bulunamadı.";
            }

            return Task.FromResult(false);
        }

        Directory.CreateDirectory(_yerelKlasor!);
        string hedefDb = Path.Combine(_yerelKlasor!, dbAdi);

        var kaynakFi = new FileInfo(kaynakDb);
        bool ayniDosyaVar = File.Exists(hedefDb) && new FileInfo(hedefDb).Length == kaynakFi.Length;

        if (_etkinDosya == hedefDb && ayniDosyaVar)
        {
            return Task.FromResult(false);
        }

        if (!ayniDosyaVar)
        {
            string tmpYol = hedefDb + ".tmp";
            try
            {
                File.Copy(kaynakDb, tmpYol, true);
                if (new FileInfo(tmpYol).Length != kaynakFi.Length)
                {
                    try { File.Delete(tmpYol); } catch { }
                    lock (_kilit)
                    {
                        _durum = DizinDurumu.Hata;
                        _ileti = "Kopyalama boyutu uyuşmuyor.";
                    }

                    return Task.FromResult(false);
                }

                File.Move(tmpYol, hedefDb, true);
            }
            catch (Exception ex)
            {
                lock (_kilit)
                {
                    _durum = DizinDurumu.Hata;
                    _ileti = $"Yerel kopya oluşturulamadı: {ex.Message}";
                }

                return Task.FromResult(false);
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

            return Task.FromResult(false);
        }

        lock (_kilit)
        {
            _etkinDosya = hedefDb;
            _yayinZamani = File.GetLastWriteTime(hedefDb);
            _durum = DizinDurumu.Hazir;
            _ileti = $"{semaSonuc.DosyaSayisi.ToString("N0", TrCulture)} dosya, {semaSonuc.SayfaSayisi.ToString("N0", TrCulture)} sayfa";
        }

        EskiYerelKopyalariTemizle(dbAdi);
        return Task.FromResult(true);
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

        var baglanti = new SqliteConnection($"Data Source={dosya};Mode=ReadOnly;Pooling=False");
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
        // Havuzsuz bağlantı kullanıldığı için açık bağlantı tutulmaz
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
            using var conn = new SqliteConnection($"Data Source={dbYolu};Mode=ReadOnly;Pooling=False");
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
            foreach (string dosya in Directory.GetFiles(_yerelKlasor, "*.db"))
            {
                string ad = Path.GetFileName(dosya);
                if (!ad.Equals(guncelDbAdi, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        File.Delete(dosya);
                    }
                    catch
                    {
                        // Kilitliyse hata yutulur
                    }
                }
            }

            foreach (string dosya in Directory.GetFiles(_yerelKlasor, "*.tmp"))
            {
                try
                {
                    File.Delete(dosya);
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
