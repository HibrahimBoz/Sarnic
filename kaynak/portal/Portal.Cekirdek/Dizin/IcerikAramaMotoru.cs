namespace Portal.Cekirdek.Dizin;

using System.Text;
using Microsoft.Data.Sqlite;

/// <summary>
/// Belge içeriği (sayfa) düzeyinde arama sonucu.
/// </summary>
public sealed record IcerikSonucu(
    long DosyaId,
    string Yol,
    string Ad,
    int SayfaNo,
    string Etiket,
    string Kaynak,
    double? Guven,
    string Alinti,
    (int Bas, int Uzunluk)[] Vurgular,
    double Puan,
    string? Ders,
    string? Sha);

/// <summary>
/// Belge içerikleri üzerinde FTS5 ve bm25 tabanlı sayfa arama motoru.
/// </summary>
public sealed class IcerikAramaMotoru
{
    private readonly DizinKaynagi _kaynak;

    public IcerikAramaMotoru(DizinKaynagi kaynak)
    {
        _kaynak = kaynak;
    }

    /// <summary>
    /// Sorgu metnine göre sayfaları arar, puanlar ve alıntı/vurgu ile döner.
    /// </summary>
    public IReadOnlyList<IcerikSonucu> Ara(
        string sorgu,
        DosyaSorgusu? suzgec = null,
        int enFazla = 200,
        int belgeBasinaEnFazla = 5,
        CancellationToken iptal = default)
    {
        if (_kaynak.Durum != DizinDurumu.Hazir || string.IsNullOrWhiteSpace(sorgu) || enFazla <= 0)
        {
            return Array.Empty<IcerikSonucu>();
        }

        string[] kelimeler = TrNormalizasyon.Kelimeler(sorgu);
        if (kelimeler.Length == 0)
        {
            return Array.Empty<IcerikSonucu>();
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

        string ftsHepsi = string.Join(" ", kelimeler.Select(k => $"\"{k.Replace("\"", "\"\"")}\"*"));
        cmd.Parameters.AddWithValue("@ftsHepsi", ftsHepsi);

        bool tamIfadeVar = kelimeler.Length >= 2;
        if (tamIfadeVar)
        {
            string ftsTam = "\"" + string.Join(" ", kelimeler.Select(k => k.Replace("\"", "\"\""))) + "\"";
            cmd.Parameters.AddWithValue("@ftsTam", ftsTam);
        }

        var whereListesi = new List<string>();
        int pIndex = 0;

