namespace Portal.Testleri;

using Portal.Cekirdek;
using Xunit;

public class KaydiracHesabiTestleri
{
    [Theory]
    [InlineData(10, 0)]
    [InlineData(60, 25)]
    [InlineData(110, 50)]
    [InlineData(210, 100)]
    [InlineData(-30, 0)]
    [InlineData(300, 100)]
    public void ZamanHesapla_TiklamaVeIzDisinaSuruklemeyiHesaplar(double konum, double beklenen)
    {
        Assert.Equal(beklenen, KaydiracHesabi.ZamanHesapla(konum, 220, 20, 100));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ZamanHesapla_BilinmeyenSuredeAramaz(double sure)
    {
        Assert.Null(KaydiracHesabi.ZamanHesapla(50, 100, 12, sure));
    }

    [Theory]
    [InlineData(0, 12)]
    [InlineData(12, 12)]
    [InlineData(double.NaN, 12)]
    [InlineData(100, double.NaN)]
    [InlineData(100, -1)]
    public void ZamanHesapla_OlculmemisIzdeAramaz(double genislik, double tutamac)
    {
        Assert.Null(KaydiracHesabi.ZamanHesapla(50, genislik, tutamac, 100));
    }

    [Fact]
    public void ZamanHesapla_TersYonuVeGecersizKonumuIsler()
    {
        Assert.Equal(75, KaydiracHesabi.ZamanHesapla(60, 220, 20, 100, ters: true));
        Assert.Null(KaydiracHesabi.ZamanHesapla(double.NaN, 220, 20, 100));
    }
}
