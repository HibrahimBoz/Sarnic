namespace Portal.Cekirdek.Dizin;

using System.Text;
using Microsoft.Data.Sqlite;

public enum SiralamaAlani { Ilgi, Ad, Yol, Boyut, Tarih }

/// <summary>
/// Arama sonuçlarının sıralama ayarı.
/// </summary>
public sealed record Siralama(SiralamaAlani Alan = SiralamaAlani.Ilgi, bool Azalan = false);

/// <summary>
/// Dosya arama motorunun döndürdüğü dosya veya klasör sonucu.
/// </summary>
public sealed record DosyaSonucu(
    long Id,
    string Yol,
    string Ad,
    string Klasor,
    string Uzanti,
    long Boyut,
    DateTime Degisme,
    string Tur,
    string Durum,
    int? SayfaSayisi,
    string? Ders,
    string? Sha);

/// <summary>
/// Dosya kataloğu üzerinde ad, yol ve süzgeç tabanlı arama motoru.
/// </summary>
public sealed class DosyaAramaMotoru
{
    private readonly DizinKaynagi _kaynak;

    public DosyaAramaMotoru(DizinKaynagi kaynak)
    {
        _kaynak = kaynak;
    }

    /// <summary>
    /// Sorguya uyan dosyaları sıralı olarak döner.
    /// </summary>
    public IReadOnlyList<DosyaSonucu> Ara(
        DosyaSorgusu sorgu,
        Siralama? siralama = null,
        int enFazla = 1000,
        CancellationToken iptal = default)
    {
        if (_kaynak.Durum != DizinDurumu.Hazir || sorgu.Bos || enFazla <= 0)
        {
            return Array.Empty<DosyaSonucu>();
        }

        iptal.ThrowIfCancellationRequested();

        using var conn = _kaynak.BaglantiAc();
        using var cmd = conn.CreateCommand();

        using var reg = iptal.Register(() =>
        {
            try
            {
                var handle = conn.Handle;
                if (handle != null && !handle.IsInvalid && !handle.IsClosed)
                {
                    SQLitePCL.raw.sqlite3_interrupt(handle);
                }
            }
            catch { }
        });

        iptal.ThrowIfCancellationRequested();

        var whereListesi = new List<string>();
        int pIndex = 0;

        // Uzantilar
        if (sorgu.Uzantilar.Count > 0)
        {
            var uzParams = new List<string>();
            foreach (string uz in sorgu.Uzantilar)
            {
                string p = $"@uz{pIndex++}";
                cmd.Parameters.AddWithValue(p, uz);
                uzParams.Add(p);
            }

            whereListesi.Add($"uzanti IN ({string.Join(", ", uzParams)})");
        }

        // Turler
        if (sorgu.Turler.Count > 0)
        {
            var turParams = new List<string>();
            foreach (string tur in sorgu.Turler)
            {
                string p = $"@tur{pIndex++}";
                cmd.Parameters.AddWithValue(p, tur);
                turParams.Add(p);
            }

            whereListesi.Add($"tur IN ({string.Join(", ", turParams)})");
        }

        // Boyut
        if (sorgu.Boyut != null)
        {
            if (sorgu.Boyut.EnAz.HasValue)
            {
                string p = $"@bMin{pIndex++}";
                cmd.Parameters.AddWithValue(p, sorgu.Boyut.EnAz.Value);
                whereListesi.Add($"boyut >= {p}");
            }

            if (sorgu.Boyut.EnCok.HasValue)
            {
                string p = $"@bMax{pIndex++}";
                cmd.Parameters.AddWithValue(p, sorgu.Boyut.EnCok.Value);
                whereListesi.Add($"boyut <= {p}");
            }
        }

        // Tarih
        if (sorgu.Tarih != null)
        {
            if (sorgu.Tarih.Bas.HasValue)
            {
                long basSec = SafeToUnixTimeSeconds(sorgu.Tarih.Bas.Value);
                string p = $"@tBas{pIndex++}";
                cmd.Parameters.AddWithValue(p, basSec);
                whereListesi.Add($"degisme >= {p}");
            }

            if (sorgu.Tarih.Son.HasValue)
            {
                long sonSec = SafeToUnixTimeSeconds(sorgu.Tarih.Son.Value);
                string p = $"@tSon{pIndex++}";
                cmd.Parameters.AddWithValue(p, sonSec);
                whereListesi.Add($"degisme < {p}");
            }
        }

        // DersBagli
        if (sorgu.DersBagli == true)
        {
            whereListesi.Add("(ders IS NOT NULL AND ders != '')");
        }
        else if (sorgu.DersBagli == false)
        {
            whereListesi.Add("(ders IS NULL OR ders = '')");
        }

        // Icerik
        if (!string.IsNullOrWhiteSpace(sorgu.Icerik))
        {
            var kelimeler = TrNormalizasyon.Kelimeler(sorgu.Icerik);
            if (kelimeler.Length > 0)
            {
                string ftsIcerik = string.Join(" ", kelimeler.Select(k => $"\"{k.Replace("\"", "\"\"")}\"*"));
                string p = $"@icr{pIndex++}";
                cmd.Parameters.AddWithValue(p, ftsIcerik);
                bool ceviriVar;
                try
                {
                    ceviriVar = IcerikAramaMotoru.CeviriAramasiVarMi(conn);
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 9 || iptal.IsCancellationRequested)
                {
                    throw new OperationCanceledException("Sorgu iptal edildi.", ex, iptal);
                }

                string ceviriKosulu = ceviriVar
                    ? $" UNION SELECT rowid FROM sayfa_ceviri_fts WHERE sayfa_ceviri_fts MATCH {p}"
                    : string.Empty;
                whereListesi.Add($"id IN (SELECT DISTINCT dosya_id FROM sayfa WHERE id IN (SELECT rowid FROM sayfa_fts WHERE sayfa_fts MATCH {p}{ceviriKosulu}))");
            }
        }

        // Kosullar (VE - VEYA grupları)
        foreach (var veyaGrubu in sorgu.Kosullar)
        {
            if (veyaGrubu.Count == 0)
            {
                continue;
            }

            var orClauses = new List<string>();
            foreach (var terim in veyaGrubu)
            {
                if (string.IsNullOrEmpty(terim.Metin))
                {
                    continue;
                }

                string p = $"@term{pIndex++}";
                if (terim.Joker)
                {
                    string globPattern = terim.Metin.Replace("[", "[[]");
                    cmd.Parameters.AddWithValue(p, globPattern);
                    string col = terim.Alan == TerimAlani.Yol ? "norm_yol" : "norm_ad";
                    orClauses.Add(terim.Degil ? $"NOT ({col} GLOB {p})" : $"({col} GLOB {p})");
                }
                else if (terim.Alan == TerimAlani.Yol)
                {
                    string likeVal = $"%{EscapeLike(terim.Metin)}%";
                    cmd.Parameters.AddWithValue(p, likeVal);
                    orClauses.Add(terim.Degil
                        ? $"(norm_yol NOT LIKE {p} ESCAPE '\\')"
                        : $"(norm_yol LIKE {p} ESCAPE '\\')");
                }
                else
                {
                    // TerimAlani.Ad - 3+ karakter kod noktası (Unicode Rune) sayar
                    if (terim.Metin.EnumerateRunes().Count() >= 3)
                    {
                        string ftsExpr = $"\"{terim.Metin.Replace("\"", "\"\"")}\"";
                        cmd.Parameters.AddWithValue(p, ftsExpr);
                        orClauses.Add(terim.Degil
                            ? $"(id NOT IN (SELECT rowid FROM dosya_fts WHERE dosya_fts MATCH {p}))"
                            : $"(id IN (SELECT rowid FROM dosya_fts WHERE dosya_fts MATCH {p}))");
                    }
                    else
                    {
                        string likeVal = $"%{EscapeLike(terim.Metin)}%";
                        cmd.Parameters.AddWithValue(p, likeVal);
                        orClauses.Add(terim.Degil
                            ? $"(norm_ad NOT LIKE {p} ESCAPE '\\')"
                            : $"(norm_ad LIKE {p} ESCAPE '\\')");
                    }
                }
            }

            if (orClauses.Count > 0)
            {
                whereListesi.Add($"({string.Join(" OR ", orClauses)})");
            }
        }

        // Siralama: Türkçe harfler için norm_ad / norm_yol ile sırala; Ilgi'de CASE ifadesi her zaman ASC
        siralama ??= new Siralama();
        string orderSql;
        if (siralama.Alan == SiralamaAlani.Ilgi)
        {
            string normTerim = DosyaNormalizasyon.Normalle(sorgu.DuzMetin);
            if (!string.IsNullOrEmpty(normTerim))
            {
                string pTam = $"@ilgiTam{pIndex++}";
                string pBas = $"@ilgiBas{pIndex++}";
                cmd.Parameters.AddWithValue(pTam, normTerim);
                cmd.Parameters.AddWithValue(pBas, $"{EscapeLike(normTerim)}%");

                string yon = siralama.Azalan ? "DESC" : "ASC";
                orderSql = $"ORDER BY CASE WHEN norm_ad = {pTam} THEN 1 WHEN norm_ad LIKE {pBas} ESCAPE '\\' THEN 2 ELSE 3 END ASC, norm_ad {yon}, ad {yon}";
            }
            else
            {
                orderSql = siralama.Azalan ? "ORDER BY norm_ad DESC, ad DESC" : "ORDER BY norm_ad ASC, ad ASC";
            }
        }
        else if (siralama.Alan == SiralamaAlani.Ad)
        {
            orderSql = siralama.Azalan ? "ORDER BY norm_ad DESC, ad DESC" : "ORDER BY norm_ad ASC, ad ASC";
        }
        else if (siralama.Alan == SiralamaAlani.Yol)
        {
            orderSql = siralama.Azalan ? "ORDER BY norm_yol DESC, yol DESC" : "ORDER BY norm_yol ASC, yol ASC";
        }
        else if (siralama.Alan == SiralamaAlani.Boyut)
        {
            orderSql = siralama.Azalan ? "ORDER BY boyut DESC, norm_ad ASC" : "ORDER BY boyut ASC, norm_ad ASC";
        }
        else
        {
            // Tarih
            orderSql = siralama.Azalan ? "ORDER BY degisme DESC, norm_ad ASC" : "ORDER BY degisme ASC, norm_ad ASC";
        }

        string whereSql = whereListesi.Count > 0 ? $"WHERE {string.Join(" AND ", whereListesi)}" : string.Empty;
        cmd.CommandText = $@"
