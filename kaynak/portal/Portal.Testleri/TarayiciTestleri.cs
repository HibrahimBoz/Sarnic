namespace Portal.Testleri;

using System.Text;
using Portal.Cekirdek;
using Xunit;

public class TarayiciTestleri : IDisposable
{
    private readonly string _geciciKokKlasor;

    public TarayiciTestleri()
    {
        _geciciKokKlasor = Path.Combine(Path.GetTempPath(), "TarayiciTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_geciciKokKlasor);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_geciciKokKlasor))
            {
                Directory.Delete(_geciciKokKlasor, true);
            }
        }
        catch
        {
            // Temizleme hatalarını yut
        }
    }

    [Fact]
    public void Tara_GeciciKlasorSentetikBesDers_BeklenenSonuclariDondurur()
    {
        // 1. Ders: Normal, işlenmiş videolu
        string ders1Ust = Path.Combine(_geciciKokKlasor, "ders1");
        string ders1Klasor = Path.Combine(ders1Ust, "ders1_islenmis");
        Directory.CreateDirectory(ders1Klasor);
        File.WriteAllText(Path.Combine(ders1Klasor, "ders1.json"), "{}", Encoding.UTF8);
        File.WriteAllBytes(Path.Combine(ders1Klasor, "ders1_islenmis.mp4"), new byte[] { 1, 2, 3 });

        // 2. Ders: Sadece orijinal video var (üst klasörde ders2.mp4)
        string ders2Ust = Path.Combine(_geciciKokKlasor, "ders2");
        string ders2Klasor = Path.Combine(ders2Ust, "ders2_islenmis");
        Directory.CreateDirectory(ders2Klasor);
        File.WriteAllText(Path.Combine(ders2Klasor, "ders2.json"), "{}", Encoding.UTF8);
        File.WriteAllBytes(Path.Combine(ders2Ust, "ders2.mp4"), new byte[] { 1, 2, 3 });

        // 3. Ders: Videosuz ders (hiçbir video yok)
        string ders3Ust = Path.Combine(_geciciKokKlasor, "ders3");
        string ders3Klasor = Path.Combine(ders3Ust, "ders3_islenmis");
        Directory.CreateDirectory(ders3Klasor);
        File.WriteAllText(Path.Combine(ders3Klasor, "ders3.json"), "{}", Encoding.UTF8);

        // 4. Ders: İç içe klasörler altında
        string ders4Ust = Path.Combine(_geciciKokKlasor, "donem2026", "matematik", "analiz");
        string ders4Klasor = Path.Combine(ders4Ust, "analiz_islenmis");
        Directory.CreateDirectory(ders4Klasor);
        File.WriteAllText(Path.Combine(ders4Klasor, "analiz.json"), "{}", Encoding.UTF8);
        File.WriteAllBytes(Path.Combine(ders4Klasor, "analiz_islenmis.mp4"), new byte[] { 1, 2, 3 });

        // 5. Ders: Bozuk / geçersiz ders (json dosyası bulunmayan *_islenmis klasörü)
        string ders5Ust = Path.Combine(_geciciKokKlasor, "ders5_bozuk");
        string ders5Klasor = Path.Combine(ders5Ust, "ders5_islenmis");
        Directory.CreateDirectory(ders5Klasor);
        // JSON dosyası yazmıyoruz -> bu klasör atlanmalı

        var sonuclar = Tarayici.Tara(_geciciKokKlasor);

        // Bozuk klasör atlanmış olmalı, toplam 4 geçerli ders bulunmalı
        Assert.Equal(4, sonuclar.Count);

        // Ders 1 doğrulaması
        var k1 = sonuclar.FirstOrDefault(k => k.Ad == "ders1");
        Assert.NotNull(k1);
        Assert.Equal(Path.Combine(ders1Klasor, "ders1_islenmis.mp4"), k1.VideoYolu);
        Assert.Equal(Path.Combine(ders1Klasor, "ders1.json"), k1.JsonYolu);

        // Ders 2 doğrulaması (üst klasördeki orijinal video)
        var k2 = sonuclar.FirstOrDefault(k => k.Ad == "ders2");
        Assert.NotNull(k2);
        Assert.Equal(Path.Combine(ders2Ust, "ders2.mp4"), k2.VideoYolu);

        // Ders 3 doğrulaması (videosuz)
        var k3 = sonuclar.FirstOrDefault(k => k.Ad == "ders3");
        Assert.NotNull(k3);
        Assert.Null(k3.VideoYolu);

        // Ders 4 doğrulaması (iç içe klasör)
        var k4 = sonuclar.FirstOrDefault(k => k.Ad == "analiz");
        Assert.NotNull(k4);
        Assert.Equal(Path.Combine(ders4Klasor, "analiz_islenmis.mp4"), k4.VideoYolu);
    }

