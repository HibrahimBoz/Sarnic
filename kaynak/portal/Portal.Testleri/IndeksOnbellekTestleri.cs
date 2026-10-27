namespace Portal.Testleri;

using Portal.Cekirdek;
using Xunit;

public class IndeksOnbellekTestleri : IDisposable
{
    private readonly string _geciciKlasor;
    private readonly string _onbellekDosyasi;

    public IndeksOnbellekTestleri()
    {
        _geciciKlasor = Path.Combine(Path.GetTempPath(), "VH_OnbellekTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_geciciKlasor);
        _onbellekDosyasi = Path.Combine(_geciciKlasor, "indeks.json");
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
                // Test temizleme hatasını yut
            }
        }
    }

    private DersKaydi OrnekDersDosyasiOlustur(string dersAdi)
    {
        string dersKlasor = Path.Combine(_geciciKlasor, $"{dersAdi}_islenmis");
        Directory.CreateDirectory(dersKlasor);

        string jsonYolu = Path.Combine(dersKlasor, $"{dersAdi}.json");
        string json = $$"""
        {
          "sema_surumu": 2,
          "ad": "{{dersAdi}}",
          "segmentler": [
            { "no": 0, "bas": 0.0, "son": 5.0, "metin": "Örnek ders içeriği {{dersAdi}}" }
          ]
        }
        """;
        File.WriteAllText(jsonYolu, json);

        return new DersKaydi(dersAdi, dersKlasor, jsonYolu, null);
    }

    [Fact]
    public async Task Onbellek_IkinciKurulumda_DisktenOkumaSayisiSifir()
    {
        var ders1 = OrnekDersDosyasiOlustur("ders1");
        var ders2 = OrnekDersDosyasiOlustur("ders2");
        var dersler = new[] { ders1, ders2 };

        var onbellek = new IndeksOnbellek(_onbellekDosyasi);

        int diskOkumaSayaci = 0;
        Transkript Okuyucu(string yol)
        {
            Interlocked.Increment(ref diskOkumaSayaci);
            return TranskriptOkuyucu.Oku(yol);
        }

        // 1. Kurulum: Diskten okunur
        using (var motor1 = new MetinAramaMotoru(onbellek, transkriptOkuyucu: Okuyucu))
        {
            await motor1.IndeksleAsync(dersler);
            Assert.Equal(2, diskOkumaSayaci);
            Assert.Equal(2, motor1.DiskOkumaSayisi);
        }

        Assert.True(File.Exists(_onbellekDosyasi));

        // 2. Kurulum: Önbellekten okunur, disk okuma sayısı 0 olmalı!
        diskOkumaSayaci = 0;
        using (var motor2 = new MetinAramaMotoru(onbellek, transkriptOkuyucu: Okuyucu))
        {
            await motor2.IndeksleAsync(dersler);
            Assert.Equal(0, diskOkumaSayaci);
            Assert.Equal(0, motor2.DiskOkumaSayisi);

            // Arama doğrulaması
            var sonuclar = motor2.Ara("ders1");
            Assert.Single(sonuclar);
        }
    }

    [Fact]
    public async Task Onbellek_DegisenDers_YenidenOkunur_DegismeyenOkunmaz()
    {
        var ders1 = OrnekDersDosyasiOlustur("ders1");
        var ders2 = OrnekDersDosyasiOlustur("ders2");
        var dersler = new[] { ders1, ders2 };

        var onbellek = new IndeksOnbellek(_onbellekDosyasi);

        int diskOkumaSayaci = 0;
        Transkript Okuyucu(string yol)
        {
            Interlocked.Increment(ref diskOkumaSayaci);
            return TranskriptOkuyucu.Oku(yol);
        }

        // 1. Kurulum
        using (var motor1 = new MetinAramaMotoru(onbellek, transkriptOkuyucu: Okuyucu))
        {
            await motor1.IndeksleAsync(dersler);
            Assert.Equal(2, diskOkumaSayaci);
        }

        // Sadece ders1'i güncelle (LastWriteTimeUtc ve içerik değişsin)
        Thread.Sleep(20);
        string guncelJson = """
        {
          "sema_surumu": 2,
          "ad": "ders1",
          "segmentler": [
            { "no": 0, "bas": 0.0, "son": 5.0, "metin": "Tamamen güncellenmiş ders1 metni" }
          ]
        }
        """;
        File.WriteAllText(ders1.JsonYolu, guncelJson);
        File.SetLastWriteTimeUtc(ders1.JsonYolu, DateTime.UtcNow.AddMinutes(1));

        // 2. Kurulum: Sadece ders1 yeniden okunmalı, ders2 önbellekten gelmeli
        diskOkumaSayaci = 0;
        using (var motor2 = new MetinAramaMotoru(onbellek, transkriptOkuyucu: Okuyucu))
        {
            await motor2.IndeksleAsync(dersler);
            Assert.Equal(1, diskOkumaSayaci);
            Assert.Equal(1, motor2.DiskOkumaSayisi);

            var bulunan1 = motor2.Ara("güncellenmiş");
            Assert.Single(bulunan1);

            var bulunan2 = motor2.Ara("ders2");
            Assert.Single(bulunan2);
        }
    }

    [Fact]
    public async Task Onbellek_DuzeltmelerKlasoruDegisince_DersYenidenOkunur()
    {
        var ders1 = OrnekDersDosyasiOlustur("ders1");
        var dersler = new[] { ders1 };

        var onbellek = new IndeksOnbellek(_onbellekDosyasi);
        var depo = new DuzeltmeDeposu(ders1.KlasorYolu);

        // 1. Kurulum
        using (var motor1 = new MetinAramaMotoru(onbellek, duzeltmeDeposu: depo))
        {
            await motor1.IndeksleAsync(dersler);
            Assert.Equal(1, motor1.DiskOkumaSayisi);
        }

        // Düzeltme dosyası ekle
        Thread.Sleep(20);
        string duzeltmeKlasoru = Path.Combine(ders1.KlasorYolu, $"{ders1.Ad}.duzeltmeler");
        var d = new Duzeltme
        {
            Ders = ders1.Ad,
            SegmentNo = 0,
            Eski = "Örnek ders içeriği ders1",
            Yeni = "Düzeltilmiş ders içeriği ders1",
            Tur = "duzelt"
        };
        depo.Yaz(duzeltmeKlasoru, d);

        // 2. Kurulum: Düzeltme klasörü değiştiği için ders diskten tekrar okunmalı
        using (var motor2 = new MetinAramaMotoru(onbellek, duzeltmeDeposu: depo))
        {
            await motor2.IndeksleAsync(dersler);
            Assert.Equal(1, motor2.DiskOkumaSayisi);

            var bulunan = motor2.Ara("Düzeltilmiş");
            Assert.Single(bulunan);
        }
    }

    [Fact]
    public async Task Onbellek_BozukDosya_SessizceYokSayilir_VeYenidenKurulur()
    {
        var ders1 = OrnekDersDosyasiOlustur("ders1");
        var dersler = new[] { ders1 };

        // Bozuk JSON dosyası oluştur
        File.WriteAllText(_onbellekDosyasi, "BOZUK_JSON_ICERIGI_{{{[[]");

        var onbellek = new IndeksOnbellek(_onbellekDosyasi);

        using var motor = new MetinAramaMotoru(onbellek);
        // Hata fırlatmamalı, sessizce yeniden kurmalı
        await motor.IndeksleAsync(dersler);

        Assert.Equal(1, motor.DiskOkumaSayisi);
        var sonuclar = motor.Ara("ders1");
        Assert.Single(sonuclar);

        // Önbellek geçerli JSON olarak yeniden yazılmış olmalı
        var veri = onbellek.Yukle();
        Assert.NotNull(veri);
        Assert.Equal(IndeksOnbellek.GuncelSurum, veri.Surum);
    }

    [Fact]
    public async Task Onbellek_DerslerNullGecerliJson_SessizceYokSayilir_VeYenidenKurulur()
    {
        var ders1 = OrnekDersDosyasiOlustur("ders1");
        var dersler = new[] { ders1 };

        // Geçerli JSON ama Dersler alanı null (bozuk önbellek)
        string bozukJson = """
        {
          "Surum": 2,
          "KayitZamaniUtc": "2026-01-01T00:00:00Z",
          "Dersler": null
        }
        """;
        File.WriteAllText(_onbellekDosyasi, bozukJson);

        var onbellek = new IndeksOnbellek(_onbellekDosyasi);

        // Bozuk yapı nedeniyle doğrudan okuma null dönmeli
        Assert.Null(onbellek.Yukle());

        using var motor = new MetinAramaMotoru(onbellek);
        // NullReference ile çökmemeli, sessizce yok sayıp yeniden kurmalı
        await motor.IndeksleAsync(dersler);

        Assert.Equal(1, motor.DiskOkumaSayisi);
        var sonuclar = motor.Ara("ders1");
        Assert.Single(sonuclar);

        // Önbellek sağlıklı şekilde yeniden oluşturulup kaydedilmiş olmalı
        var tazeVeri = onbellek.Yukle();
        Assert.NotNull(tazeVeri);
        Assert.NotNull(tazeVeri.Dersler);
        Assert.True(tazeVeri.Dersler.ContainsKey(ders1.IndeksAnahtari));
    }

    [Fact]
    public async Task Onbellek_BozukDersIcerigi_SessizceYokSayilir_VeYenidenKurulur()
    {
        var ders1 = OrnekDersDosyasiOlustur("ders1");
        var dersler = new[] { ders1 };

        // Geçerli JSON ama Segmentler veya Anahtar null olan ders nesnesi
        string bozukDersJson = """
        {
          "Surum": 1,
          "KayitZamaniUtc": "2026-01-01T00:00:00Z",
          "Dersler": {
            "ders1": {
              "DersAd": "ders1",
              "JsonYolu": "ornek.json",
              "Anahtar": null,
              "Segmentler": null
            }
          }
        }
        """;
        File.WriteAllText(_onbellekDosyasi, bozukDersJson);

        var onbellek = new IndeksOnbellek(_onbellekDosyasi);
        Assert.Null(onbellek.Yukle());

        using var motor = new MetinAramaMotoru(onbellek);
        await motor.IndeksleAsync(dersler);

        Assert.Equal(1, motor.DiskOkumaSayisi);
        var sonuclar = motor.Ara("ders1");
        Assert.Single(sonuclar);
    }

    [Fact]
    public async Task Onbellek_EskiSurumDosya_SessizceYokSayilir()
    {
        var ders1 = OrnekDersDosyasiOlustur("ders1");
        var dersler = new[] { ders1 };

        // Eski sürüm JSON
        string eskiJson = """
        {
          "Surum": 999,
          "KayitZamaniUtc": "2026-01-01T00:00:00Z",
          "Dersler": {}
        }
        """;
        File.WriteAllText(_onbellekDosyasi, eskiJson);

        var onbellek = new IndeksOnbellek(_onbellekDosyasi);

        using var motor = new MetinAramaMotoru(onbellek);
        await motor.IndeksleAsync(dersler);

        Assert.Equal(1, motor.DiskOkumaSayisi);
    }

    [Fact]
    public void Onbellek_AtomikYazim_TmpDosyasiKalmaz_VeDosyaYazilir()
    {
        var onbellek = new IndeksOnbellek(_onbellekDosyasi);
        var veri = new OnbellekVerisi
        {
            Surum = IndeksOnbellek.GuncelSurum,
            KayitZamaniUtc = DateTime.UtcNow
        };

        onbellek.Kaydet(veri);

        Assert.True(File.Exists(_onbellekDosyasi));
        var tmpDosyalari = Directory.GetFiles(_geciciKlasor, "*.tmp");
        Assert.Empty(tmpDosyalari);

        var yuklenen = onbellek.Yukle();
        Assert.NotNull(yuklenen);
        Assert.Equal(IndeksOnbellek.GuncelSurum, yuklenen.Surum);
    }

    [Fact]
    public void Onbellek_Sha1Hesapla_Tutarlidir()
    {
        string girdi = "C:\\Videolar\\Dersler";
        string hash1 = IndeksOnbellek.Sha1Hesapla(girdi);
        string hash2 = IndeksOnbellek.Sha1Hesapla(girdi.ToUpperInvariant());

        Assert.Equal(hash1, hash2); // Küçük harfe normalize edildiği için eşit
        Assert.Equal(40, hash1.Length);
    }

    [Fact]
    public void Onbellek_OzelKlasorYoluEnjekteEdildiginde_OrayaYazar()
    {
        string ozelKlasor = Path.Combine(_geciciKlasor, "alt_klasor");
        var onbellek = new IndeksOnbellek(ozelKlasor, "C:\\Dersler");

        string dosyaYolu = onbellek.DosyaYoluBelirle();
        Assert.StartsWith(Path.GetFullPath(ozelKlasor), dosyaYolu, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".json", dosyaYolu, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Onbellek_SegmentlerNullElemanIcerirse_DisktenYuklenir_IndekslemeBasarili()
    {
        var ders1 = OrnekDersDosyasiOlustur("ders1");
        var dersler = new[] { ders1 };

        // Geçerli JSON ve Sürüm 2, ancak Segmentler listesinde null eleman var: [ null ]
        string bozukSegmentliJson = """
        {
          "Surum": 2,
          "KayitZamaniUtc": "2026-01-01T00:00:00Z",
          "Dersler": {
            "ders1": {
              "DersAd": "ders1",
              "JsonYolu": "ornek.json",
              "Anahtar": {
                "JsonZamanUtcTicks": 0,
                "JsonBoyut": 0,
                "DuzeltmeSayisi": 0,
                "DuzeltmeEnYeniUtcTicks": 0
              },
              "Segmentler": [
                null
              ]
            }
          }
        }
        """;
        File.WriteAllText(_onbellekDosyasi, bozukSegmentliJson);

        var onbellek = new IndeksOnbellek(_onbellekDosyasi);

        using var motor = new MetinAramaMotoru(onbellek);
        // NullReferenceException ile düşmemeli, bozuk ders önbelleğini atıp diskten yüklemeli
        await motor.IndeksleAsync(dersler);

        Assert.Equal(1, motor.DiskOkumaSayisi);
        Assert.Equal(0, motor.AtlananDersSayisi);

        var sonuclar = motor.Ara("ders1");
        Assert.Single(sonuclar);
    }
}
