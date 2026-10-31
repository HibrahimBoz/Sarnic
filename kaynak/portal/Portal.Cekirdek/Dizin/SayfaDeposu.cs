namespace Portal.Cekirdek.Dizin;

using Microsoft.Data.Sqlite;

/// <summary>
/// Belge sayfasına ait metin ve üst veri kaydı.
/// </summary>
public sealed record SayfaKaydi(long Id, long DosyaId, int No, string Etiket, string Kaynak, double? Guven, string Metin, string? Dil = null, string? Ceviri = null);

/// <summary>
/// Belge sayfalarını ve üst bilgilerini dizinden çeken depo.
/// </summary>
public sealed class SayfaDeposu
{
    private readonly DizinKaynagi _kaynak;

    public SayfaDeposu(DizinKaynagi kaynak)
    {
        _kaynak = kaynak;
    }

    /// <summary>
    /// Bir dosyaya ait tüm sayfaları sayfa numarası sırasıyla getirir.
    /// </summary>
    public IReadOnlyList<SayfaKaydi> Getir(long dosyaId)
    {
        if (_kaynak.Durum != DizinDurumu.Hazir)
        {
            return Array.Empty<SayfaKaydi>();
        }

        using var conn = _kaynak.BaglantiAc();
        using var cmd = conn.CreateCommand();
        bool sutunlarVar = DilCeviriSutunlariVarMi(conn);
        cmd.CommandText = sutunlarVar
            ? "SELECT id, dosya_id, no, etiket, kaynak, guven, metin, dil, ceviri FROM sayfa WHERE dosya_id = @dosyaId ORDER BY no ASC;"
            : "SELECT id, dosya_id, no, etiket, kaynak, guven, metin FROM sayfa WHERE dosya_id = @dosyaId ORDER BY no ASC;";
        cmd.Parameters.AddWithValue("@dosyaId", dosyaId);

        var sonuclar = new List<SayfaKaydi>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            sonuclar.Add(OkuyucudanSayfa(reader));
        }

        return sonuclar;
    }

    /// <summary>
    /// Bir dosyaya ait belirli bir sayfayı getirir.
    /// </summary>
    public SayfaKaydi? Getir(long dosyaId, int no)
    {
        if (_kaynak.Durum != DizinDurumu.Hazir)
        {
            return null;
        }

        using var conn = _kaynak.BaglantiAc();
        using var cmd = conn.CreateCommand();
        bool sutunlarVar = DilCeviriSutunlariVarMi(conn);
        cmd.CommandText = sutunlarVar
            ? "SELECT id, dosya_id, no, etiket, kaynak, guven, metin, dil, ceviri FROM sayfa WHERE dosya_id = @dosyaId AND no = @no LIMIT 1;"
            : "SELECT id, dosya_id, no, etiket, kaynak, guven, metin FROM sayfa WHERE dosya_id = @dosyaId AND no = @no LIMIT 1;";
        cmd.Parameters.AddWithValue("@dosyaId", dosyaId);
        cmd.Parameters.AddWithValue("@no", no);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return OkuyucudanSayfa(reader);
        }

        return null;
    }

    /// <summary>
    /// Bir dosyanın toplam sayfa sayısını döner.
    /// </summary>
    public int SayfaSayisi(long dosyaId)
    {
        if (_kaynak.Durum != DizinDurumu.Hazir)
        {
            return 0;
        }

        using var conn = _kaynak.BaglantiAc();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sayfa WHERE dosya_id = @dosyaId;";
        cmd.Parameters.AddWithValue("@dosyaId", dosyaId);

        object? res = cmd.ExecuteScalar();
        if (res is long l)
        {
            return (int)l;
        }

        if (res is int i)
        {
            return i;
        }

        return 0;
    }

    /// <summary>
    /// Bir dosyanın üst bilgilerini döner.
    /// </summary>
    public DosyaSonucu? Dosya(long dosyaId)
    {
        if (_kaynak.Durum != DizinDurumu.Hazir)
        {
            return null;
        }

        using var conn = _kaynak.BaglantiAc();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT id, yol, ad, klasor, uzanti, boyut, degisme, tur, durum, sayfa_sayisi, ders, sha
FROM dosya
WHERE id = @dosyaId
LIMIT 1;";
        cmd.Parameters.AddWithValue("@dosyaId", dosyaId);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            long id = reader.GetInt64(0);
            string yol = reader.GetString(1);
            string ad = reader.GetString(2);
            string klasor = reader.GetString(3);
            string uzanti = reader.GetString(4);
            long boyut = reader.GetInt64(5);
            long degismeSec = reader.GetInt64(6);
            DateTime degisme = DateTimeOffset.FromUnixTimeSeconds(degismeSec).LocalDateTime;
            string tur = reader.GetString(7);
            string durum = reader.GetString(8);
            int? sayfaSayisi = reader.IsDBNull(9) ? null : (int?)reader.GetInt32(9);
            string? ders = reader.IsDBNull(10) ? null : reader.GetString(10);
            string? sha = reader.IsDBNull(11) ? null : reader.GetString(11);

            return new DosyaSonucu(id, yol, ad, klasor, uzanti, boyut, degisme, tur, durum, sayfaSayisi, ders, sha);
        }

        return null;
    }

    private static SayfaKaydi OkuyucudanSayfa(SqliteDataReader reader)
    {
        long id = reader.GetInt64(0);
        long dosyaId = reader.GetInt64(1);
        int no = reader.GetInt32(2);
        string etiket = reader.GetString(3);
        string kaynak = reader.GetString(4);
        double? guven = reader.IsDBNull(5) ? null : (double?)reader.GetDouble(5);
        string metin = reader.GetString(6);

        string? dil = reader.FieldCount > 7 && !reader.IsDBNull(7) ? reader.GetString(7) : null;
        string? ceviri = reader.FieldCount > 8 && !reader.IsDBNull(8) ? reader.GetString(8) : null;

        return new SayfaKaydi(id, dosyaId, no, etiket, kaynak, guven, metin, dil, ceviri);
    }

    // Yayımlanan dizin aynı yolda yeni şemayla değişebilir; sonuç önbelleğe alınmaz (PRAGMA ucuzdur).
    internal static bool DilCeviriSutunlariVarMi(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('sayfa') WHERE name IN ('dil', 'ceviri');";
        return Convert.ToInt64(cmd.ExecuteScalar()) == 2;
    }
}