SELECT id, yol, ad, klasor, uzanti, boyut, degisme, tur, durum, sayfa_sayisi, ders, sha
FROM dosya
{whereSql}
{orderSql}
LIMIT @enFazla;";
        cmd.Parameters.AddWithValue("@enFazla", enFazla);

        var sonuclar = new List<DosyaSonucu>();
        try
        {
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                iptal.ThrowIfCancellationRequested();
                sonuclar.Add(OkuyucudanDosyaSonucu(reader));
            }
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 9 || iptal.IsCancellationRequested)
        {
            throw new OperationCanceledException("Sorgu iptal edildi.", ex, iptal);
        }

        return sonuclar;
    }

    /// <summary>
    /// Belirtilen ders klasörüne bağlı olan dosyaları Türkçe harf duyarsız ve Ad sırasıyla getirir.
    /// </summary>
    public IReadOnlyList<DosyaSonucu> DersBelgeleri(string dersKlasoru)
    {
        if (_kaynak.Durum != DizinDurumu.Hazir || string.IsNullOrWhiteSpace(dersKlasoru))
        {
            return Array.Empty<DosyaSonucu>();
        }

        using var conn = _kaynak.BaglantiAc();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT id, yol, ad, klasor, uzanti, boyut, degisme, tur, durum, sayfa_sayisi, ders, sha
FROM dosya
WHERE ders IS NOT NULL AND ders != ''
ORDER BY norm_ad ASC, ad ASC;";

        string normAranan = DosyaNormalizasyon.Normalle(dersKlasoru);
        var sonuclar = new List<DosyaSonucu>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string? dersDegeri = reader.IsDBNull(10) ? null : reader.GetString(10);
            if (dersDegeri != null && DosyaNormalizasyon.Normalle(dersDegeri) == normAranan)
            {
                sonuclar.Add(OkuyucudanDosyaSonucu(reader));
            }
        }

        return sonuclar;
    }

    internal static long SafeToUnixTimeSeconds(DateTime dt)
    {
        try
        {
            DateTimeOffset dto = dt.Kind == DateTimeKind.Unspecified
                ? new DateTimeOffset(dt, DateTimeOffset.Now.Offset)
                : new DateTimeOffset(dt);
            return dto.ToUnixTimeSeconds();
        }
        catch
        {
            return dt.Ticks < DateTime.UnixEpoch.Ticks
                ? -62135596800L
                : 253402300799L;
        }
    }

    private static string EscapeLike(string input)
    {
        return input
            .Replace(@"\", @"\\")
            .Replace("%", @"\%")
            .Replace("_", @"\_");
    }

    private static DosyaSonucu OkuyucudanDosyaSonucu(SqliteDataReader reader)
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
}
