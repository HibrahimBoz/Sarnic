namespace Portal.Servis;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Portal.Cekirdek;
using Portal.Cekirdek.Yonetim;

/// <summary>Ajan soruları. İş kuyruğuyla aynı veritabanında durur; çökmede kaybolmaz.</summary>
public sealed partial class IsKuyrugu
{
    public const int EnUzunSoru = 2000;
    public const int EnCokBekleyenSoru = 200;
    /// <summary>Kişi başına 1 çalışan ve 2 bekleyen soru.</summary>
    public const int KisiBasinaEnCokSoru = 3;
    private const string SoruKolonlari = "id,kullanici,soru,durum,cevap,kaynaklar,eklenme";

    private static void SoruTablosunuKur(SqliteConnection db)
    {
        Calistir(db, """
            CREATE TABLE IF NOT EXISTS soru(
              id INTEGER PRIMARY KEY AUTOINCREMENT, kullanici TEXT NOT NULL, soru TEXT NOT NULL, anahtar TEXT NOT NULL,
              durum TEXT NOT NULL, cevap TEXT, kaynaklar TEXT, eklenme TEXT NOT NULL, bitis TEXT);
            CREATE INDEX IF NOT EXISTS soru_durum ON soru(durum, id);
            CREATE INDEX IF NOT EXISTS soru_kisi ON soru(kullanici, id);
            CREATE INDEX IF NOT EXISTS soru_anahtar ON soru(anahtar, durum);
            """);
        Calistir(db, "UPDATE soru SET durum=$b WHERE durum=$c", ("$b", IsSabitleri.Bekliyor), ("$c", IsSabitleri.Calisiyor));
    }

