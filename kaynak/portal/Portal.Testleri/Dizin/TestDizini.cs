namespace Portal.Testleri.Dizin;

using Microsoft.Data.Sqlite;
using Portal.Cekirdek;
using Portal.Cekirdek.Dizin;

/// <summary>
/// Dizin veritabanı ve anlık görüntü testleri için geçici ortam yardımcısı.
/// </summary>
public sealed class TestDizini : IDisposable
{
    public string KokKlasor { get; }
    public string YayinKlasoru { get; }
    public string YerelKlasor { get; }
    public string DbYolu { get; }

    /// <summary>
    /// Geçici dizin ortamını hazırlar ve şemayı kurar.
    /// </summary>
    public TestDizini(string? surum = "1", bool guncelYaz = true)
    {
        KokKlasor = Path.Combine(Path.GetTempPath(), "bulgu_test_" + Guid.NewGuid().ToString("N"));
        YayinKlasoru = Path.Combine(KokKlasor, "yayin");
        YerelKlasor = Path.Combine(KokKlasor, "yerel");

        Directory.CreateDirectory(YayinKlasoru);
        Directory.CreateDirectory(YerelKlasor);

        string dbAdi = "dizin-20261030-000000-000001.db";
        DbYolu = Path.Combine(YayinKlasoru, dbAdi);

        SemaKur(DbYolu, surum);

        if (guncelYaz)
        {
            GuncelTxtYaz(dbAdi);
        }
    }

    /// <summary>
    /// guncel.txt içeriğini yazar.
    /// </summary>
    public void GuncelTxtYaz(string icerik)
    {
        File.WriteAllText(Path.Combine(YayinKlasoru, "guncel.txt"), icerik);
    }

    /// <summary>
    /// Yayın klasöründe yeni bir anlık görüntü veritabanı kurar.
    /// </summary>
    public string YeniDbOlustur(string dbAdi, string? surum = "1")
    {
        string yeniDbYolu = Path.Combine(YayinKlasoru, dbAdi);
        SemaKur(yeniDbYolu, surum);
        return yeniDbYolu;
    }

    /// <summary>
    /// Bu dizin ortamına bağlı DizinKaynagi üretir.
    /// </summary>
    public DizinKaynagi KaynakOlustur()
    {
        return new DizinKaynagi(KokKlasor, YerelKlasor);
    }

    /// <summary>
    /// Test veritabanını doğrudan salt okur olarak açar.
    /// </summary>
    public DizinKaynagi DosyadanKaynakAc()
    {
        return DizinKaynagi.DosyadanAc(DbYolu);
    }

