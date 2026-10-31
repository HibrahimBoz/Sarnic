namespace Portal.Testleri;

using Portal.Cekirdek;
using Xunit;

public class PencereGeometrisiTestleri
{
    [Theory]
    [InlineData(0, 0, 1920, 1040, 0, 0, 1920, 1040)]
    [InlineData(60, 0, 1920, 1080, 60, 0, 1860, 1080)]
    [InlineData(0, 40, 1920, 1080, 0, 40, 1920, 1040)]
    public void BuyutmeSiniri_GorevCubugununKonumunuDikkateAlir(
        int sol, int ust, int sag, int alt, int x, int y, int genislik, int yukseklik)
    {
        Assert.Equal((x, y, genislik, yukseklik), PencereGeometrisi.BuyutmeSiniri(
            new(0, 0, 1920, 1080), new(sol, ust, sag, alt), false));
    }

    [Theory]
    [InlineData(-2560, -300)]
    [InlineData(1920, 180)]
    public void BuyutmeSiniri_IkincilMonitorunFizikselKoordinatlariniKullanir(int sol, int ust)
    {
        var monitor = new PikselDikdortgen(sol, ust, sol + 2560, ust + 1440);
        var calisma = monitor with { Sol = sol + 60, Alt = ust + 1400 };
        Assert.Equal((60, 0, 2500, 1400), PencereGeometrisi.BuyutmeSiniri(monitor, calisma, false));
        Assert.Equal((0, 0, 2560, 1440), PencereGeometrisi.BuyutmeSiniri(monitor, calisma, true));
    }

    [Theory]
    [InlineData(1.0, 12)]
    [InlineData(1.25, 9.6)]
    [InlineData(1.5, 8)]
    public void TasmaPayi_DpiyeGoreIcerigiCalismaAlaninaSigdirir(double olcek, double beklenen)
    {
        var pay = PencereGeometrisi.TasmaPayi(new(-1932, -12, 12, 1052), new(-1920, 0, 0, 1040), olcek, olcek);
        Assert.Equal((beklenen, beklenen, beklenen, beklenen), pay);
    }

    [Fact]
    public void TasmaPayi_SinirlarZatenDogruysaIkinciKezBoslukEklemez()
    {
        var alan = new PikselDikdortgen(1920, 0, 4480, 1400);
        Assert.Equal((0d, 0d, 0d, 0d), PencereGeometrisi.TasmaPayi(alan, alan, 1.5, 1.5));
    }
}
