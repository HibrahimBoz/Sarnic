namespace Portal.Testleri;

using Portal.Cekirdek;
using Xunit;

public class DuzenlemeParolasiTestleri : IDisposable
{
    private const string Varsayilan = "ehpm";
    private readonly string _kok = Path.Combine(Path.GetTempPath(), "PortalDuzenlemeParolasiTest", Guid.NewGuid().ToString("N"));

    [Fact]
    public void DogrulaKok_DosyaOlmadanVarsayilaniKabulEder()
    {
        Assert.True(DuzenlemeParolasi.DogrulaKok(_kok, Varsayilan, Varsayilan));
        Assert.True(DuzenlemeParolasi.DogrulaKok("\0", Varsayilan, Varsayilan));
        Assert.False(DuzenlemeParolasi.DogrulaKok(_kok, "yanlis", Varsayilan));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("EHPM")]
    [InlineData(" ehpm ")]
    public void DogrulaKok_BosVeyaFarkliGirisiReddeder(string? girilen)
    {
        Assert.False(DuzenlemeParolasi.DogrulaKok(_kok, girilen, Varsayilan));
    }

    [Fact]
    public void DogrulaKok_BozukDosyadaDaVarsayilaniKabulEder()
    {
        Directory.CreateDirectory(Path.Combine(_kok, ".portal"));
        File.WriteAllText(Path.Combine(_kok, ".portal", "parola.json"), "bozuk json");
        Assert.True(DuzenlemeParolasi.DogrulaKok(_kok, Varsayilan, Varsayilan));
        Assert.False(DuzenlemeParolasi.DogrulaKok(_kok, "yanlis", Varsayilan));
    }

    [Fact]
    public void DogrulaKok_KayitliParolayiVeUstKlasoruKorumayaDevamEder()
    {
        Assert.True(Parola.Olustur(_kok, "KayitliParola123", "test"));
        string alt = Path.Combine(_kok, "ders", "video");
        Assert.True(DuzenlemeParolasi.DogrulaKok(alt, Varsayilan, Varsayilan));
        Assert.True(DuzenlemeParolasi.DogrulaKok(alt, "KayitliParola123", Varsayilan));
        Assert.False(DuzenlemeParolasi.DogrulaKok(alt, "YanlisParola123", Varsayilan));
        Assert.False(Parola.Dogrula(_kok, Varsayilan));
        Assert.Equal(8, Parola.EnAzParolaUzunlugu);
    }

    public void Dispose()
    {
        if (Directory.Exists(_kok)) Directory.Delete(_kok, true);
    }
}
