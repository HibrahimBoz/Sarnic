namespace Portal.Testleri.Dizin;

using Portal.Cekirdek;
using Portal.Cekirdek.Dizin;
using Xunit;

/// <summary>
/// IcerikAramaMotoru, SayfaDeposu ve BelgeYollari işlevlerini doğrulayan testler.
/// </summary>
public class IcerikAramaMotoruTestleri
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Ara_BosSorgu_BosListeDondurur(string? sorgu)
    {
        using var testDizini = new TestDizini();
        testDizini.DosyaEkle(@"C:\Belgeler\not.txt", "not.txt");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new IcerikAramaMotoru(kaynak);

        var sonuclar = motor.Ara(sorgu!);

        Assert.Empty(sonuclar);
    }

    [Fact]
    public void Ara_TurkceDuyarsizlik_FarkliKarakterYazimlariylaEslestirir()
    {
        using var testDizini = new TestDizini();
        long dosyaId = testDizini.DosyaEkle(@"C:\Belgeler\belge.pdf", "belge.pdf");
        testDizini.SayfaEkle(dosyaId, 1, "Sayfa 1", "Bu bölümde Şemsiye ve IŞIKLI ÇEŞME konuları işlenmektedir.");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new IcerikAramaMotoru(kaynak);

        var sonuclar = motor.Ara("semsiye isikli cesme");

        Assert.Single(sonuclar);
        Assert.Equal(dosyaId, sonuclar[0].DosyaId);
    }

    [Fact]
    public void Ara_OnekEslesmesi_HizSozcuguHizlandiriciyiBulur()
    {
        using var testDizini = new TestDizini();
        long dosyaId = testDizini.DosyaEkle(@"C:\Belgeler\fizik.pdf", "fizik.pdf");
        testDizini.SayfaEkle(dosyaId, 1, "Sayfa 1", "Parçacık hızlandırıcı deneyi tamamlandı.");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new IcerikAramaMotoru(kaynak);

        var sonuclar = motor.Ara("hız");

        Assert.Single(sonuclar);
        Assert.Equal("fizik.pdf", sonuclar[0].Ad);
    }

    [Fact]
    public void Ara_VeSemantigi_TumSozcuklerinBulunmasiniZorunluKilar()
    {
        using var testDizini = new TestDizini();
        long dosya1 = testDizini.DosyaEkle(@"C:\Belgeler\tam.txt", "tam.txt");
        testDizini.SayfaEkle(dosya1, 1, "Sayfa 1", "Hem elma hem de armut içeren bir meyve sepeti.");

        long dosya2 = testDizini.DosyaEkle(@"C:\Belgeler\eksik.txt", "eksik.txt");
        testDizini.SayfaEkle(dosya2, 1, "Sayfa 1", "Sadece elma içeren bir sepet.");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new IcerikAramaMotoru(kaynak);

        var sonuclar = motor.Ara("elma armut");

        Assert.Single(sonuclar);
        Assert.Equal(dosya1, sonuclar[0].DosyaId);
    }

    [Fact]
    public void Ara_TamIfadeOnceligi_TamIfadeEslenenSayfayaDahaYuksekPuanVerir()
    {
        using var testDizini = new TestDizini();
        long dosya1 = testDizini.DosyaEkle(@"C:\Belgeler\tam_ifade.txt", "tam_ifade.txt");
        testDizini.SayfaEkle(dosya1, 1, "Sayfa 1", "Burada derin öğrenme modelleri tartışılmaktadır.");

        long dosya2 = testDizini.DosyaEkle(@"C:\Belgeler\ayrik_kelimeler.txt", "ayrik_kelimeler.txt");
        testDizini.SayfaEkle(dosya2, 1, "Sayfa 1", "Derin vadilerde öğrenme sürecini inceledik.");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new IcerikAramaMotoru(kaynak);

        var sonuclar = motor.Ara("derin öğrenme");

        Assert.Equal(2, sonuclar.Count);
        Assert.Equal(dosya1, sonuclar[0].DosyaId);
        Assert.True(sonuclar[0].Puan > sonuclar[1].Puan);
    }

    [Fact]
    public void Ara_BelgeBasinaEnFazlaSiniri_CokSayfaliBelgedeKisitlarVeDigerBelgeleriGetirir()
    {
        using var testDizini = new TestDizini();

        // 12 eşleşen sayfası olan 1. belge
        long dosyaA = testDizini.DosyaEkle(@"C:\Belgeler\belgeA.pdf", "belgeA.pdf");
        for (int i = 1; i <= 12; i++)
        {
            testDizini.SayfaEkle(dosyaA, i, $"Sayfa {i}", $"Aranan kelime bu sayfada geçmektedir no {i}");
        }

        // 2 eşleşen sayfası olan 2. belge
        long dosyaB = testDizini.DosyaEkle(@"C:\Belgeler\belgeB.pdf", "belgeB.pdf");
        testDizini.SayfaEkle(dosyaB, 1, "Sayfa 1", "Aranan kelime ikinci belgede de bulunmaktadır");
        testDizini.SayfaEkle(dosyaB, 2, "Sayfa 2", "Aranan kelime yine bu belgede geçiyor");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new IcerikAramaMotoru(kaynak);

        var sonuclar = motor.Ara("aranan", belgeBasinaEnFazla: 5);

        int belgeASayisi = sonuclar.Count(s => s.DosyaId == dosyaA);
        int belgeBSayisi = sonuclar.Count(s => s.DosyaId == dosyaB);

        Assert.Equal(5, belgeASayisi);
        Assert.True(belgeBSayisi > 0);
    }

    [Fact]
    public void Ara_Suzgec_UzantiyaGoreBelgeTablosuUzerindenFiltreler()
    {
        using var testDizini = new TestDizini();
        long dosyaPdf = testDizini.DosyaEkle(@"C:\Belgeler\rapor.pdf", "rapor.pdf", uzanti: "pdf");
        testDizini.SayfaEkle(dosyaPdf, 1, "Sayfa 1", "Önemli sistem analizi içeriği");

        long dosyaDocx = testDizini.DosyaEkle(@"C:\Belgeler\rapor.docx", "rapor.docx", uzanti: "docx");
        testDizini.SayfaEkle(dosyaDocx, 1, "Sayfa 1", "Önemli sistem analizi içeriği");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new IcerikAramaMotoru(kaynak);

        var sonuclar = motor.Ara("sistem", suzgec: DosyaSorgusu.Ayristir("uz:pdf"));

        Assert.Single(sonuclar);
        Assert.Equal(dosyaPdf, sonuclar[0].DosyaId);
    }

    [Fact]
    public void Ara_OcrKaynakVeGuvenAlanlariniDogruDoldurur()
    {
        using var testDizini = new TestDizini();
        long dosyaId = testDizini.DosyaEkle(@"C:\Taramalar\tarama.pdf", "tarama.pdf");
        testDizini.SayfaEkle(dosyaId, 1, "Sayfa 1", "Taranmış optik karakter metni.", kaynak: "ocr", guven: 0.94);
        testDizini.SayfaEkle(dosyaId, 2, "Sayfa 2", "Doğrudan metin sayfası.", kaynak: "metin", guven: null);

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new IcerikAramaMotoru(kaynak);

        var sonuclarOcr = motor.Ara("optik");
        Assert.Single(sonuclarOcr);
        Assert.Equal("ocr", sonuclarOcr[0].Kaynak);
        Assert.Equal(0.94, sonuclarOcr[0].Guven);

        var sonuclarMetin = motor.Ara("doğrudan");
        Assert.Single(sonuclarMetin);
        Assert.Equal("metin", sonuclarMetin[0].Kaynak);
        Assert.Null(sonuclarMetin[0].Guven);
    }

    [Fact]
    public void Ara_AlintiVeVurguKonumlari_TurkceHarfliMetindeDogruAltDizeyiGosterir()
    {
        using var testDizini = new TestDizini();
        long dosyaId = testDizini.DosyaEkle(@"C:\Belgeler\makale.pdf", "makale.pdf");
        string metin = "Giriş metni burada yer almaktadır. Araştırmada Şemsiye ve IŞIKLI ÇEŞME modelleri test edildi. Sonuçlar başarılı.";
        testDizini.SayfaEkle(dosyaId, 1, "Sayfa 1", metin);

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new IcerikAramaMotoru(kaynak);

        var sonuclar = motor.Ara("isikli");

        Assert.Single(sonuclar);
        var sonuc = sonuclar[0];
        Assert.NotEmpty(sonuc.Alinti);
        Assert.NotEmpty(sonuc.Vurgular);

        foreach (var (bas, uzunluk) in sonuc.Vurgular)
        {
            string altDize = sonuc.Alinti.Substring(bas, uzunluk);
            string normAltDize = TrNormalizasyon.Normalle(altDize);
            Assert.StartsWith("isik", normAltDize);
        }
    }

    [Fact]
    public void SayfaDeposu_SayfalariVeDosyayiDogruGetirir()
    {
        using var testDizini = new TestDizini();
        long dosyaId = testDizini.DosyaEkle(@"C:\Belgeler\belge.pdf", "belge.pdf", sayfaSayisi: 2);
        testDizini.SayfaEkle(dosyaId, 1, "Sayfa 1", "Birinci sayfa metni");
        testDizini.SayfaEkle(dosyaId, 2, "Sayfa 2", "İkinci sayfa metni");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var depo = new SayfaDeposu(kaynak);

        var sayfalar = depo.Getir(dosyaId);
        Assert.Equal(2, sayfalar.Count);
        Assert.Equal(1, sayfalar[0].No);
        Assert.Equal(2, sayfalar[1].No);

        var tekSayfa = depo.Getir(dosyaId, 2);
        Assert.NotNull(tekSayfa);
        Assert.Equal("İkinci sayfa metni", tekSayfa.Metin);

        Assert.Equal(2, depo.SayfaSayisi(dosyaId));

        var dosya = depo.Dosya(dosyaId);
        Assert.NotNull(dosya);
        Assert.Equal("belge.pdf", dosya.Ad);
    }

    [Fact]
    public void BelgeYollari_KlasorVePdfYolunuDogruOlusturur()
    {
        string depo = @"C:\TestDepo";
        string sha = "abcdef1234567890abcdef1234567890abcdef12";

        string beklenenKlasor = Path.Combine(depo, "belgeler", sha[..2], sha);
        Assert.Equal(beklenenKlasor, BelgeYollari.BelgeKlasoru(depo, sha));

        string? aranabilirPdfYokken = BelgeYollari.AranabilirPdf(depo, "olmayan_sha");
        Assert.Null(aranabilirPdfYokken);
    }

    [Fact]
    public void Ara_SiraliCteOptimizasyonu_VeBeraberlikKirici_DogruCalisir()
    {
        using var testDizini = new TestDizini();
        long dosyaId = testDizini.DosyaEkle(@"C:\Belgeler\kitap.pdf", "kitap.pdf");
        // Aynı metne sahip 3 sayfa - puanları eşit olacak
        testDizini.SayfaEkle(dosyaId, 1, "Sayfa 1", "Aynı içerik tekrarı burada yer alır");
        testDizini.SayfaEkle(dosyaId, 2, "Sayfa 2", "Aynı içerik tekrarı burada yer alır");
        testDizini.SayfaEkle(dosyaId, 3, "Sayfa 3", "Aynı içerik tekrarı burada yer alır");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new IcerikAramaMotoru(kaynak);

        var sonuclar = motor.Ara("tekrarı");

        Assert.Equal(3, sonuclar.Count);
        // Beraberlik kırıcı s.id ASC sayesinde sıra 1, 2, 3 olmalı
        Assert.Equal(1, sonuclar[0].SayfaNo);
        Assert.Equal(2, sonuclar[1].SayfaNo);
        Assert.Equal(3, sonuclar[2].SayfaNo);
        // Dış JOIN ile metin ve dosya bilgisi doğru gelmeli
        Assert.Contains("tekrarı", sonuclar[0].Alinti);
        Assert.Equal("kitap.pdf", sonuclar[0].Ad);
    }

    [Fact]
    public void Ara_KarisikVeyaGrubu_TamamenAtlanir()
    {
        using var testDizini = new TestDizini();
        // Dosya yolu gizli içermiyor ama ad rapor.pdf
        long dosyaId = testDizini.DosyaEkle(@"C:\AcikKlasor\rapor.pdf", "rapor.pdf");
        testDizini.SayfaEkle(dosyaId, 1, "Sayfa 1", "Rapor araştırma özeti burada.");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new IcerikAramaMotoru(kaynak);

        // VEYA grubunda Ad ve Yol karışık: "ad:rapor|yol:gizli"
        // Karışık grup tamamen atlanmalı (daraltma yapılmamalı)
        var suzgec = DosyaSorgusu.Ayristir("ad:rapor|yol:gizli");
        var sonuclar = motor.Ara("özeti", suzgec: suzgec);

        Assert.Single(sonuclar);
        Assert.Equal(dosyaId, sonuclar[0].DosyaId);
    }

    [Fact]
    public void Ara_IptalEdildiginde_OperationCanceledExceptionFirlatir()
    {
        using var testDizini = new TestDizini();
        long dosyaId = testDizini.DosyaEkle(@"C:\Belgeler\belge.pdf", "belge.pdf");
        testDizini.SayfaEkle(dosyaId, 1, "Sayfa 1", "İptal testi içeriği");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var motor = new IcerikAramaMotoru(kaynak);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => motor.Ara("içeriği", iptal: cts.Token));
    }
}