    /// <summary>
    /// Veritabanına dosya kaydı ekler.
    /// </summary>
    public long DosyaEkle(
        string yol,
        string ad,
        string? klasor = null,
        string? uzanti = null,
        long boyut = 1024,
        DateTime? degisme = null,
        string tur = "belge",
        string durum = "tamam",
        string? sha = null,
        int? sayfaSayisi = null,
        string? ders = null,
        string? hata = null,
        int? ocrSayfa = null,
        string? hedefDbYolu = null)
    {
        string dbHedef = hedefDbYolu ?? DbYolu;
        klasor ??= Path.GetDirectoryName(yol) ?? string.Empty;

        if (uzanti == null)
        {
            string ext = Path.GetExtension(ad);
            uzanti = ext.StartsWith('.') ? ext[1..].ToLowerInvariant() : ext.ToLowerInvariant();
        }

        long degismeUnix = degisme.HasValue
            ? new DateTimeOffset(degisme.Value.ToUniversalTime()).ToUnixTimeSeconds()
            : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        string normAd = DosyaNormalizasyon.Normalle(ad);
        string normYol = DosyaNormalizasyon.Normalle(yol);

        using var conn = new SqliteConnection($"Data Source={dbHedef}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO dosya (yol, ad, klasor, uzanti, boyut, degisme, tur, durum, hata, sha, sayfa_sayisi, ocr_sayfa, ders, norm_ad, norm_yol)
            VALUES (@yol, @ad, @klasor, @uzanti, @boyut, @degisme, @tur, @durum, @hata, @sha, @sayfa_sayisi, @ocr_sayfa, @ders, @norm_ad, @norm_yol);
            SELECT last_insert_rowid();";

        cmd.Parameters.AddWithValue("@yol", yol);
        cmd.Parameters.AddWithValue("@ad", ad);
        cmd.Parameters.AddWithValue("@klasor", klasor);
        cmd.Parameters.AddWithValue("@uzanti", uzanti);
        cmd.Parameters.AddWithValue("@boyut", boyut);
        cmd.Parameters.AddWithValue("@degisme", degismeUnix);
        cmd.Parameters.AddWithValue("@tur", tur);
        cmd.Parameters.AddWithValue("@durum", durum);
        cmd.Parameters.AddWithValue("@hata", (object?)hata ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sha", (object?)sha ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sayfa_sayisi", (object?)sayfaSayisi ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ocr_sayfa", (object?)ocrSayfa ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ders", (object?)ders ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@norm_ad", normAd);
        cmd.Parameters.AddWithValue("@norm_yol", normYol);

        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>
    /// Veritabanına sayfa kaydı ve sayfa_fts satırı ekler.
    /// </summary>
    public long SayfaEkle(
        long dosyaId,
        int no,
        string etiket,
        string metin,
        string kaynak = "metin",
        double? guven = null,
        string? hedefDbYolu = null)
    {
        string dbHedef = hedefDbYolu ?? DbYolu;
        string norm = TrNormalizasyon.Normalle(metin);

        using var conn = new SqliteConnection($"Data Source={dbHedef}");
        conn.Open();
        using var tx = conn.BeginTransaction();

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO sayfa (dosya_id, no, etiket, kaynak, guven, metin)
            VALUES (@dosya_id, @no, @etiket, @kaynak, @guven, @metin);
            SELECT last_insert_rowid();";

        cmd.Parameters.AddWithValue("@dosya_id", dosyaId);
        cmd.Parameters.AddWithValue("@no", no);
        cmd.Parameters.AddWithValue("@etiket", etiket);
        cmd.Parameters.AddWithValue("@kaynak", kaynak);
        cmd.Parameters.AddWithValue("@guven", (object?)guven ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@metin", metin);

        long sayfaId = (long)cmd.ExecuteScalar()!;

        using var ftsCmd = conn.CreateCommand();
        ftsCmd.Transaction = tx;
        ftsCmd.CommandText = "INSERT INTO sayfa_fts(rowid, norm) VALUES (@rowid, @norm);";
        ftsCmd.Parameters.AddWithValue("@rowid", sayfaId);
        ftsCmd.Parameters.AddWithValue("@norm", norm);
        ftsCmd.ExecuteNonQuery();

        tx.Commit();
        return sayfaId;
    }

    private static void SemaKur(string dbYolu, string? surum)
    {
        string semaYolu = Path.Combine(AppContext.BaseDirectory, "ortak", "dizin_sema.sql");
        string semaSql;
        if (File.Exists(semaYolu))
        {
            semaSql = File.ReadAllText(semaYolu);
        }
        else
        {
            string aday = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "ortak", "dizin_sema.sql"));
            if (File.Exists(aday))
            {
                semaSql = File.ReadAllText(aday);
            }
            else
            {
                throw new FileNotFoundException("dizin_sema.sql bulunamadı: " + semaYolu);
            }
        }

        using var conn = new SqliteConnection($"Data Source={dbYolu}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = semaSql;
        cmd.ExecuteNonQuery();

        if (surum != null && surum != "1")
        {
            using var cmdSurum = conn.CreateCommand();
            cmdSurum.CommandText = "INSERT OR REPLACE INTO meta(anahtar, deger) VALUES ('surum', @surum)";
            cmdSurum.Parameters.AddWithValue("@surum", surum);
            cmdSurum.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Bağlantı havuzlarını boşaltır ve geçici test klasörünü temizler.
    /// </summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        for (int i = 0; i < 5; i++)
        {
            try
            {
                if (Directory.Exists(KokKlasor))
                {
                    Directory.Delete(KokKlasor, true);
                }
                break;
            }
            catch (Exception)
            {
                Thread.Sleep(50);
            }
        }
    }
}
