namespace Portal.Testleri;

using Portal.Cekirdek;
using Xunit;

public class LibVlcAyarlariTestleri
{
    [Fact]
    public void SecenekleriBirlestir_VarsayilanlariVeEkleriYinelemesizBirArayaGetirir()
    {
        string[] sonuc = VlcSecenekleri.Birlestir(
        [
            "--avcodec-hw=any",
            "--quiet",
            "--quiet",
            "",
            "  "
        ]);

        string[] beklenen =
        [
            "--avcodec-hw=any",
            "--no-video-title-show",
            "--no-snapshot-preview",
            "--no-osd",
            "--no-stats",
            "--file-caching=300",
            "--network-caching=500",
            "--drop-late-frames",
            "--skip-frames",
            "--sub-autodetect-file",
            "--quiet"
        ];

        Assert.Equal(beklenen, sonuc);
    }

    [Fact]
    public void SecenekleriBirlestir_EkListeOlmadanVarsayilanlariDondurur()
    {
        string[] sonuc = VlcSecenekleri.Birlestir(null);

        Assert.Equal(10, sonuc.Length);
        Assert.Equal(sonuc.Length, sonuc.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void SecenekleriBirlestir_KullaniciSecenegiVarsayilaniGecersizKilar()
    {
        string[] sonuc = VlcSecenekleri.Birlestir(
            ["--file-caching=800", "--avcodec-hw=none", "--network-caching=2000", "--stats", "--osd"]);

        Assert.Contains("--file-caching=800", sonuc);
        Assert.Contains("--network-caching=2000", sonuc);
        Assert.Contains("--avcodec-hw=none", sonuc);
        Assert.Contains("--stats", sonuc);
        Assert.Contains("--osd", sonuc);
        Assert.DoesNotContain("--file-caching=300", sonuc);
        Assert.DoesNotContain("--network-caching=500", sonuc);
        Assert.DoesNotContain("--avcodec-hw=any", sonuc);
        Assert.DoesNotContain("--no-stats", sonuc);
        Assert.DoesNotContain("--no-osd", sonuc);
    }

    [Fact]
    public void SecenekleriBirlestir_AyniAnahtarinSonDegeriKazanir()
    {
        string[] sonuc = VlcSecenekleri.Birlestir(["--file-caching=800", " --file-caching=300 "]);
        Assert.Single(sonuc, s => s.StartsWith("--file-caching=", StringComparison.Ordinal));
        Assert.Contains("--file-caching=300", sonuc);
    }
}
