namespace Portal.Testleri.Dizin;

using Portal.Cekirdek.Dizin;
using Xunit;

/// <summary>
/// DizinKaynagi yaşam döngüsü, anlık görüntü yenileme ve bağlantı yönetimini doğrulayan testler.
/// </summary>
public class DizinKaynagiTestleri
{
    [Fact]
    public void Olustur_GuncelTxtYokken_DurumYokVeIletiDoluDondurur()
    {
        using var testDizini = new TestDizini(guncelYaz: false);
        using var kaynak = testDizini.KaynakOlustur();

        Assert.Equal(DizinDurumu.Yok, kaynak.Durum);
        Assert.Null(kaynak.EtkinDosya);
        Assert.Contains("Dizin bulunamadı", kaynak.Ileti);
    }

    [Fact]
    public void Olustur_GuncelTxtVarken_DurumHazirVeEtkinDosyaYerelKlasordedir()
    {
        using var testDizini = new TestDizini(guncelYaz: true);
        testDizini.DosyaEkle(@"C:\test.txt", "test.txt");

        using var kaynak = testDizini.KaynakOlustur();

        Assert.Equal(DizinDurumu.Hazir, kaynak.Durum);
        Assert.NotNull(kaynak.EtkinDosya);
        Assert.True(File.Exists(kaynak.EtkinDosya));
        Assert.StartsWith(testDizini.YerelKlasor, kaynak.EtkinDosya, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(kaynak.YayinZamani);
        Assert.Contains("dosya", kaynak.Ileti, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task YenileAsync_GuncelTxtYeniDbyiGosterince_TrueVeYeniSayilariDondurur()
    {
        using var testDizini = new TestDizini(guncelYaz: true);
        testDizini.DosyaEkle(@"C:\test1.txt", "test1.txt");

        using var kaynak = testDizini.KaynakOlustur();
        var (ilkDosya, ilkSayfa) = kaynak.Sayimlar();
        Assert.Equal(1, ilkDosya);
        Assert.Equal(0, ilkSayfa);

        // Yeni veritabanı hazırla ve 3 dosya + 2 sayfa ekle
        string yeniDbAdi = "dizin-20261030-000000-000002.db";
        string yeniDbYolu = testDizini.YeniDbOlustur(yeniDbAdi);
        long d1 = testDizini.DosyaEkle(@"C:\a.txt", "a.txt", hedefDbYolu: yeniDbYolu);
        long d2 = testDizini.DosyaEkle(@"C:\b.txt", "b.txt", hedefDbYolu: yeniDbYolu);
        long d3 = testDizini.DosyaEkle(@"C:\c.txt", "c.txt", hedefDbYolu: yeniDbYolu);
        testDizini.SayfaEkle(d1, 1, "Sayfa 1", "İçerik 1", hedefDbYolu: yeniDbYolu);
        testDizini.SayfaEkle(d2, 1, "Sayfa 1", "İçerik 2", hedefDbYolu: yeniDbYolu);

        // guncel.txt'yi yeni veritabanına yönlendir
        testDizini.GuncelTxtYaz(yeniDbAdi);

        bool yenilendi = await kaynak.YenileAsync();

        Assert.True(yenilendi);
        var (yeniDosya, yeniSayfa) = kaynak.Sayimlar();
        Assert.Equal(3, yeniDosya);
        Assert.Equal(2, yeniSayfa);
    }

    [Fact]
    public void Olustur_MetaSurumFarkli_DurumHataVeIletideSemaSurumuGecer()
    {
        using var testDizini = new TestDizini(surum: "2", guncelYaz: true);
        using var kaynak = testDizini.KaynakOlustur();

        Assert.Equal(DizinDurumu.Hata, kaynak.Durum);
        Assert.Contains("şema sürümü", kaynak.Ileti, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Olustur_GuncelTxtDizinDisinaCikiyorsa_DurumHataOlur()
    {
        using var testDizini = new TestDizini(guncelYaz: false);
        testDizini.GuncelTxtYaz(@"..\x.db");

        using var kaynak = testDizini.KaynakOlustur();

        Assert.Equal(DizinDurumu.Hata, kaynak.Durum);
    }

    [Fact]
    public void BaglantiAc_HazirDegilken_InvalidOperationExceptionFirlatir()
    {
        using var testDizini = new TestDizini(guncelYaz: false);
        using var kaynak = testDizini.KaynakOlustur();

        Assert.Equal(DizinDurumu.Yok, kaynak.Durum);
        Assert.Throws<InvalidOperationException>(() => kaynak.BaglantiAc());
    }

    [Fact]
    public void BaglantiAc_Hazirken_GecerliSaltOkurBaglantiDondurur()
    {
        using var testDizini = new TestDizini(guncelYaz: true);
        testDizini.DosyaEkle(@"C:\test.txt", "test.txt");

        using var kaynak = testDizini.KaynakOlustur();
        Assert.Equal(DizinDurumu.Hazir, kaynak.Durum);

        using var conn = kaynak.BaglantiAc();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM dosya";
        long count = (long)cmd.ExecuteScalar()!;

        Assert.Equal(1, count);
    }

    [Fact]
    public void DosyadanAc_KopyalamaYapmadanDogrudanAcar()
    {
        using var testDizini = new TestDizini(guncelYaz: true);
        testDizini.DosyaEkle(@"C:\test.txt", "test.txt");

        using var kaynak = DizinKaynagi.DosyadanAc(testDizini.DbYolu);

        Assert.Equal(DizinDurumu.Hazir, kaynak.Durum);
        Assert.Equal(testDizini.DbYolu, kaynak.EtkinDosya);
        Assert.Equal(testDizini.YayinKlasoru, kaynak.DizinKlasoru);
    }
}