    /// <summary>Aynı soru ve aynı dizin sürümü önbellekten döner. Boşluk ve büyük harf farkı sayılmaz.</summary>
    public static string SoruAnahtari(string soru, string dizinSurumu)
    {
        string normal = string.Join(' ', TrNormalizasyon.Kelimeler(soru));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normal + "\n" + dizinSurumu)));
    }

    public (long? Id, string? Hata) SoruEkle(string kullanici, string soru, string dizinSurumu)
    {
        string anahtar = SoruAnahtari(soru, dizinSurumu);
        lock (_kilit)
        {
            using var db = Ac();
            if (Sayi(db, "SELECT COUNT(*) FROM soru WHERE kullanici=$k AND durum IN ($b,$c)",
                    ("$k", kullanici), ("$b", IsSabitleri.Bekliyor), ("$c", IsSabitleri.Calisiyor)) >= KisiBasinaEnCokSoru)
            {
                return (null, $"En çok {KisiBasinaEnCokSoru} açık sorunuz olabilir. Birinin bitmesini bekleyin.");
            }

            using (var o = db.CreateCommand())
            {
                o.CommandText = "SELECT cevap,kaynaklar FROM soru WHERE anahtar=$a AND durum=$d ORDER BY id DESC LIMIT 1";
                o.Parameters.AddWithValue("$a", anahtar);
                o.Parameters.AddWithValue("$d", IsSabitleri.Bitti);
                using var r = o.ExecuteReader();
                if (r.Read())
                {
                    return (SoruYaz(db, kullanici, soru, anahtar, IsSabitleri.Bitti, r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1)), null);
                }
            }

            if (Sayi(db, "SELECT COUNT(*) FROM soru WHERE durum=$b", ("$b", IsSabitleri.Bekliyor)) >= EnCokBekleyenSoru)
            {
                return (null, $"Soru kuyruğu dolu: en çok {EnCokBekleyenSoru} bekleyen soru olur.");
            }
            return (SoruYaz(db, kullanici, soru, anahtar, IsSabitleri.Bekliyor, null, null), null);
        }
    }

    private static long SoruYaz(SqliteConnection db, string kullanici, string soru, string anahtar, string durum, string? cevap, string? kaynaklar)
    {
        using var k = db.CreateCommand();
        k.CommandText = """
            INSERT INTO soru(kullanici,soru,anahtar,durum,cevap,kaynaklar,eklenme) VALUES($k,$s,$a,$d,$c,$y,$z);
            SELECT last_insert_rowid();
            """;
        k.Parameters.AddWithValue("$k", kullanici);
        k.Parameters.AddWithValue("$s", soru);
        k.Parameters.AddWithValue("$a", anahtar);
        k.Parameters.AddWithValue("$d", durum);
        k.Parameters.AddWithValue("$c", (object?)cevap ?? DBNull.Value);
        k.Parameters.AddWithValue("$y", (object?)kaynaklar ?? DBNull.Value);
        k.Parameters.AddWithValue("$z", Simdi());
        return (long)k.ExecuteScalar()!;
    }

    private static long Sayi(SqliteConnection db, string sql, params (string Ad, object? Deger)[] degerler)
    {
        using var k = db.CreateCommand();
        k.CommandText = sql;
        foreach (var (ad, deger) in degerler)
        {
            k.Parameters.AddWithValue(ad, deger ?? DBNull.Value);
        }
        return (long)k.ExecuteScalar()!;
    }

    public int BekleyenSoruSayisi()
    {
        lock (_kilit)
        {
            using var db = Ac();
            return (int)Sayi(db, "SELECT COUNT(*) FROM soru WHERE durum=$b", ("$b", IsSabitleri.Bekliyor));
        }
    }

    public SoruKaydi? SiradakiSoruyuAl()
    {
        lock (_kilit)
        {
            using var db = Ac();
            SoruKaydi? kayit = SoruListele(db, "WHERE durum=$b ORDER BY id LIMIT 1", ("$b", IsSabitleri.Bekliyor)).FirstOrDefault();
            if (kayit == null)
            {
                return null;
            }
            Calistir(db, "UPDATE soru SET durum=$c WHERE id=$i", ("$c", IsSabitleri.Calisiyor), ("$i", kayit.Id));
            kayit.Durum = IsSabitleri.Calisiyor;
            return kayit;
        }
    }

    public void SoruBitir(long id, string durum, string? cevap, IReadOnlyList<AjanKaynagi>? kaynaklar)
    {
        lock (_kilit)
        {
            using var db = Ac();
            Calistir(db, "UPDATE soru SET durum=$d, cevap=$c, kaynaklar=$y, bitis=$z WHERE id=$i",
                ("$d", durum), ("$c", cevap), ("$y", kaynaklar == null ? null : JsonSerializer.Serialize(kaynaklar)),
                ("$z", Simdi()), ("$i", id));
        }
    }

    /// <summary>Servis kapanırken yarım kalan soru sıraya döner.</summary>
    public void SoruyuGeriKoy(long id)
    {
        lock (_kilit)
        {
            using var db = Ac();
            Calistir(db, "UPDATE soru SET durum=$b WHERE id=$i", ("$b", IsSabitleri.Bekliyor), ("$i", id));
        }
    }

    /// <summary>Kişi yalnız kendi bekleyen sorusunu iptal eder.</summary>
    public bool SoruyuIptalEt(long id, string kullanici)
    {
        lock (_kilit)
        {
            using var db = Ac();
            return Calistir(db, "UPDATE soru SET durum=$p, bitis=$z WHERE id=$i AND kullanici=$k AND durum=$b",
                ("$p", IsSabitleri.Iptal), ("$z", Simdi()), ("$i", id), ("$k", kullanici), ("$b", IsSabitleri.Bekliyor)) > 0;
        }
    }

    /// <summary>Kişinin son soruları. Bekleyen soruda önündeki soru sayısı + 1 yazılır.</summary>
    public List<SoruKaydi> KullaniciSorulari(string kullanici, int adet = 20)
    {
        lock (_kilit)
        {
            using var db = Ac();
            List<SoruKaydi> liste = SoruListele(db, "WHERE kullanici=$k ORDER BY id DESC LIMIT $n", ("$k", kullanici), ("$n", adet));
            foreach (SoruKaydi s in liste.Where(s => s.Durum == IsSabitleri.Bekliyor))
            {
                s.Sira = (int)Sayi(db, "SELECT COUNT(*) FROM soru WHERE durum=$b AND id<$i", ("$b", IsSabitleri.Bekliyor), ("$i", s.Id)) + 1;
            }
            return liste;
        }
    }

    private static List<SoruKaydi> SoruListele(SqliteConnection db, string kosul, params (string Ad, object? Deger)[] degerler)
    {
        using var k = db.CreateCommand();
        k.CommandText = $"SELECT {SoruKolonlari} FROM soru " + kosul;
        foreach (var (ad, deger) in degerler)
        {
            k.Parameters.AddWithValue(ad, deger ?? DBNull.Value);
        }
        var sonuc = new List<SoruKaydi>();
        using var r = k.ExecuteReader();
        while (r.Read())
        {
            sonuc.Add(new SoruKaydi
            {
                Id = r.GetInt64(0),
                Kullanici = r.GetString(1),
                Soru = r.GetString(2),
                Durum = r.GetString(3),
                Cevap = r.IsDBNull(4) ? null : r.GetString(4),
                Kaynaklar = r.IsDBNull(5) ? new() : JsonSerializer.Deserialize<List<AjanKaynagi>>(r.GetString(5)) ?? new(),
                Eklenme = r.GetString(6),
            });
        }
        return sonuc;
    }
}
