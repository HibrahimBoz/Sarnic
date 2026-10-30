namespace Portal.Testleri.Dizin;

using Portal.Cekirdek;
using Portal.Cekirdek.Dizin;
using Xunit;

/// <summary>
/// MetinEsleme sınıfının indeks eşleme, vurgulama ve alıntı üretme işlevlerini doğrulayan testler.
/// </summary>
public class MetinEslemeTestleri
{
    [Fact]
    public void Esle_NormDizesi_TrNormalizasyonIleBirebirAynidir()
    {
        string metin = "  Çığ’ın   düşüşü! ";
        var (norm, ozgun) = MetinEsleme.Esle(metin);

        Assert.Equal(TrNormalizasyon.Normalle(metin), norm);
        Assert.Equal(norm.Length, ozgun.Length);
    }

    [Fact]
    public void Esle_OzgunIndeksler_DogruKarakterKonumlariniGosterir()
    {
        string metin = "Işık";
        var (norm, ozgun) = MetinEsleme.Esle(metin);

        Assert.Equal("isik", norm);
        Assert.Equal(4, ozgun.Length);
        Assert.Equal(0, ozgun[0]); // 'I'
        Assert.Equal(1, ozgun[1]); // 'ş'
        Assert.Equal(2, ozgun[2]); // 'ı'
        Assert.Equal(3, ozgun[3]); // 'k'
    }

    [Fact]
    public void Esle_BirlestiriciIsaretlerAtildiginda_OzgunIndeksDogruKalir()
    {
        // 'i' + U+0307 (combining dot above)
        string metin = "i\u0307.txt";
        var (norm, ozgun) = MetinEsleme.Esle(metin);

        // Mn işareti atıldığı için norm uzunluğu 5 olur: "i txt"
        Assert.Equal(TrNormalizasyon.Normalle(metin), norm);
        Assert.Equal(norm.Length, ozgun.Length);
        Assert.Equal(0, ozgun[0]); // 'i'
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Esle_NullVeBosGirdi_BosDondurur(string? metin)
    {
        var (norm, ozgun) = MetinEsleme.Esle(metin);

        Assert.Equal(string.Empty, norm);
        Assert.Empty(ozgun);
    }

    [Fact]
    public void Vurgula_OnekEslesmesi_SozcugunEslesenKisminiDondurur()
    {
        string metin = "Laboratuvarda parçacık hızlandırıcı deneyi yapıldı.";
        var vurgular = MetinEsleme.Vurgula(metin, new[] { "hiz" });

        Assert.Single(vurgular);
        var (bas, uzunluk) = vurgular[0];
        string eslesen = metin.Substring(bas, uzunluk);

        Assert.Equal("hız", eslesen);
        Assert.Equal("hiz", TrNormalizasyon.Normalle(eslesen));
    }

    [Fact]
    public void Vurgula_TurkceBuyukKarakterler_DogruKonumuGosterir()
    {
        string metin = "Rapor kapsamında IŞIK ve ŞEMSİYE incelendi.";
        var vurgular = MetinEsleme.Vurgula(metin, new[] { "isik", "semsiye" });

        Assert.Equal(2, vurgular.Length);

        string ilk = metin.Substring(vurgular[0].Bas, vurgular[0].Uzunluk);
        string ikinci = metin.Substring(vurgular[1].Bas, vurgular[1].Uzunluk);

        Assert.Equal("IŞIK", ilk);
        Assert.Equal("ŞEMSİYE", ikinci);
    }

    [Fact]
    public void Vurgula_CokluSozcuk_ArtanSiradaDondurur()
    {
        string metin = "Derin öğrenme ve yapay zeka uygulamaları";
        var vurgular = MetinEsleme.Vurgula(metin, new[] { "zeka", "derin", "ogrenme" });

        Assert.Equal(3, vurgular.Length);
        Assert.True(vurgular[0].Bas < vurgular[1].Bas);
        Assert.True(vurgular[1].Bas < vurgular[2].Bas);
    }

    [Fact]
    public void Vurgula_NullVeBosGirdi_BosDiziDondurur()
    {
        Assert.Empty(MetinEsleme.Vurgula(null, new[] { "test" }));
        Assert.Empty(MetinEsleme.Vurgula("", new[] { "test" }));
        Assert.Empty(MetinEsleme.Vurgula("Metin", Array.Empty<string>()));
    }

    [Fact]
    public void AlintiUret_KisaMetin_NoktalarOlmadanTamMetniVeVurguyuDondurur()
    {
        string metin = "Kısa bir test cümlesidir.";
        var (alinti, vurgular) = MetinEsleme.AlintiUret(metin, new[] { "test" }, enFazla: 240);

        Assert.Equal(metin, alinti);
        Assert.Single(vurgular);

        string vurgulanan = alinti.Substring(vurgular[0].Bas, vurgular[0].Uzunluk);
        Assert.Equal("test", vurgulanan);
    }

    [Fact]
    public void AlintiUret_UzunMetin_KesisimNoktalarindaUcNoktaEklerVeVurguKonumunuDuzenler()
    {
        string onEk = "Başlangıç bölümü çok uzun bir metin dizisidir ve devam eder. ";
        string orta = "Burada odaklanılan hedefkelime yer almaktadır. ";
        string sonEk = "Sonuç bölümü de oldukça uzun bir şekilde metni tamamlar ve biter.";

        string metin = onEk + onEk + orta + sonEk + sonEk;
        var (alinti, vurgular) = MetinEsleme.AlintiUret(metin, new[] { "hedefkelime" }, enFazla: 80);

        Assert.Contains("…", alinti);
        Assert.Single(vurgular);

        string vurgulanan = alinti.Substring(vurgular[0].Bas, vurgular[0].Uzunluk);
        Assert.Equal("hedefkelime", vurgulanan);
    }

    [Fact]
    public void AlintiUret_EslesmeBulunamazsa_MetninBasiniDondurur()
    {
        string metin = "Burada aranan kelime bulunmuyor ancak genel bir açıklama mevcut.";
        var (alinti, vurgular) = MetinEsleme.AlintiUret(metin, new[] { "olmayan" }, enFazla: 30);

        Assert.Empty(vurgular);
        Assert.StartsWith("Burada", alinti);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AlintiUret_NullVeBosMetin_BosAlintiDondurur(string? metin)
    {
        var (alinti, vurgular) = MetinEsleme.AlintiUret(metin, new[] { "test" });

        Assert.Equal(string.Empty, alinti);
        Assert.Empty(vurgular);
    }
}