        if (suzgec != null)
        {
            // Uzantilar
            if (suzgec.Uzantilar.Count > 0)
            {
                var uzParams = new List<string>();
                foreach (string uz in suzgec.Uzantilar)
                {
                    string p = $"@uz{pIndex++}";
                    cmd.Parameters.AddWithValue(p, uz);
                    uzParams.Add(p);
                }

                whereListesi.Add($"d.uzanti IN ({string.Join(", ", uzParams)})");
            }

            // Turler
            if (suzgec.Turler.Count > 0)
            {
                var turParams = new List<string>();
                foreach (string tur in suzgec.Turler)
                {
                    string p = $"@tur{pIndex++}";
                    cmd.Parameters.AddWithValue(p, tur);
                    turParams.Add(p);
                }

                whereListesi.Add($"d.tur IN ({string.Join(", ", turParams)})");
            }

            // Boyut
            if (suzgec.Boyut != null)
            {
                if (suzgec.Boyut.EnAz.HasValue)
                {
                    string p = $"@bMin{pIndex++}";
                    cmd.Parameters.AddWithValue(p, suzgec.Boyut.EnAz.Value);
                    whereListesi.Add($"d.boyut >= {p}");
                }

                if (suzgec.Boyut.EnCok.HasValue)
                {
                    string p = $"@bMax{pIndex++}";
                    cmd.Parameters.AddWithValue(p, suzgec.Boyut.EnCok.Value);
                    whereListesi.Add($"d.boyut <= {p}");
                }
            }

            // Tarih
            if (suzgec.Tarih != null)
            {
                if (suzgec.Tarih.Bas.HasValue)
                {
                    long basSec = DosyaAramaMotoru.SafeToUnixTimeSeconds(suzgec.Tarih.Bas.Value);
                    string p = $"@tBas{pIndex++}";
                    cmd.Parameters.AddWithValue(p, basSec);
                    whereListesi.Add($"d.degisme >= {p}");
                }

                if (suzgec.Tarih.Son.HasValue)
                {
                    long sonSec = DosyaAramaMotoru.SafeToUnixTimeSeconds(suzgec.Tarih.Son.Value);
                    string p = $"@tSon{pIndex++}";
                    cmd.Parameters.AddWithValue(p, sonSec);
                    whereListesi.Add($"d.degisme < {p}");
                }
            }

            // DersBagli
            if (suzgec.DersBagli == true)
            {
                whereListesi.Add("(d.ders IS NOT NULL AND d.ders != '')");
            }
            else if (suzgec.DersBagli == false)
            {
                whereListesi.Add("(d.ders IS NULL OR d.ders = '')");
            }

            // Yol terimleri: VEYA grubunda Ad ve Yol karışıksa daraltma olmaması için grup atlanır
            foreach (var veyaGrubu in suzgec.Kosullar)
            {
                if (veyaGrubu.Count == 0)
                {
                    continue;
                }

                if (veyaGrubu.Any(t => t.Alan != TerimAlani.Yol))
                {
                    continue;
                }

                var yolTerimleri = veyaGrubu.Where(t => t.Alan == TerimAlani.Yol).ToList();
                if (yolTerimleri.Count == 0)
                {
                    continue;
                }

                var orClauses = new List<string>();
                foreach (var terim in yolTerimleri)
                {
                    if (string.IsNullOrEmpty(terim.Metin))
                    {
                        continue;
                    }

                    string p = $"@yol{pIndex++}";
                    if (terim.Joker)
                    {
                        string glob = terim.Metin.Replace("[", "[[]");
                        cmd.Parameters.AddWithValue(p, glob);
                        orClauses.Add(terim.Degil ? $"NOT (d.norm_yol GLOB {p})" : $"(d.norm_yol GLOB {p})");
                    }
                    else
                    {
                        string like = $"%{EscapeLike(terim.Metin)}%";
                        cmd.Parameters.AddWithValue(p, like);
                        orClauses.Add(terim.Degil
                            ? $"(d.norm_yol NOT LIKE {p} ESCAPE '\\')"
                            : $"(d.norm_yol LIKE {p} ESCAPE '\\')");
                    }
                }

                if (orClauses.Count > 0)
                {
                    whereListesi.Add($"({string.Join(" OR ", orClauses)})");
                }
            }
        }

        string whereSql = whereListesi.Count > 0 ? $"WHERE {string.Join(" AND ", whereListesi)}" : string.Empty;

        var sbSql = new StringBuilder();
        sbSql.AppendLine("WITH fts_sonuclari AS (");
        sbSql.AppendLine("    SELECT rowid, bm25(sayfa_fts) AS bm");
        sbSql.AppendLine("    FROM sayfa_fts");
        sbSql.AppendLine("    WHERE sayfa_fts MATCH @ftsHepsi");
        sbSql.AppendLine(")");

        if (tamIfadeVar)
        {
            sbSql.AppendLine(", tam_ifade AS (");
            sbSql.AppendLine("    SELECT rowid");
            sbSql.AppendLine("    FROM sayfa_fts");
            sbSql.AppendLine("    WHERE sayfa_fts MATCH @ftsTam");
            sbSql.AppendLine("),");
            sbSql.AppendLine("puanli AS (");
            sbSql.AppendLine("    SELECT");
            sbSql.AppendLine("        f.rowid AS sayfa_id,");
            sbSql.AppendLine("        (CASE WHEN t.rowid IS NOT NULL THEN 1000.0 + (-f.bm) ELSE -f.bm END) AS puan");
            sbSql.AppendLine("    FROM fts_sonuclari f");
            sbSql.AppendLine("    LEFT JOIN tam_ifade t ON f.rowid = t.rowid");
            sbSql.AppendLine(")");
        }
        else
        {
            sbSql.AppendLine(", puanli AS (");
            sbSql.AppendLine("    SELECT");
            sbSql.AppendLine("        f.rowid AS sayfa_id,");
            sbSql.AppendLine("        -f.bm AS puan");
            sbSql.AppendLine("    FROM fts_sonuclari f");
            sbSql.AppendLine(")");
        }

