namespace Portal.Testleri;

using System.Diagnostics;
using Portal.Cekirdek;
using Xunit;

public class MetinAramaMotoruTestleri : IDisposable
{
    private readonly string _geciciKlasor;

    public MetinAramaMotoruTestleri()
    {
        _geciciKlasor = Path.Combine(Path.GetTempPath(), "VH_AramaTest_" + Guid.NewGuid().ToString("N"));
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
                // Test temizleme hatasını yut
            }
        }
    }

    private static DersKaydi OrnekDersUret(string dersAdi = "fizik101", string klasor = "")
    {
        string k = string.IsNullOrEmpty(klasor) ? Path.GetTempPath() : klasor;
        return new DersKaydi(dersAdi, k, Path.Combine(k, $"{dersAdi}.json"), null);
    }

    [Theory]
    [InlineData("ISPARTA")]
    [InlineData("ısparta")]
    [InlineData("Isparta")]
    [InlineData("İsparta")]
    public async Task TurkceDuyarlilik_IspartaVaryasyonlari_AyniSonucuVerir(string sorgu)
    {
        var ders = OrnekDersUret();
        var transkript = new Transkript
        {
            Ad = ders.Ad,
            JsonYolu = ders.JsonYolu,
            Segmentler = new List<Segment>
            {
                new(0, 0.0, 5.0, "Isparta ilinde gül hasadı başladı"),
                new(1, 5.0, 10.0, "Konya ovasında buğday yetişir")
            }
        };

        using var motor = new MetinAramaMotoru(transkriptOkuyucu: _ => transkript);
        await motor.IndeksleAsync(new[] { ders });

        var sonuclar = motor.Ara(sorgu);

        Assert.Single(sonuclar);
        Assert.Equal(0, sonuclar[0].SegmentNo);
        Assert.Equal(100.0, sonuclar[0].Puan);
        Assert.NotEmpty(sonuclar[0].Vurgular);
        var v = sonuclar[0].Vurgular[0];
        Assert.Equal("Isparta", sonuclar[0].Metin.Substring(v.Bas, v.Uzunluk));
    }

    [Fact]
    public async Task TurkceDuyarlilik_SapkaliVeOzelHarfler_DogruEslenir()
    {
        var ders = OrnekDersUret();
        var transkript = new Transkript
        {
            Ad = ders.Ad,
            JsonYolu = ders.JsonYolu,
            Segmentler = new List<Segment>
            {
                new(0, 0.0, 5.0, "Resmî kâğıt üzerine sükût yemini yazıldı"),
                new(1, 5.0, 10.0, "Şemsiye altında çiğdem ve çiçek topladık")
            }
        };

        using var motor = new MetinAramaMotoru(transkriptOkuyucu: _ => transkript);
        await motor.IndeksleAsync(new[] { ders });

        var sonuclarKagit = motor.Ara("kagit");
        Assert.Single(sonuclarKagit);
        Assert.Equal(0, sonuclarKagit[0].SegmentNo);

        var sonuclarResmi = motor.Ara("resmi");
        Assert.Single(sonuclarResmi);
        Assert.Equal(0, sonuclarResmi[0].SegmentNo);

        var sonuclarSemsiye = motor.Ara("semsiye cigdem");
        Assert.Single(sonuclarSemsiye);
        Assert.Equal(1, sonuclarSemsiye[0].SegmentNo);
    }

    [Fact]
    public async Task OnekEslesmesi_KelimeBasiAramada_Bulunur()
    {
        var ders = OrnekDersUret();
        var transkript = new Transkript
        {
            Ad = ders.Ad,
            JsonYolu = ders.JsonYolu,
            Segmentler = new List<Segment>
            {
                new(0, 0.0, 5.0, "Türev alma kurallarını öğreniyoruz"),
                new(1, 5.0, 10.0, "İntegral hesapları oldukça zevklidir")
            }
        };

        using var motor = new MetinAramaMotoru(transkriptOkuyucu: _ => transkript);
        await motor.IndeksleAsync(new[] { ders });

        var sonuclar = motor.Ara("tür");
        Assert.Single(sonuclar);
        Assert.Equal(0, sonuclar[0].SegmentNo);
    }

    [Fact]
    public async Task OnekEslesmesi_KelimeOrtasiKelimeBasiDegilse_Tier2OlarakBulunmaz()
    {
        var ders = OrnekDersUret();
        var transkript = new Transkript
        {
            Ad = ders.Ad,
            JsonYolu = ders.JsonYolu,
            Segmentler = new List<Segment>
            {
                new(0, 0.0, 5.0, "Türev formülü")
            }
        };

        using var motor = new MetinAramaMotoru(transkriptOkuyucu: _ => transkript);
        await motor.IndeksleAsync(new[] { ders });

        // 'rev' kelimesi 'türev' kelimesinin öneki değildir. 'rev formulu' ifadesinde tam alt dize de yok.
        var sonuclar = motor.Ara("rev formulu");
        // 'rev' kelimesi önek eşleşmesi sağlamaz, dolayısıyla tüm kelimeler önek eşleşmesi (Tier 2) olmaz
        // 2 kelimeden sadece 1'i (formulu) eşleştiği için %50 < %60 -> bulunmaz
        Assert.Empty(sonuclar);
    }

    [Fact]
    public async Task TamIfadeEslesmesi_BitisikKelimeleriBulur()
    {
        var ders = OrnekDersUret();
        var transkript = new Transkript
        {
            Ad = ders.Ad,
            JsonYolu = ders.JsonYolu,
            Segmentler = new List<Segment>
            {
                new(0, 0.0, 5.0, "Türev alma formülü tahtadadır"),
                new(1, 5.0, 10.0, "Formülü alma ve türev kuralları")
            }
        };

        using var motor = new MetinAramaMotoru(transkriptOkuyucu: _ => transkript);
        await motor.IndeksleAsync(new[] { ders });

        var sonuclar = motor.Ara("türev alma");

        Assert.Equal(2, sonuclar.Count);
        Assert.Equal(0, sonuclar[0].SegmentNo); // Tam ifade 100 puan ile birinci
        Assert.Equal(100.0, sonuclar[0].Puan);

        Assert.Equal(1, sonuclar[1].SegmentNo); // Ters sıra tüm kelimeler 50 puan ile ikinci
        Assert.Equal(50.0, sonuclar[1].Puan);
    }

    [Fact]
    public async Task KismiEslesmeEsigi_TekKelimeliSorguda_KismiEslesmeYoktur()
    {
        var ders = OrnekDersUret();
        var transkript = new Transkript
        {
            Ad = ders.Ad,
            JsonYolu = ders.JsonYolu,
            Segmentler = new List<Segment>
            {
                new(0, 0.0, 5.0, "Matematik ve geometri dersi")
            }
        };

        using var motor = new MetinAramaMotoru(transkriptOkuyucu: _ => transkript);
        await motor.IndeksleAsync(new[] { ders });

        var sonuclar = motor.Ara("fizik");
        Assert.Empty(sonuclar);
    }

    [Fact]
    public async Task KismiEslesmeEsigi_IkiKelimeliSorguda_YuzdeElliEslesmez()
    {
        var ders = OrnekDersUret();
        var transkript = new Transkript
        {
            Ad = ders.Ad,
            JsonYolu = ders.JsonYolu,
            Segmentler = new List<Segment>
            {
                new(0, 0.0, 5.0, "Fizik deneyleri yapıldı")
            }
        };

        using var motor = new MetinAramaMotoru(transkriptOkuyucu: _ => transkript);
        await motor.IndeksleAsync(new[] { ders });

        // "fizik kimya": 2 kelimeden 1'i eşleşiyor (%50 < %60 eşik) -> sonuç dönmemeli
        var sonuclar = motor.Ara("fizik kimya");
        Assert.Empty(sonuclar);
    }

    [Fact]
    public async Task KismiEslesmeEsigi_UcKelimeliSorguda_YuzdeAltmisAltiEslesir()
    {
        var ders = OrnekDersUret();
        var transkript = new Transkript
        {
            Ad = ders.Ad,
            JsonYolu = ders.JsonYolu,
            Segmentler = new List<Segment>
            {
                new(0, 0.0, 5.0, "Fizik ve kimya laboratuvarı")
            }
        };

        using var motor = new MetinAramaMotoru(transkriptOkuyucu: _ => transkript);
        await motor.IndeksleAsync(new[] { ders });

        // "fizik kimya biyoloji": 3 kelimeden 2'si eşleşiyor (2/3 = %66.7 >= %60) -> Tier 3 ile dönmeli
        var sonuclar = motor.Ara("fizik kimya biyoloji");
        Assert.Single(sonuclar);
        Assert.True(sonuclar[0].Puan < 50.0);
        Assert.True(sonuclar[0].Puan >= 6.0);
    }

    [Fact]
    public async Task KismiEslesmeEsigi_BesKelimeliSorguda_YuzdeAltmisVeKırkKontrolu()
    {
        var ders = OrnekDersUret();
        var transkript = new Transkript
        {
            Ad = ders.Ad,
            JsonYolu = ders.JsonYolu,
            Segmentler = new List<Segment>
            {
                new(0, 0.0, 5.0, "Elma armut muz meyveleri pazarda satılıyor")
            }
        };

        using var motor = new MetinAramaMotoru(transkriptOkuyucu: _ => transkript);
        await motor.IndeksleAsync(new[] { ders });

        // 5 kelimeden 3'ü eşleşiyor (3/5 = %60 >= %60) -> Eşleşmeli
        var sonuclar3 = motor.Ara("elma armut muz kiraz cilek");
        Assert.Single(sonuclar3);

        // 5 kelimeden 2'si eşleşiyor (2/5 = %40 < %60) -> Eşleşmemeli
        var sonuclar2 = motor.Ara("elma armut kavun karpuz erik");
        Assert.Empty(sonuclar2);
    }

    [Fact]
    public async Task Siralama_PuanaGoreBuyuktenKucugeSiralanir()
    {
        var ders = OrnekDersUret();
        var transkript = new Transkript
        {
            Ad = ders.Ad,
            JsonYolu = ders.JsonYolu,
            Segmentler = new List<Segment>
            {
                new(0, 0.0, 5.0, "A ve B ve C"),                 // Tier 3 (%66.7)
                new(1, 5.0, 10.0, "C ve B ve A"),                // Tier 2 (tüm kelimeler ters sıra)
                new(2, 10.0, 15.0, "A B C sirasiyla calisir")   // Tier 1 (tam ifade)
            }
        };

        using var motor = new MetinAramaMotoru(transkriptOkuyucu: _ => transkript);
        await motor.IndeksleAsync(new[] { ders });

        var sonuclar = motor.Ara("A B C D"); // A, B, C eşleşiyor

        // Segment 2: "A B C" tam ifade alt dize eşleşmesi -> Tier 1 (100)
        // Segment 1: "C ve B ve A" -> 3/4 (%75) -> Tier 3
        // Segment 0: "A ve B ve C" -> 3/4 (%75) -> Tier 3
        Assert.Equal(3, sonuclar.Count);
        Assert.True(sonuclar[0].Puan >= sonuclar[1].Puan);
        Assert.True(sonuclar[1].Puan >= sonuclar[2].Puan);
    }

    [Fact]
    public async Task Siralama_EsitPuanda_DersAdiSonraBasSnSirasi()
    {
        var dersB = new DersKaydi("dersB", Path.GetTempPath(), Path.Combine(Path.GetTempPath(), "b.json"), null);
        var dersA = new DersKaydi("dersA", Path.GetTempPath(), Path.Combine(Path.GetTempPath(), "a.json"), null);

        var transB = new Transkript
        {
            Ad = "dersB",
            JsonYolu = dersB.JsonYolu,
            Segmentler = new List<Segment> { new(0, 10.0, 15.0, "Ortak test ifadesi") }
        };
        var transA = new Transkript
        {
            Ad = "dersA",
            JsonYolu = dersA.JsonYolu,
            Segmentler = new List<Segment>
            {
                new(1, 20.0, 25.0, "Ortak test ifadesi"),
                new(0, 5.0, 10.0, "Ortak test ifadesi")
            }
        };

        using var motor = new MetinAramaMotoru(transkriptOkuyucu: yol => yol.Contains("a.json") ? transA : transB);
        await motor.IndeksleAsync(new[] { dersB, dersA });

        var sonuclar = motor.Ara("Ortak test ifadesi");

        Assert.Equal(3, sonuclar.Count);
        // Önce dersA (BasSn 5.0), sonra dersA (BasSn 20.0), sonra dersB (BasSn 10.0)
        Assert.Equal("dersA", sonuclar[0].DersAd);
        Assert.Equal(5.0, sonuclar[0].BasSn);

        Assert.Equal("dersA", sonuclar[1].DersAd);
        Assert.Equal(20.0, sonuclar[1].BasSn);

        Assert.Equal("dersB", sonuclar[2].DersAd);
        Assert.Equal(10.0, sonuclar[2].BasSn);
    }

    [Fact]
    public async Task VurguDogrulugu_OrijinalMetinKarakterlerini_DogruIsaretler()
    {
        var ders = OrnekDersUret();
        var transkript = new Transkript
        {
            Ad = ders.Ad,
            JsonYolu = ders.JsonYolu,
            Segmentler = new List<Segment>
            {
                new(0, 0.0, 5.0, "Tarih boyunca ISPARTA gülü meşhurdur")
            }
        };

        using var motor = new MetinAramaMotoru(transkriptOkuyucu: _ => transkript);
        await motor.IndeksleAsync(new[] { ders });

        var sonuclar = motor.Ara("ısparta");
        Assert.Single(sonuclar);
        Assert.NotEmpty(sonuclar[0].Vurgular);

        var (bas, uz) = sonuclar[0].Vurgular[0];
        string vurgulanan = sonuclar[0].Metin.Substring(bas, uz);
        Assert.Equal("ISPARTA", vurgulanan);
    }

    [Fact]
    public async Task VurguDogrulugu_ApostrofVeOzelKarakterliMetinde_DogruAralik()
    {
        var ders = OrnekDersUret();
        var transkript = new Transkript
        {
            Ad = ders.Ad,
            JsonYolu = ders.JsonYolu,
            Segmentler = new List<Segment>
            {
                new(0, 0.0, 5.0, "Ankara'da buluştuk")
            }
        };

        using var motor = new MetinAramaMotoru(transkriptOkuyucu: _ => transkript);
        await motor.IndeksleAsync(new[] { ders });

        var sonuclar = motor.Ara("ankara da");
        Assert.Single(sonuclar);
        var (bas, uz) = sonuclar[0].Vurgular[0];
        string vurgulanan = sonuclar[0].Metin.Substring(bas, uz);
        Assert.Equal("Ankara'da", vurgulanan);
    }

    [Fact]
    public async Task VurguDogrulugu_CokluBoslukluMetinde_DogruAralik()
    {
        var ders = OrnekDersUret();
        var transkript = new Transkript
        {
            Ad = ders.Ad,
            JsonYolu = ders.JsonYolu,
            Segmentler = new List<Segment>
            {
                new(0, 0.0, 5.0, "Isparta   Gülü")
            }
        };

        using var motor = new MetinAramaMotoru(transkriptOkuyucu: _ => transkript);
        await motor.IndeksleAsync(new[] { ders });

        var sonuclar = motor.Ara("isparta gulu");
        Assert.Single(sonuclar);
        var (bas, uz) = sonuclar[0].Vurgular[0];
        string vurgulanan = sonuclar[0].Metin.Substring(bas, uz);
        Assert.Equal("Isparta   Gülü", vurgulanan);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData(" , . ; : ! ? ")]
    public async Task BosVeyaBoslukSorgu_BosListeDoner(string? sorgu)
    {
        var ders = OrnekDersUret();
        var transkript = new Transkript
        {
            Ad = ders.Ad,
            JsonYolu = ders.JsonYolu,
            Segmentler = new List<Segment>
            {
                new(0, 0.0, 5.0, "Ders içeriği")
            }
        };

        using var motor = new MetinAramaMotoru(transkriptOkuyucu: _ => transkript);
        await motor.IndeksleAsync(new[] { ders });

        var sonuclar = motor.Ara(sorgu!);
        Assert.Empty(sonuclar);
    }

    [Fact]
    public async Task EnFazlaParametresi_SonucSayisiniSinirlar()
    {
        var ders = OrnekDersUret();
        var segler = new List<Segment>();
        for (int i = 0; i < 20; i++)
        {
            segler.Add(new Segment(i, i * 2.0, (i + 1) * 2.0, $"Fizik dersi ornek cumle {i}"));
        }

        var transkript = new Transkript
        {
            Ad = ders.Ad,
            JsonYolu = ders.JsonYolu,
            Segmentler = segler
        };

        using var motor = new MetinAramaMotoru(transkriptOkuyucu: _ => transkript);
        await motor.IndeksleAsync(new[] { ders });

        var sonuclar = motor.Ara("fizik", en_fazla: 5);
        Assert.Equal(5, sonuclar.Count);
    }

    [Fact]
    public async Task DuzeltmeSonrasi_AramaYeniMetniBulur_Eskiyibulmaz()
    {
        string dersKlasor = Path.Combine(_geciciKlasor, "mat101_islenmis");
        Directory.CreateDirectory(dersKlasor);

        string jsonYolu = Path.Combine(dersKlasor, "mat101.json");
        string transkriptJson = """
        {
          "sema_surumu": 2,
          "ad": "mat101",
          "segmentler": [
            { "no": 0, "bas": 0.0, "son": 5.0, "metin": "Formülü tahtaya yazıyorum" }
          ]
        }
        """;
        File.WriteAllText(jsonYolu, transkriptJson);

        var ders = new DersKaydi("mat101", dersKlasor, jsonYolu, null);
        var depo = new DuzeltmeDeposu(dersKlasor);

        using var motor = new MetinAramaMotoru(duzeltmeDeposu: depo);
        await motor.IndeksleAsync(new[] { ders });

        // İlk arama: "tahtaya" bulunur, "deftere" bulunmaz
        var ilkBulunan = motor.Ara("tahtaya");
        Assert.Single(ilkBulunan);
        Assert.Empty(motor.Ara("deftere"));

        // Düzeltme kaydet
        string duzeltmeKlasoru = Path.Combine(dersKlasor, "mat101.duzeltmeler");
        var d = new Duzeltme
        {
            Ders = "mat101",
            SegmentNo = 0,
            Eski = "Formülü tahtaya yazıyorum",
            Yeni = "Denklemi deftere yazıyorum",
            Tur = "duzelt"
        };
        depo.Yaz(duzeltmeKlasoru, d);

        // Dersi yenile
        motor.DersiYenile(ders);

        // İkinci arama: "deftere" bulunur, "tahtaya" artık bulunmaz!
        var yeniBulunan = motor.Ara("deftere");
        Assert.Single(yeniBulunan);
        Assert.Equal("Denklemi deftere yazıyorum", yeniBulunan[0].Metin);

        var eskiBulunan = motor.Ara("tahtaya");
        Assert.Empty(eskiBulunan);
    }

    [Fact]
    public async Task IptalTokeni_IndeksleAsync_IptalEdilinceIstisnaFirlatir()
    {
        var dersler = Enumerable.Range(0, 10).Select(i => OrnekDersUret($"ders{i}")).ToList();

        using var cts = new CancellationTokenSource();
        int okunanDers = 0;

        using var motor = new MetinAramaMotoru(transkriptOkuyucu: yol =>
        {
            int siradaki = Interlocked.Increment(ref okunanDers);
            if (siradaki >= 3)
            {
                cts.Cancel();
            }

            return new Transkript
            {
                Ad = Path.GetFileNameWithoutExtension(yol),
                Segmentler = new List<Segment> { new(0, 0, 1, "test") }
            };
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await motor.IndeksleAsync(dersler, iptal: cts.Token);
        });

        Assert.True(okunanDers >= 3, "İndeksleme sürerken iptal tetiklenmeli");
        Assert.True(okunanDers < 10, "İptal sonrası kalan dersler indekslenmemeli");
    }

    [Fact]
    public async Task IptalTokeni_IndeksleAsync_CagridanOnceIptalEdilmisToken_IstisnaFirlatir()
    {
        var dersler = Enumerable.Range(0, 5).Select(i => OrnekDersUret($"ders{i}")).ToList();

        using var motor = new MetinAramaMotoru(transkriptOkuyucu: yol => new Transkript
        {
            Ad = Path.GetFileNameWithoutExtension(yol),
            Segmentler = new List<Segment> { new(0, 0, 1, "test") }
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await motor.IndeksleAsync(dersler, iptal: cts.Token);
        });
    }

    [Fact]
    public async Task IndeksleAsync_TranskriptOkumaHatasi_IstisnaFirlatirVeOnbellegeYazmaz()
    {
        string onbellekYolu = Path.Combine(_geciciKlasor, "okuma_hata_onbellek.json");
        var onbellek = new IndeksOnbellek(onbellekYolu);
        var ders = OrnekDersUret("hataliDers", _geciciKlasor);

        using var motor = new MetinAramaMotoru(onbellek, transkriptOkuyucu: _ => throw new IOException("Disk okuma hatası"));

        // Artık istisna fırlatmaz, dersi atlar ve önbelleğe yazmaz
        await motor.IndeksleAsync(new[] { ders });

        Assert.Equal(1, motor.AtlananDersSayisi);
        Assert.Single(motor.AtlananDersler);
        Assert.Equal("hataliDers", motor.AtlananDersler[0].DersAd);
        Assert.False(File.Exists(onbellekYolu), "Hata durumunda önbellek dosyası yazılmamalı");
        Assert.Empty(motor.Ara("test"));
    }

    [Fact]
    public async Task DersiYenile_TranskriptOkumaHatasi_IstisnaFirlatirVeMevcutIndeksiKorur()
    {
        var ders = OrnekDersUret("yenileDers", _geciciKlasor);
        bool hataVer = false;

        using var motor = new MetinAramaMotoru(transkriptOkuyucu: yol =>
        {
            if (hataVer)
            {
                throw new IOException("Yenileme okuma hatası");
            }

            return new Transkript
            {
                Ad = ders.Ad,
                JsonYolu = ders.JsonYolu,
                Segmentler = new List<Segment> { new(0, 0, 5, "Başarılı segment içeriği") }
            };
        });

        await motor.IndeksleAsync(new[] { ders });
        var ilkArama = motor.Ara("başarılı");
        Assert.Single(ilkArama);

        hataVer = true;
        Assert.Throws<IOException>(() =>
        {
            motor.DersiYenile(ders);
        });

        // Mevcut geçerli indeks bozulmamış olmalı
        var ikinciArama = motor.Ara("başarılı");
        Assert.Single(ikinciArama);
    }

    [Fact]
    public async Task IlerlemeBildirimi_IndeksleAsync_YuzdeYuzRaporlar()
    {
        var dersler = Enumerable.Range(0, 4).Select(i => OrnekDersUret($"ders{i}")).ToList();

        using var motor = new MetinAramaMotoru(transkriptOkuyucu: yol => new Transkript
        {
            Ad = Path.GetFileNameWithoutExtension(yol),
            Segmentler = new List<Segment> { new(0, 0, 1, "test") }
        });

        var ilerlemeler = new List<int>();
        var progress = new Progress<int>(v => ilerlemeler.Add(v));

        await motor.IndeksleAsync(dersler, progress);

        // Kısa bir bekleme (Progress<int> arka planda tetiklenebilir)
        await Task.Delay(50);

        Assert.NotEmpty(ilerlemeler);
        Assert.Equal(100, ilerlemeler[^1]);
    }

    [Fact]
    public async Task Eszamanli_AraVeDersiYenile_200Yineleme_IstisnasizCalisir()
    {
        var ders = OrnekDersUret("esZamanliDers", _geciciKlasor);
        int sayac = 0;

        using var motor = new MetinAramaMotoru(transkriptOkuyucu: _ =>
        {
            int s = Interlocked.Increment(ref sayac);
            return new Transkript
            {
                Ad = ders.Ad,
                Segmentler = new List<Segment>
                {
                    new(0, 0.0, 5.0, $"Eşzamanlı test metni iterasyon {s}"),
                    new(1, 5.0, 10.0, "Sabit arama içeriği")
                }
            };
        });

        await motor.IndeksleAsync(new[] { ders });

        Exception? araHata = null;
        Exception? yenileHata = null;

        var araGorevi = Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < 200; i++)
                {
                    var sonuclar = motor.Ara("Sabit");
                    Assert.NotNull(sonuclar);
                }
            }
            catch (Exception ex)
            {
                araHata = ex;
            }
        });

        var yenileGorevi = Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < 200; i++)
                {
                    motor.DersiYenile(ders);
                }
            }
            catch (Exception ex)
            {
                yenileHata = ex;
            }
        });

        await Task.WhenAll(araGorevi, yenileGorevi);

        Assert.Null(araHata);
        Assert.Null(yenileHata);
    }

    [Fact]
    public async Task Performans_1000Ders_700Segment_KurulumVeAramaHizli()
    {
        // 700 segmentli sentetik segment listesi (paylaşımlı kopya ile hızlı kurulum)
        var segmentler = new List<Segment>(700);
        for (int s = 0; s < 700; s++)
        {
            segmentler.Add(new Segment(
                s,
                s * 5.0,
                (s + 1) * 5.0,
                $"Bu sentetik ders metni ornek aciklama cumlesidir sira no {s} ve fiziksel deney sonucudur"));
        }

        var dersler = new List<DersKaydi>(1000);
        for (int d = 0; d < 1000; d++)
        {
            dersler.Add(new DersKaydi($"ders_{d}", _geciciKlasor, Path.Combine(_geciciKlasor, $"ders_{d}.json"), null));
        }

        using var motor = new MetinAramaMotoru(transkriptOkuyucu: yol => new Transkript
        {
            Ad = Path.GetFileNameWithoutExtension(yol),
            Segmentler = segmentler
        });

        // Kurulum ölçümü (< 15 s)
        var swKurulum = Stopwatch.StartNew();
        await motor.IndeksleAsync(dersler);
        swKurulum.Stop();

        Assert.True(swKurulum.ElapsedMilliseconds < 15000, $"Kurulum süresi çok uzun: {swKurulum.ElapsedMilliseconds} ms");

        // Arama ölçümü (Release: ortalama < 300 ms, Release dışı / Debug: < 600 ms)
        // İlk arama (JIT ısınma)
        _ = motor.Ara("fiziksel deney");

        var swArama = Stopwatch.StartNew();
        const int aramaTekrari = 5;
        for (int i = 0; i < aramaTekrari; i++)
        {
            var sonuclar = motor.Ara("fiziksel deney", en_fazla: 50);
            Assert.NotEmpty(sonuclar);
        }
        swArama.Stop();

#if DEBUG
        double esikMs = 600.0;
#else
        double esikMs = 300.0;
#endif

        double ortalamaMs = swArama.Elapsed.TotalMilliseconds / aramaTekrari;
        Assert.True(ortalamaMs < esikMs, $"Ortalama arama süresi çok uzun: {ortalamaMs:F2} ms (eşik: {esikMs} ms)");
    }

    [Fact]
    public async Task IndeksleAsync_BiriBozukIkiDers_DigeriDersIndekslenirVeAtlananSayisiBir()
    {
        var bozukDers = OrnekDersUret("bozukDers", _geciciKlasor);
        var saglamDers = OrnekDersUret("saglamDers", _geciciKlasor);

        using var motor = new MetinAramaMotoru(transkriptOkuyucu: yol =>
        {
            if (yol.Contains("bozukDers", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Şema geçersiz: sema!=2");
            }

            return new Transkript
            {
                Ad = "saglamDers",
                JsonYolu = yol,
                Segmentler = new List<Segment> { new(0, 0, 5, "saglam icerik arama kelimesi") }
            };
        });

        await motor.IndeksleAsync(new[] { bozukDers, saglamDers });

        Assert.Equal(1, motor.AtlananDersSayisi);
        Assert.Single(motor.AtlananDersler);
        Assert.Equal("bozukDers", motor.AtlananDersler[0].DersAd);

        var sonuclar = motor.Ara("saglam");
        Assert.Single(sonuclar);
        Assert.Equal("saglamDers", sonuclar[0].DersAd);
    }

    [Fact]
    public async Task IndeksleAsync_AyniAdliIkiFarkliKlasorDersi_IkisiDeAranabilir()
    {
        string klasorA = Path.Combine(_geciciKlasor, "donem1", "Hafta1_islenmis");
        string klasorB = Path.Combine(_geciciKlasor, "donem2", "Hafta1_islenmis");
        Directory.CreateDirectory(klasorA);
        Directory.CreateDirectory(klasorB);

        string jsonA = Path.Combine(klasorA, "Hafta1.json");
        string jsonB = Path.Combine(klasorB, "Hafta1.json");

        var dersA = new DersKaydi("Hafta1", klasorA, jsonA, null);
        var dersB = new DersKaydi("Hafta1", klasorB, jsonB, null);

        using var motor = new MetinAramaMotoru(transkriptOkuyucu: yol =>
        {
            if (string.Equals(yol, jsonA, StringComparison.OrdinalIgnoreCase))
            {
                return new Transkript
                {
                    Ad = "Hafta1",
                    JsonYolu = jsonA,
                    Segmentler = new List<Segment> { new(0, 0, 5, "fizik mekanik konusu") }
                };
            }

            return new Transkript
            {
                Ad = "Hafta1",
                JsonYolu = jsonB,
                Segmentler = new List<Segment> { new(0, 0, 5, "kimya organik konusu") }
            };
        });

        await motor.IndeksleAsync(new[] { dersA, dersB });

        var sonuclarFizik = motor.Ara("fizik");
        var sonuclarKimya = motor.Ara("kimya");

        Assert.Single(sonuclarFizik);
        Assert.Equal(jsonA, sonuclarFizik[0].JsonYolu);

        Assert.Single(sonuclarKimya);
        Assert.Equal(jsonB, sonuclarKimya[0].JsonYolu);
    }

    [Fact]
    public async Task IndekslemeSirasinda_DersiYenile_SonuctaKaybolmaz()
    {
        var ders1 = OrnekDersUret("ders1", _geciciKlasor);
        var ders2 = OrnekDersUret("ders2", _geciciKlasor);

        var transkript1Eski = new Transkript
        {
            Ad = ders1.Ad,
            JsonYolu = ders1.JsonYolu,
            Segmentler = new List<Segment>
            {
                new(0, 0.0, 5.0, "Eski Metin Bir")
            }
        };

        var transkript1Yeni = new Transkript
        {
            Ad = ders1.Ad,
            JsonYolu = ders1.JsonYolu,
            Segmentler = new List<Segment>
            {
                new(0, 0.0, 5.0, "Yepyeni Guncel Metin Bir")
            }
        };

        var transkript2 = new Transkript
        {
            Ad = ders2.Ad,
            JsonYolu = ders2.JsonYolu,
            Segmentler = new List<Segment>
            {
                new(0, 0.0, 5.0, "Ders Iki Metni")
            }
        };

        bool ilkOkuma = true;
        MetinAramaMotoru? motorRef = null;

        Transkript Okuyucu(string yol)
        {
            if (yol.Contains("ders1", StringComparison.OrdinalIgnoreCase))
            {
                if (ilkOkuma)
                {
                    ilkOkuma = false;
                    return transkript1Eski;
                }
                return transkript1Yeni;
            }

            // ders2 okunurken (yani indeksleme arka planda devam ederken) ders1 yenileniyor
            if (yol.Contains("ders2", StringComparison.OrdinalIgnoreCase) && motorRef != null)
            {
                motorRef.DersiYenile(ders1);
            }

            return transkript2;
        }

        using var motor = new MetinAramaMotoru(transkriptOkuyucu: Okuyucu);
        motorRef = motor;

        await motor.IndeksleAsync(new[] { ders1, ders2 });

        // İndeksleme sonrasında DersiYenile ile güncellenen metin korunmalı
        var sonuclar = motor.Ara("Yepyeni Guncel Metin");
        Assert.Single(sonuclar);
        Assert.Equal("Yepyeni Guncel Metin Bir", sonuclar[0].Metin);

        // Eski metin ise ezilmiş olmalı ("Eski Metin Bir" sorgusu "Metin Bir" kısmi eşleşmesiyle güncel segmenti döndürür)
        var eskiSonuclar = motor.Ara("Eski Metin Bir");
        Assert.Contains(eskiSonuclar, s => s.Metin == "Yepyeni Guncel Metin Bir");
        Assert.DoesNotContain(eskiSonuclar, s => s.Metin == "Eski Metin Bir");
        Assert.Empty(motor.Ara("Eski"));
    }

    [Fact]
    public async Task IndeksleAsync_IptalEdildiginde_SonucYayimlanmaz()
    {
        var ders1 = OrnekDersUret("ders1", _geciciKlasor);
        var transkript1 = new Transkript
        {
            Ad = ders1.Ad,
            JsonYolu = ders1.JsonYolu,
            Segmentler = new List<Segment> { new(0, 0.0, 5.0, "Iptal Metni") }
        };

        using var cts = new CancellationTokenSource();
        using var motor = new MetinAramaMotoru(transkriptOkuyucu: _ =>
        {
            cts.Cancel();
            return transkript1;
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await motor.IndeksleAsync(new[] { ders1 }, iptal: cts.Token);
        });

        var sonuclar = motor.Ara("Iptal Metni");
        Assert.Empty(sonuclar);
    }
}
