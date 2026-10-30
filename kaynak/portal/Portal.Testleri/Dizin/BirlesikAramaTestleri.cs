namespace Portal.Testleri.Dizin;

using Portal.Cekirdek;
using Portal.Cekirdek.Dizin;
using Xunit;

/// <summary>
/// BirlesikArama motorunun çoklu kaynak (video, belge, dosya) orkestrasyonunu doğrulayan testler.
/// </summary>
public class BirlesikAramaTestleri
{
    private sealed class SahteVideoAramaMotoru : IAramaMotoru
    {
        public string? SonSorgu { get; private set; }
        public int AtlananDersSayisi => 0;

        public Task IndeksleAsync(IEnumerable<DersKaydi> dersler, IProgress<int>? ilerleme = null, CancellationToken iptal = default)
            => Task.CompletedTask;

        public IReadOnlyList<AramaSonucu> Ara(string sorgu, int en_fazla = 50)
        {
            SonSorgu = sorgu;
            if (string.IsNullOrWhiteSpace(sorgu))
            {
                return Array.Empty<AramaSonucu>();
            }

            return new List<AramaSonucu>
            {
                new AramaSonucu(
                    DersAd: "Fizik 101",
                    JsonYolu: @"C:\Dersler\fizik.json",
                    SegmentNo: 1,
                    BasSn: 15.0,
                    Metin: "Bu bir video dersi segmentidir",
                    Puan: 90.0,
                    Vurgular: new[] { (0, 2) }
                )
            };
        }

        public void DersiYenile(DersKaydi ders)
        {
        }
    }

    [Fact]
    public async Task AraAsync_DizinNull_YalnizVideoSonuclariDondurur()
    {
        var sahteVideo = new SahteVideoAramaMotoru();
        var arama = new BirlesikArama(sahteVideo, null);

        var sonuc = await arama.AraAsync("deney");

        Assert.Single(sonuc.Videolar);
        Assert.Empty(sonuc.Belgeler);
        Assert.Empty(sonuc.Dosyalar);
        Assert.Null(sonuc.DizinIletisi);
    }

    [Fact]
    public async Task AraAsync_DizinYokken_DizinIletisiDoluVeBelgelerBosDondurur()
    {
        using var testDizini = new TestDizini(guncelYaz: false);
        using var kaynak = testDizini.KaynakOlustur();

        var sahteVideo = new SahteVideoAramaMotoru();
        var arama = new BirlesikArama(sahteVideo, kaynak);

        var sonuc = await arama.AraAsync("deney");

        Assert.Single(sonuc.Videolar);
        Assert.Empty(sonuc.Belgeler);
        Assert.Empty(sonuc.Dosyalar);
        Assert.NotNull(sonuc.DizinIletisi);
        Assert.Equal(kaynak.Ileti, sonuc.DizinIletisi);
    }

    [Fact]
    public async Task AraAsync_IptalEdildiginde_OperationCanceledExceptionFirlatir()
    {
        var sahteVideo = new SahteVideoAramaMotoru();
        var arama = new BirlesikArama(sahteVideo, null);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => arama.AraAsync("test", iptal: cts.Token));
    }

    [Fact]
    public async Task AraAsync_TumKaynaklarHazirken_VideolarBelgelerVeDosyalariBirlestirir()
    {
        using var testDizini = new TestDizini(guncelYaz: true);
        long dosyaId = testDizini.DosyaEkle(@"C:\Belgeler\arastirma.pdf", "arastirma.pdf");
        testDizini.SayfaEkle(dosyaId, 1, "Sayfa 1", "Bu araştırma içeriğinde önemli bulgular bulunmaktadır.");

        using var kaynak = testDizini.KaynakOlustur();
        var sahteVideo = new SahteVideoAramaMotoru();
        var arama = new BirlesikArama(sahteVideo, kaynak);

        var sonuc = await arama.AraAsync("arastirma");

        Assert.Single(sonuc.Videolar);
        Assert.Single(sonuc.Dosyalar);
        Assert.Single(sonuc.Belgeler);
        Assert.Null(sonuc.DizinIletisi);
    }

    [Fact]
    public async Task AraAsync_VideoMotoruna_DuzMetinGonderir()
    {
        var sahteVideo = new SahteVideoAramaMotoru();
        var arama = new BirlesikArama(sahteVideo, null);

        await arama.AraAsync("rapor uz:pdf !eski");

        Assert.Equal("rapor", sahteVideo.SonSorgu);
    }

    [Fact]
    public async Task AraAsync_VideoNullOldugunda_DizinSonuclariDondurulur()
    {
        using var testDizini = new TestDizini(guncelYaz: true);
        long dosyaId = testDizini.DosyaEkle(@"C:\Belgeler\rapor.pdf", "rapor.pdf");
        testDizini.SayfaEkle(dosyaId, 1, "Sayfa 1", "Rapor sayfa metni.");

        using var kaynak = testDizini.KaynakOlustur();
        var arama = new BirlesikArama(null, kaynak);

        var sonuc = await arama.AraAsync("rapor");

        Assert.Empty(sonuc.Videolar);
        Assert.Single(sonuc.Dosyalar);
        Assert.Single(sonuc.Belgeler);
    }
}