        // Metin sütununu CTE dışına alarak bellek ve sıralama yükünü optimize et
        sbSql.AppendLine(", sirali AS (");
        sbSql.AppendLine("    SELECT");
        sbSql.AppendLine("        s.id AS sayfa_id,");
        sbSql.AppendLine("        s.dosya_id,");
        sbSql.AppendLine("        p.puan,");
        sbSql.AppendLine("        ROW_NUMBER() OVER (PARTITION BY s.dosya_id ORDER BY p.puan DESC, s.id ASC) AS sira");
        sbSql.AppendLine("    FROM puanli p");
        sbSql.AppendLine("    JOIN sayfa s ON p.sayfa_id = s.id");
        sbSql.AppendLine("    JOIN dosya d ON s.dosya_id = d.id");
        if (!string.IsNullOrEmpty(whereSql))
        {
            sbSql.AppendLine($"    {whereSql}");
        }

        sbSql.AppendLine("),");
        sbSql.AppendLine("secilenler AS (");
        sbSql.AppendLine("    SELECT sayfa_id, dosya_id, puan");
        sbSql.AppendLine("    FROM sirali");
        sbSql.AppendLine("    WHERE sira <= @belgeBasinaEnFazla");
        sbSql.AppendLine("    ORDER BY puan DESC, sayfa_id ASC");
        sbSql.AppendLine("    LIMIT @enFazla");
        sbSql.AppendLine(")");
        sbSql.AppendLine("SELECT sec.dosya_id, d.yol, d.ad, s.no AS sayfa_no, s.etiket, s.kaynak, s.guven, s.metin, sec.puan, d.ders, d.sha");
        sbSql.AppendLine("FROM secilenler sec");
        sbSql.AppendLine("JOIN sayfa s ON sec.sayfa_id = s.id");
        sbSql.AppendLine("JOIN dosya d ON sec.dosya_id = d.id");
        sbSql.AppendLine("ORDER BY sec.puan DESC, sec.sayfa_id ASC;");

        cmd.CommandText = sbSql.ToString();
        cmd.Parameters.AddWithValue("@belgeBasinaEnFazla", belgeBasinaEnFazla);
        cmd.Parameters.AddWithValue("@enFazla", enFazla);

        var sonuclar = new List<IcerikSonucu>();
        try
        {
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                iptal.ThrowIfCancellationRequested();

                long dosyaId = reader.GetInt64(0);
                string yol = reader.GetString(1);
                string ad = reader.GetString(2);
                int sayfaNo = reader.GetInt32(3);
                string etiket = reader.GetString(4);
                string kaynak = reader.GetString(5);
                double? guven = reader.IsDBNull(6) ? null : (double?)reader.GetDouble(6);
                string metin = reader.GetString(7);
                double puan = reader.GetDouble(8);
                string? ders = reader.IsDBNull(9) ? null : reader.GetString(9);
                string? sha = reader.IsDBNull(10) ? null : reader.GetString(10);

                var (alinti, vurgular) = MetinEsleme.AlintiUret(metin, kelimeler, 240);

                sonuclar.Add(new IcerikSonucu(
                    dosyaId,
                    yol,
                    ad,
                    sayfaNo,
                    etiket,
                    kaynak,
                    guven,
                    alinti,
                    vurgular,
                    puan,
                    ders,
                    sha));
            }
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 9 || iptal.IsCancellationRequested)
        {
            throw new OperationCanceledException("Sorgu iptal edildi.", ex, iptal);
        }

        return sonuclar;
    }

    private static string EscapeLike(string input)
    {
        return input
            .Replace(@"\", @"\\")
            .Replace("%", @"\%")
            .Replace("_", @"\_");
    }
}
