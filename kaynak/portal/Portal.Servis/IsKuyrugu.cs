namespace Portal.Servis;

using System.Globalization;
using Microsoft.Data.Sqlite;
using Portal.Cekirdek.Yonetim;

/// <summary>Kalıcı iş kuyruğu. Yalnız yol kaydeder, dosya kopyalamaz.</summary>
public sealed partial class IsKuyrugu
{
    public const int EnCokBekleyen = 200;
    private readonly string _baglanti;
    private readonly object _kilit = new();

    public IsKuyrugu(string dbYolu)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbYolu)!);
        _baglanti = new SqliteConnectionStringBuilder { DataSource = dbYolu, Pooling = false }.ToString();
        lock (_kilit)
        {
            using var db = Ac();
            Calistir(db, """
                CREATE TABLE IF NOT EXISTS is_kaydi(
                  id INTEGER PRIMARY KEY AUTOINCREMENT, tur TEXT NOT NULL, yol TEXT NOT NULL, cikis TEXT,
                  durum TEXT NOT NULL, ekleyen TEXT NOT NULL, eklenme TEXT NOT NULL, baslama TEXT, bitis TEXT,
                  son_satir TEXT, cikis_kodu INTEGER);
                CREATE INDEX IF NOT EXISTS is_durum ON is_kaydi(durum, id);
                """);
            SoruTablosunuKur(db);
            // Servis çöktüyse yarım kalan iş yeniden sıraya girer.
            Calistir(db, "UPDATE is_kaydi SET durum=$b, baslama=NULL WHERE durum=$c",
                ("$b", IsSabitleri.Bekliyor), ("$c", IsSabitleri.Calisiyor));
        }
    }

    private SqliteConnection Ac()
    {
        var db = new SqliteConnection(_baglanti);
        db.Open();
        return db;
    }

    private static int Calistir(SqliteConnection db, string sql, params (string Ad, object? Deger)[] degerler)
    {
        using var k = db.CreateCommand();
        k.CommandText = sql;
        foreach (var (ad, deger) in degerler)
        {
            k.Parameters.AddWithValue(ad, deger ?? DBNull.Value);
        }
        return k.ExecuteNonQuery();
    }

    private static string Simdi() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>Sınır aşılırsa null döner.</summary>
    public long? Ekle(string tur, string yol, string? cikis, string ekleyen)
    {
        lock (_kilit)
        {
            using var db = Ac();
            using var say = db.CreateCommand();
            say.CommandText = "SELECT COUNT(*) FROM is_kaydi WHERE durum=$b";
            say.Parameters.AddWithValue("$b", IsSabitleri.Bekliyor);
            if ((long)say.ExecuteScalar()! >= EnCokBekleyen)
            {
                return null;
            }
            using var k = db.CreateCommand();
            k.CommandText = """
                INSERT INTO is_kaydi(tur, yol, cikis, durum, ekleyen, eklenme) VALUES($t,$y,$c,$d,$e,$z);
                SELECT last_insert_rowid();
                """;
            k.Parameters.AddWithValue("$t", tur);
            k.Parameters.AddWithValue("$y", yol);
            k.Parameters.AddWithValue("$c", (object?)cikis ?? DBNull.Value);
            k.Parameters.AddWithValue("$d", IsSabitleri.Bekliyor);
            k.Parameters.AddWithValue("$e", ekleyen);
            k.Parameters.AddWithValue("$z", Simdi());
            return (long)k.ExecuteScalar()!;
        }
    }

    /// <summary>Sıradaki işi çalışıyor durumuna alır.</summary>
    public IsKaydi? SiradakiniAl()
    {
        lock (_kilit)
        {
            using var db = Ac();
            IsKaydi? kayit = Listele(db, "WHERE durum=$b ORDER BY id LIMIT 1", ("$b", IsSabitleri.Bekliyor)).FirstOrDefault();
            if (kayit == null)
            {
                return null;
            }
            string baslama = Simdi();
            Calistir(db, "UPDATE is_kaydi SET durum=$c, baslama=$z WHERE id=$i",
                ("$c", IsSabitleri.Calisiyor), ("$z", baslama), ("$i", kayit.Id));
            kayit.Durum = IsSabitleri.Calisiyor;
            kayit.Baslama = baslama;
            return kayit;
        }
    }

    public void SonSatirYaz(long id, string satir)
    {
        lock (_kilit)
        {
            using var db = Ac();
            Calistir(db, "UPDATE is_kaydi SET son_satir=$s WHERE id=$i", ("$s", satir), ("$i", id));
        }
    }

    public void Bitir(long id, string durum, int? cikisKodu)
    {
        lock (_kilit)
        {
            using var db = Ac();
            Calistir(db, "UPDATE is_kaydi SET durum=$d, bitis=$z, cikis_kodu=$k WHERE id=$i",
                ("$d", durum), ("$z", Simdi()), ("$k", cikisKodu), ("$i", id));
        }
    }

    /// <summary>Bekleyen işi iptal eder. Çalışan iş için işçi süreci durdurur.</summary>
    public bool BekleyeniIptalEt(long id)
    {
        lock (_kilit)
        {
            using var db = Ac();
            return Calistir(db, "UPDATE is_kaydi SET durum=$p, bitis=$z WHERE id=$i AND durum=$b",
                ("$p", IsSabitleri.Iptal), ("$z", Simdi()), ("$i", id), ("$b", IsSabitleri.Bekliyor)) > 0;
        }
    }

    public bool YenidenDene(long id)
    {
        lock (_kilit)
        {
            using var db = Ac();
            return Calistir(db, """
                UPDATE is_kaydi SET durum=$b, baslama=NULL, bitis=NULL, cikis_kodu=NULL, son_satir=NULL
                WHERE id=$i AND durum IN ($h, $p)
                """, ("$b", IsSabitleri.Bekliyor), ("$i", id), ("$h", IsSabitleri.Hata), ("$p", IsSabitleri.Iptal)) > 0;
        }
    }

    /// <summary>Yalnız kuyruk kaydını siler; kaynak ve çıktı dosyalarına dokunmaz. Çalışan iş kalır.</summary>
    public int Temizle()
    {
        lock (_kilit)
        {
            using var db = Ac();
            return Calistir(db, "DELETE FROM is_kaydi WHERE durum<>$c", ("$c", IsSabitleri.Calisiyor))
                + Calistir(db, "DELETE FROM soru WHERE durum<>$c", ("$c", IsSabitleri.Calisiyor));
        }
    }

    public List<IsKaydi> SonIsler(int adet = 300)
    {
        lock (_kilit)
        {
            using var db = Ac();
            return Listele(db, "ORDER BY id DESC LIMIT $n", ("$n", adet));
        }
    }

    private static List<IsKaydi> Listele(SqliteConnection db, string kosul, params (string Ad, object? Deger)[] degerler)
    {
        using var k = db.CreateCommand();
        k.CommandText = "SELECT id,tur,yol,cikis,durum,ekleyen,eklenme,baslama,bitis,son_satir,cikis_kodu FROM is_kaydi " + kosul;
        foreach (var (ad, deger) in degerler)
        {
            k.Parameters.AddWithValue(ad, deger ?? DBNull.Value);
        }
        var sonuc = new List<IsKaydi>();
        using var r = k.ExecuteReader();
        while (r.Read())
        {
            sonuc.Add(new IsKaydi
            {
                Id = r.GetInt64(0),
                Tur = r.GetString(1),
                Yol = r.GetString(2),
                Cikis = r.IsDBNull(3) ? null : r.GetString(3),
                Durum = r.GetString(4),
                Ekleyen = r.GetString(5),
                Eklenme = r.GetString(6),
                Baslama = r.IsDBNull(7) ? null : r.GetString(7),
                Bitis = r.IsDBNull(8) ? null : r.GetString(8),
                SonSatir = r.IsDBNull(9) ? null : r.GetString(9),
                CikisKodu = r.IsDBNull(10) ? null : r.GetInt32(10),
            });
        }
        return sonuc;
    }
}