    [Fact]
    public void Tara_YeniVeEskiEkleriAyniKokteBirlikteListeler()
    {
        string dersUst = Path.Combine(_geciciKokKlasor, "dersler");
        string yeniKlasor = Path.Combine(dersUst, "x_IsLeNmIs");
        string eskiKlasor = Path.Combine(dersUst, "x_videoperisi");
        Directory.CreateDirectory(yeniKlasor);
        Directory.CreateDirectory(eskiKlasor);
        File.WriteAllText(Path.Combine(yeniKlasor, "x.json"), "{}", Encoding.UTF8);
        File.WriteAllText(Path.Combine(eskiKlasor, "x.json"), "{}", Encoding.UTF8);

        var sonuclar = Tarayici.Tara(dersUst);

        Assert.Equal(2, sonuclar.Count);
        Assert.All(sonuclar, kayit => Assert.Equal("x", kayit.Ad));
        Assert.Contains(sonuclar, kayit => kayit.JsonYolu == Path.Combine(yeniKlasor, "x.json"));
        Assert.Contains(sonuclar, kayit => kayit.JsonYolu == Path.Combine(eskiKlasor, "x.json"));
    }

    [Fact]
    public void Tara_IslenmisMp4OrijinalVideoyaGoreOnceliklidir()
    {
        string ustKlasor = Path.Combine(_geciciKokKlasor, "oncelik_test");
        string vpKlasor = Path.Combine(ustKlasor, "ders_islenmis");
        Directory.CreateDirectory(vpKlasor);

        File.WriteAllText(Path.Combine(vpKlasor, "ders.json"), "{}", Encoding.UTF8);
        string islenmis = Path.Combine(vpKlasor, "ders_islenmis.mp4");
        string orijinal = Path.Combine(ustKlasor, "ders.mp4");

        File.WriteAllBytes(islenmis, new byte[] { 1 });
        File.WriteAllBytes(orijinal, new byte[] { 1 });

        var sonuclar = Tarayici.Tara(_geciciKokKlasor);

        var kayit = Assert.Single(sonuclar);
        Assert.Equal("ders", kayit.Ad);
        Assert.Equal(islenmis, kayit.VideoYolu);
    }

    [Fact]
    public void Tara_OrijinalVideoUzantilariSirasi_Mp4VeMkvArasindaMp4Secilir()
    {
        string ustKlasor = Path.Combine(_geciciKokKlasor, "uzanti_test");
        string vpKlasor = Path.Combine(ustKlasor, "ders_islenmis");
        Directory.CreateDirectory(vpKlasor);

        File.WriteAllText(Path.Combine(vpKlasor, "ders.json"), "{}", Encoding.UTF8);
        string mp4Yolu = Path.Combine(ustKlasor, "ders.mp4");
        string mkvYolu = Path.Combine(ustKlasor, "ders.mkv");

        File.WriteAllBytes(mp4Yolu, new byte[] { 1 });
        File.WriteAllBytes(mkvYolu, new byte[] { 1 });

        var sonuclar = Tarayici.Tara(_geciciKokKlasor);

        var kayit = Assert.Single(sonuclar);
        Assert.Equal(mp4Yolu, kayit.VideoYolu);
    }

    [Fact]
    public void Tara_OrijinalVideoUzantilari_MkvMovVeAviFormatlariniDestekler()
    {
        string ustKlasor = Path.Combine(_geciciKokKlasor, "avi_test");
        string vpKlasor = Path.Combine(ustKlasor, "video_islenmis");
        Directory.CreateDirectory(vpKlasor);

        File.WriteAllText(Path.Combine(vpKlasor, "video.json"), "{}", Encoding.UTF8);
        string aviYolu = Path.Combine(ustKlasor, "video.avi");
        File.WriteAllBytes(aviYolu, new byte[] { 1 });

        var sonuclar = Tarayici.Tara(_geciciKokKlasor);

        var kayit = Assert.Single(sonuclar);
        Assert.Equal(aviYolu, kayit.VideoYolu);
    }

    [Fact]
    public void Tara_OlmayanKokKlasor_BosListeDondurur()
    {
        string olmayan = Path.Combine(_geciciKokKlasor, "olmayan_klasor_123");
        var sonuclar = Tarayici.Tara(olmayan);
        Assert.Empty(sonuclar);
    }

    [Fact]
    public void Tara_BosKokKlasor_BosListeDondurur()
    {
        string bosKlasor = Path.Combine(_geciciKokKlasor, "bos_dizin");
        Directory.CreateDirectory(bosKlasor);

        var sonuclar = Tarayici.Tara(bosKlasor);
        Assert.Empty(sonuclar);
    }
}
