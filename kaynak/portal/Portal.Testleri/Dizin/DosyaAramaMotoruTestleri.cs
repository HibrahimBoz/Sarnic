namespace Portal.Testleri.Dizin;

using Microsoft.Data.Sqlite;
using Portal.Cekirdek.Dizin;
using Xunit;

/// <summary>
/// DosyaAramaMotoru arama yeteneklerini, süzgeçlerini ve sıralama kurallarını doğrulayan testler.
/// </summary>
public class DosyaAramaMotoruTestleri
{
    [Fact]
    public void Ara_TurkceHarfDuyarsizligi_IsikSorgusuIsikDosyasiniBulur()
    {
        using var testDizini = new TestDizini();
        testDizini.DosyaEkle(@"C:\Belgeler\ISIK.pdf", "ISIK.pdf");
        testDizini.DosyaEkle(@"C:\Belgeler\Diger.docx", "Diger.docx");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new DosyaAramaMotoru(kaynak);

        var sonuclar = motor.Ara(DosyaSorgusu.Ayristir("ışık"));

        Assert.Single(sonuclar);
        Assert.Equal("ISIK.pdf", sonuclar[0].Ad);
    }

    [Fact]
    public void Ara_NoktaliAd_v12DosyasiniBulur()
    {
        using var testDizini = new TestDizini();
        testDizini.DosyaEkle(@"C:\Yazilim\paket_v1.2.zip", "paket_v1.2.zip");
        testDizini.DosyaEkle(@"C:\Yazilim\paket_v2.0.zip", "paket_v2.0.zip");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new DosyaAramaMotoru(kaynak);

        var sonuclar = motor.Ara(DosyaSorgusu.Ayristir("v1.2"));

        Assert.Single(sonuclar);
        Assert.Equal("paket_v1.2.zip", sonuclar[0].Ad);
    }

    [Fact]
    public void Ara_IkiKarakterliTerim_LikeIleBasariylaBulur()
    {
        using var testDizini = new TestDizini();
        testDizini.DosyaEkle(@"C:\Belgeler\ab_rapor.txt", "ab_rapor.txt");
        testDizini.DosyaEkle(@"C:\Belgeler\cd_rapor.txt", "cd_rapor.txt");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new DosyaAramaMotoru(kaynak);

        var sonuclar = motor.Ara(DosyaSorgusu.Ayristir("ab"));

        Assert.Single(sonuclar);
        Assert.Equal("ab_rapor.txt", sonuclar[0].Ad);
    }

    [Fact]
    public void Ara_JokerSorgusu_YalnizBelirtilenUzantililariBulur()
    {
        using var testDizini = new TestDizini();
        testDizini.DosyaEkle(@"C:\Belgeler\tez.pdf", "tez.pdf");
        testDizini.DosyaEkle(@"C:\Belgeler\rapor_pdf.docx", "rapor_pdf.docx");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new DosyaAramaMotoru(kaynak);

        var sonuclar = motor.Ara(DosyaSorgusu.Ayristir("*.pdf"));

        Assert.Single(sonuclar);
        Assert.Equal("tez.pdf", sonuclar[0].Ad);
    }

    [Fact]
    public void Ara_DegilSemantigi_IlgiliDosyayiHaricTutar()
    {
        using var testDizini = new TestDizini();
        testDizini.DosyaEkle(@"C:\Belgeler\rapor_2025.pdf", "rapor_2025.pdf");
        testDizini.DosyaEkle(@"C:\Belgeler\rapor_2026.pdf", "rapor_2026.pdf");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new DosyaAramaMotoru(kaynak);

        var sonuclar = motor.Ara(DosyaSorgusu.Ayristir("rapor !2025"));

        Assert.Single(sonuclar);
        Assert.Equal("rapor_2026.pdf", sonuclar[0].Ad);
    }

