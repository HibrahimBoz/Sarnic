namespace Portal.Testleri;

using System;
using System.IO;
using Portal.Cekirdek;
using Xunit;

/// <summary>
/// UrunAdi sınıfının başlık biçimlendirme ve ayar.json'dan kurum okuma testleri.
/// </summary>
public sealed class UrunAdiTestleri : IDisposable
{
    private readonly string _geciciKlasor;

    public UrunAdiTestleri()
    {
        _geciciKlasor = Path.Combine(Path.GetTempPath(), "VH_UrunAdiTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_geciciKlasor);
    }

    public void Dispose()
    {
        if (Directory.Exists(_geciciKlasor))
        {
            try
            {
                Directory.Delete(_geciciKlasor, true);
            }
            catch
            {
                // Test temizliği hatasını yut
            }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Baslik_BosVeyaBoslukKurum_SadeceUrunAdiniDondurur(string? kurum)
    {
        string baslik = UrunAdi.Baslik(kurum);
        Assert.Equal("Sarnıç", baslik);
    }

    [Fact]
    public void Baslik_KurumVerildiginde_KurumVeUrunAdiniBirlestirir()
    {
        string baslik = UrunAdi.Baslik(" ABC ");
        Assert.Equal("ABC Sarnıç", baslik);
    }

    [Fact]
    public void KurumOku_DosyaYokken_NullDondurur()
    {
        string olmayanDosya = Path.Combine(_geciciKlasor, "olmayan_ayar.json");
        string? sonuc = UrunAdi.KurumOku(olmayanDosya);
        Assert.Null(sonuc);
    }

    [Fact]
    public void KurumOku_BozukJson_NullDondurur()
    {
        string dosya = Path.Combine(_geciciKlasor, "bozuk.json");
        File.WriteAllText(dosya, "{ bozuk json: true, ");
        string? sonuc = UrunAdi.KurumOku(dosya);
        Assert.Null(sonuc);
    }

    [Fact]
    public void KurumOku_KurumAlaniYokken_NullDondurur()
    {
        string dosya = Path.Combine(_geciciKlasor, "kurum_yok.json");
        File.WriteAllText(dosya, "{\"kok\":\"\"}");
        string? sonuc = UrunAdi.KurumOku(dosya);
        Assert.Null(sonuc);
    }

    [Fact]
    public void KurumOku_GecerliKurumVeTrailingComma_KurumuDondurur()
    {
        string dosya = Path.Combine(_geciciKlasor, "gecerli.json");
        File.WriteAllText(dosya, "{\"kurum\":\"XYZ\", }");
        string? sonuc = UrunAdi.KurumOku(dosya);
        Assert.Equal("XYZ", sonuc);
    }

    [Fact]
    public void KurumOku_SayisalKurum_NullDondurur()
    {
        string dosya = Path.Combine(_geciciKlasor, "sayisal.json");
        File.WriteAllText(dosya, "{\"kurum\": 5}");
        string? sonuc = UrunAdi.KurumOku(dosya);
        Assert.Null(sonuc);
    }

    [Fact]
    public void KurumOku_BoslukKurum_BoslukDondururVeBasliktaSarnicUretir()
    {
        string dosya = Path.Combine(_geciciKlasor, "bosluk.json");
        File.WriteAllText(dosya, "{\"kurum\":\"  \"}");
        string? kurum = UrunAdi.KurumOku(dosya);
        Assert.Equal("  ", kurum);
        Assert.Equal("Sarnıç", UrunAdi.Baslik(kurum));
    }
}