    [Fact]
    public void Ara_VeyaSemantigi_AlternatiflerdenBiriniIcerenleriBulur()
    {
        using var testDizini = new TestDizini();
        testDizini.DosyaEkle(@"C:\Belgeler\fizik.pdf", "fizik.pdf");
        testDizini.DosyaEkle(@"C:\Belgeler\kimya.pdf", "kimya.pdf");
        testDizini.DosyaEkle(@"C:\Belgeler\biyoloji.pdf", "biyoloji.pdf");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new DosyaAramaMotoru(kaynak);

        var sonuclar = motor.Ara(DosyaSorgusu.Ayristir("fizik|kimya"));

        Assert.Equal(2, sonuclar.Count);
        Assert.Contains(sonuclar, s => s.Ad == "fizik.pdf");
        Assert.Contains(sonuclar, s => s.Ad == "kimya.pdf");
        Assert.DoesNotContain(sonuclar, s => s.Ad == "biyoloji.pdf");
    }

    [Fact]
    public void Ara_UzantiTurBoyutTarihSuzgecleri_BirlikteCalisir()
    {
        using var testDizini = new TestDizini();
        var ekimTarihi = new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc);
        var ocakTarihi = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

        testDizini.DosyaEkle(@"C:\Belgeler\hedef.pdf", "hedef.pdf", uzanti: "pdf", tur: "belge", boyut: 2 * 1024 * 1024, degisme: ekimTarihi);
        testDizini.DosyaEkle(@"C:\Belgeler\eski.pdf", "eski.pdf", uzanti: "pdf", tur: "belge", boyut: 2 * 1024 * 1024, degisme: ocakTarihi);
        testDizini.DosyaEkle(@"C:\Belgeler\kucuk.pdf", "kucuk.pdf", uzanti: "pdf", tur: "belge", boyut: 100, degisme: ekimTarihi);
        testDizini.DosyaEkle(@"C:\Belgeler\video.mp4", "video.mp4", uzanti: "mp4", tur: "video", boyut: 2 * 1024 * 1024, degisme: ekimTarihi);

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new DosyaAramaMotoru(kaynak);

        var sonuclar = motor.Ara(DosyaSorgusu.Ayristir("uz:pdf tur:belge boyut:>1mb tarih:2026-10"));

        Assert.Single(sonuclar);
        Assert.Equal("hedef.pdf", sonuclar[0].Ad);
    }

    [Fact]
    public void Ara_YolTerimi_DosyaYolunaGoreFiltreler()
    {
        using var testDizini = new TestDizini();
        testDizini.DosyaEkle(@"C:\Arsiv\Gizli\plan.docx", "plan.docx", klasor: @"C:\Arsiv\Gizli");
        testDizini.DosyaEkle(@"C:\Genel\Acik\plan.docx", "plan.docx", klasor: @"C:\Genel\Acik");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new DosyaAramaMotoru(kaynak);

        var sonuclar = motor.Ara(DosyaSorgusu.Ayristir("yol:gizli plan"));

        Assert.Single(sonuclar);
        Assert.Equal(@"C:\Arsiv\Gizli\plan.docx", sonuclar[0].Yol);
    }

    [Fact]
    public void Ara_Siralama_IlgiSirasi_TamSonraBaslarSonraIcerir()
    {
        using var testDizini = new TestDizini();
        testDizini.DosyaEkle(@"C:\Belgeler\is_plani_ozeti.txt", "is_plani_ozeti.txt");
        testDizini.DosyaEkle(@"C:\Belgeler\plan.txt", "plan.txt");
        testDizini.DosyaEkle(@"C:\Belgeler\planlama_rehberi.txt", "planlama_rehberi.txt");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new DosyaAramaMotoru(kaynak);

        var sonuclar = motor.Ara(DosyaSorgusu.Ayristir("plan"), new Siralama(SiralamaAlani.Ilgi, Azalan: false));

        Assert.Equal(3, sonuclar.Count);
        Assert.Equal("plan.txt", sonuclar[0].Ad);
        Assert.Equal("planlama_rehberi.txt", sonuclar[1].Ad);
        Assert.Equal("is_plani_ozeti.txt", sonuclar[2].Ad);
    }

    [Fact]
    public void Ara_Siralama_BoyutVeTarihSiralamasi()
    {
        using var testDizini = new TestDizini();
        var t1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        testDizini.DosyaEkle(@"C:\a.txt", "a.txt", boyut: 100, degisme: t1);
        testDizini.DosyaEkle(@"C:\b.txt", "b.txt", boyut: 500, degisme: t2);

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new DosyaAramaMotoru(kaynak);

        var boyutAzalan = motor.Ara(DosyaSorgusu.Ayristir("*.txt"), new Siralama(SiralamaAlani.Boyut, Azalan: true));
        Assert.Equal("b.txt", boyutAzalan[0].Ad);
        Assert.Equal("a.txt", boyutAzalan[1].Ad);

        var tarihArtan = motor.Ara(DosyaSorgusu.Ayristir("*.txt"), new Siralama(SiralamaAlani.Tarih, Azalan: false));
        Assert.Equal("a.txt", tarihArtan[0].Ad);
        Assert.Equal("b.txt", tarihArtan[1].Ad);
    }

    [Fact]
    public void Ara_Siralama_TurkceKarakterlerVeIlgiAzalan()
    {
        using var testDizini = new TestDizini();
        testDizini.DosyaEkle(@"C:\Zeta.txt", "Zeta.txt");
        testDizini.DosyaEkle(@"C:\alfa.txt", "alfa.txt");
        testDizini.DosyaEkle(@"C:\Çiçek.txt", "Çiçek.txt");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new DosyaAramaMotoru(kaynak);

        // Türkçe harf katlama (norm_ad) ile sıralanmalı: alfa < cicek < zeta
        var sonuclarAd = motor.Ara(DosyaSorgusu.Ayristir("*.txt"), new Siralama(SiralamaAlani.Ad, Azalan: false));
        Assert.Equal(3, sonuclarAd.Count);
        Assert.Equal("alfa.txt", sonuclarAd[0].Ad);
        Assert.Equal("Çiçek.txt", sonuclarAd[1].Ad);
        Assert.Equal("Zeta.txt", sonuclarAd[2].Ad);

        // Ilgi sıralamasında Azalan=true olduğunda dahi tam eşleşme (CASE 1) ilk sırada kalmalı
        testDizini.DosyaEkle(@"C:\planlama.txt", "planlama.txt");
        testDizini.DosyaEkle(@"C:\plan.txt", "plan.txt");
        testDizini.DosyaEkle(@"C:\is_plani.txt", "is_plani.txt");

        var sonuclarIlgiAzalan = motor.Ara(DosyaSorgusu.Ayristir("plan.txt"), new Siralama(SiralamaAlani.Ilgi, Azalan: true));
        Assert.Equal("plan.txt", sonuclarIlgiAzalan[0].Ad);
    }

    [Fact]
    public void Ara_IptalEdildiginde_OperationCanceledExceptionFirlatir()
    {
        using var testDizini = new TestDizini();

        // SQLite recursive CTE ile 20.000 satırlık büyük bir tablo oluştur
        using (var conn = new SqliteConnection($"Data Source={testDizini.DbYolu}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                WITH RECURSIVE cnt(x) AS (
                    VALUES(1) UNION ALL SELECT x+1 FROM cnt WHERE x < 20000
                )
                INSERT INTO dosya (yol, ad, klasor, uzanti, boyut, degisme, tur, durum, norm_ad, norm_yol)
                SELECT 'C:\dokuman_' || x || '.txt', 'dokuman_' || x || '.txt', 'C:\', 'txt', 100, 1000, 'belge', 'tamam', 'dokuman_' || x || '.txt', 'c:\dokuman_' || x || '.txt'
                FROM cnt;";
            cmd.ExecuteNonQuery();
        }

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new DosyaAramaMotoru(kaynak);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50); // 50 ms sonra iptal et

        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            motor.Ara(DosyaSorgusu.Ayristir("dokuman"), enFazla: 20000, iptal: cts.Token);
        });
    }

    [Fact]
    public void Ara_EnFazlaSiniri_SonucSayisiniKisitlar()
    {
        using var testDizini = new TestDizini();
        for (int i = 0; i < 10; i++)
        {
            testDizini.DosyaEkle(@$"C:\Belgeler\belge_{i}.txt", $"belge_{i}.txt");
        }

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new DosyaAramaMotoru(kaynak);

        var sonuclar = motor.Ara(DosyaSorgusu.Ayristir("belge"), enFazla: 4);

        Assert.Equal(4, sonuclar.Count);
    }

    [Fact]
    public void DersBelgeleri_KlasorYolunuBuyukKucukDuyarsizKarsilastirirVeAdSiraliDondurur()
    {
        using var testDizini = new TestDizini();
        string hedefDers = @"C:\Dersler\Fizik_101_islenmis";

        testDizini.DosyaEkle(@"C:\Dersler\z_notlar.pdf", "z_notlar.pdf", ders: hedefDers);
        testDizini.DosyaEkle(@"C:\Dersler\a_giris.pdf", "a_giris.pdf", ders: hedefDers);
        testDizini.DosyaEkle(@"C:\Dersler\baskaders.pdf", "baskaders.pdf", ders: @"C:\Dersler\Kimya_islenmis");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new DosyaAramaMotoru(kaynak);

        var sonuclar = motor.DersBelgeleri(@"c:\dersler\fizik_101_islenmis");

        Assert.Equal(2, sonuclar.Count);
        Assert.Equal("a_giris.pdf", sonuclar[0].Ad);
        Assert.Equal("z_notlar.pdf", sonuclar[1].Ad);
    }

    [Fact]
    public void DersBelgeleri_TurkceHarfliKlasorYolunuBuyukKucukDuyarsizKarsilastirir()
    {
        using var testDizini = new TestDizini();
        string hedefDers = @"C:\Dersler\İLERİ_BİLGİ_101";

        testDizini.DosyaEkle(@"C:\Dersler\notlar.pdf", "notlar.pdf", ders: hedefDers);
        testDizini.DosyaEkle(@"C:\Dersler\diger.pdf", "diger.pdf", ders: @"C:\Dersler\BaskaDers");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new DosyaAramaMotoru(kaynak);

        // Türkçe 'İ' ve 'I' harflerini içeren küçük harfli sorgu eşleşmelidir
        var sonuclar = motor.DersBelgeleri(@"c:\dersler\ileri_bilgi_101");

        Assert.Single(sonuclar);
        Assert.Equal("notlar.pdf", sonuclar[0].Ad);
    }

    [Fact]
    public void Ara_UcKarakterKontrolu_UnicodeRuneSayar()
    {
        using var testDizini = new TestDizini();
        testDizini.DosyaEkle(@"C:\test_dosya.txt", "test_dosya.txt");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new DosyaAramaMotoru(kaynak);

        // 3 harfli terim başarıyla aranır
        var sonuclar = motor.Ara(DosyaSorgusu.Ayristir("tes"));
        Assert.Single(sonuclar);
    }

    [Fact]
    public void Ara_IcerikSuzgeci_CokSayfaliBelgeyiYalnizBirKezUretir()
    {
        using var testDizini = new TestDizini();
        long dosyaId = testDizini.DosyaEkle(@"C:\Kitaplar\kuantum.pdf", "kuantum.pdf");

        // Aynı dosyaya içerik eşleşmesi sağlayan 4 farklı sayfa ekle
        testDizini.SayfaEkle(dosyaId, 1, "Sayfa 1", "Kuantum mekaniği prensipleri 1");
        testDizini.SayfaEkle(dosyaId, 2, "Sayfa 2", "Kuantum mekaniği prensipleri 2");
        testDizini.SayfaEkle(dosyaId, 3, "Sayfa 3", "Kuantum mekaniği prensipleri 3");
        testDizini.SayfaEkle(dosyaId, 4, "Sayfa 4", "Kuantum mekaniği prensipleri 4");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new DosyaAramaMotoru(kaynak);

        var sonuclar = motor.Ara(DosyaSorgusu.Ayristir("icerik:kuantum"));

        Assert.Single(sonuclar);
        Assert.Equal("kuantum.pdf", sonuclar[0].Ad);
    }

    [Fact]
    public void Ara_BosSorgu_BosListeDondurur()
    {
        using var testDizini = new TestDizini();
        testDizini.DosyaEkle(@"C:\test.txt", "test.txt");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new DosyaAramaMotoru(kaynak);

        var sonuclar = motor.Ara(DosyaSorgusu.Ayristir(""));

        Assert.Empty(sonuclar);
    }
}
