namespace Portal.Testleri;

using Portal.Cekirdek;
using Portal.Cekirdek.Dizin;
using Portal.Testleri.Dizin;
using Xunit;

/// <summary>
/// Türkçe dışı içeriğin çevirisi: döküm JSON'u, belge dizini ve video araması.
/// </summary>
public class CeviriTestleri : IDisposable
{
    private readonly string _geciciKlasor;

    public CeviriTestleri()
    {
        _geciciKlasor = Path.Combine(Path.GetTempPath(), "VH_CeviriTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_geciciKlasor);
    }

    public void Dispose()
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

    private string DokumYaz(string ad, string segmentlerJson, string kokEk = "")
    {
        string klasor = Path.Combine(_geciciKlasor, $"{ad}_islenmis");
        Directory.CreateDirectory(klasor);
        string jsonYolu = Path.Combine(klasor, $"{ad}.json");
        File.WriteAllText(jsonYolu, $$"""
        {
          "sema_surumu": 2,
          {{kokEk}}
          "segmentler": [ {{segmentlerJson}} ]
        }
        """);
        return jsonYolu;
    }

    [Fact]
    public void Dokum_CeviriAlani_OkunurVeCeviriVar()
    {
        string yol = DokumYaz("ders_en",
            """{ "no": 0, "bas": 0.0, "son": 3.0, "metin": "The meeting starts at nine.", "ceviri": "Toplantı dokuzda başlar." }""",
            "\"ceviri_dil\": \"tr\", \"ceviri_model\": \"nllb-200-distilled-1.3B-ct2-int8\",");

        var t = TranskriptOkuyucu.Oku(yol);

        Assert.True(t.CeviriVar);
        Assert.Equal("Toplantı dokuzda başlar.", t.Segmentler[0].Ceviri);
        Assert.Equal("tr", t.CeviriDil);
        Assert.Equal("nllb-200-distilled-1.3B-ct2-int8", t.CeviriModel);
    }

    [Fact]
    public void Dokum_CeviriAlaniYok_CeviriVarYanlis()
    {
        string yol = DokumYaz("ders_tr", """{ "no": 0, "bas": 0.0, "son": 3.0, "metin": "Toplantı dokuzda başlar." }""");

        var t = TranskriptOkuyucu.Oku(yol);

        Assert.False(t.CeviriVar);
        Assert.Null(t.Segmentler[0].Ceviri);
    }

    [Fact]
    public void Dokum_CeviriMetinDegil_DokumYineOkunur()
    {
        string yol = DokumYaz("ders_bozuk", """{ "no": 0, "bas": 0.0, "son": 3.0, "metin": "Hello.", "ceviri": 42 }""");

        var t = TranskriptOkuyucu.Oku(yol);

        Assert.Single(t.Segmentler);
        Assert.Null(t.Segmentler[0].Ceviri);
    }

    [Fact]
    public void Duzeltme_Birlestir_CeviriyiKorur()
    {
        string yol = DokumYaz("ders_duz",
            """{ "no": 0, "bas": 0.0, "son": 3.0, "metin": "Hello.", "ceviri": "Merhaba." }""");
        var depo = new DuzeltmeDeposu(Path.Combine(_geciciKlasor, "duzeltmeler"));

        var sonuc = depo.Birlestir(TranskriptOkuyucu.Oku(yol));

        Assert.Equal("Merhaba.", sonuc.Segmentler[0].Ceviri);
    }

    [Fact]
    public async Task VideoAramasi_YalnizCeviridekiSozcuk_CevirideEslestiDoner()
    {
        string yol = DokumYaz("ders_ru",
            """{ "no": 0, "bas": 0.0, "son": 3.0, "metin": "Встреча начнётся завтра.", "ceviri": "Toplantı yarın başlayacak." }""");
        var ders = new DersKaydi("ders_ru", Path.GetDirectoryName(yol)!, yol, null);
        var onbellek = new IndeksOnbellek(Path.Combine(_geciciKlasor, "indeks.json"));

        using var motor = new MetinAramaMotoru(onbellek);
        await motor.IndeksleAsync(new[] { ders });

        var bulunan = motor.Ara("toplantı");
        Assert.Single(bulunan);
        Assert.True(bulunan[0].CevirideEslesti);
        Assert.Equal("Toplantı yarın başlayacak.", bulunan[0].Metin);

        var ozgun = motor.Ara("встреча");
        Assert.Single(ozgun);
        Assert.False(ozgun[0].CevirideEslesti);
    }

    [Fact]
    public async Task VideoAramasi_OzgunKismiCeviriTam_CeviriKazanir()
    {
        // İnceleme bulgusu: özgün metin 3 sözcükten 2'sinde (ortak sayı/özel ad) kısmi eşleşince çeviriye bakılmıyordu.
        string yol = DokumYaz("ders_kismi",
            """{ "no": 0, "bas": 0.0, "son": 3.0, "metin": "NATO 2026 учения начнутся.", "ceviri": "NATO 2026 tatbikatı başlayacak." }""");
        var ders = new DersKaydi("ders_kismi", Path.GetDirectoryName(yol)!, yol, null);

        using var motor = new MetinAramaMotoru(new IndeksOnbellek(Path.Combine(_geciciKlasor, "indeks2.json")));
        await motor.IndeksleAsync(new[] { ders });

        var bulunan = motor.Ara("nato 2026 tatbikat");
        Assert.Single(bulunan);
        Assert.True(bulunan[0].CevirideEslesti);
    }

    [Fact]
    public void KullaniciAyari_KokVeCeviriGoster_BirbiriniSilmez()
    {
        string yol = Path.Combine(_geciciKlasor, "kullanici.json");

        KokYapilandirma.CeviriGosterKaydet(true, yol);
        KokYapilandirma.Kaydet(_geciciKlasor, yol);

        Assert.True(KokYapilandirma.CeviriGosterOku(yol));
        Assert.Contains("\"kok\"", File.ReadAllText(yol));

        KokYapilandirma.CeviriGosterKaydet(false, yol);
        Assert.False(KokYapilandirma.CeviriGosterOku(yol));
        Assert.Contains("\"kok\"", File.ReadAllText(yol));
    }

    [Fact]
    public void IcerikAramasi_YalnizCeviride_CevirideEslestiVeAlintiCeviriden()
    {
        using var testDizini = new TestDizini();
        long dosyaId = testDizini.DosyaEkle(@"C:\Belgeler\rapor.pdf", "rapor.pdf");
        long sayfaId = testDizini.SayfaEkle(dosyaId, 1, "Sayfa 1", "Встреча назначена на завтра.");
        testDizini.SayfaCevirisiEkle(sayfaId, "ru", "Toplantı yarına planlandı.");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var sonuclar = new IcerikAramaMotoru(kaynak).Ara("toplantı");

        Assert.Single(sonuclar);
        Assert.True(sonuclar[0].CevirideEslesti);
        Assert.Contains("Toplantı", sonuclar[0].Alinti);
    }

    [Fact]
    public void IcerikAramasi_OzgundeVeCeviride_OzgunEslesmeKazanir()
    {
        using var testDizini = new TestDizini();
        long dosyaId = testDizini.DosyaEkle(@"C:\Belgeler\memo.pdf", "memo.pdf");
        long sayfaId = testDizini.SayfaEkle(dosyaId, 1, "Sayfa 1", "Project budget review.");
        testDizini.SayfaCevirisiEkle(sayfaId, "en", "Project bütçe incelemesi.");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var sonuclar = new IcerikAramaMotoru(kaynak).Ara("project");

        Assert.Single(sonuclar);
        Assert.False(sonuclar[0].CevirideEslesti);
    }

    [Fact]
    public void DosyaAramasi_IcerikSuzgeci_CeviriyiKapsar()
    {
        using var testDizini = new TestDizini();
        long dosyaId = testDizini.DosyaEkle(@"C:\Belgeler\rapor.pdf", "rapor.pdf");
        long sayfaId = testDizini.SayfaEkle(dosyaId, 1, "Sayfa 1", "Встреча назначена на завтра.");
        testDizini.SayfaCevirisiEkle(sayfaId, "ru", "Toplantı yarına planlandı.");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var sonuclar = new DosyaAramaMotoru(kaynak).Ara(DosyaSorgusu.Ayristir("icerik:toplantı"));

        Assert.Single(sonuclar);
        Assert.Equal(dosyaId, sonuclar[0].Id);
    }

    [Fact]
    public void EskiDizin_CeviriSutunuYok_AramaVeSayfaOkumaHataVermez()
    {
        using var testDizini = new TestDizini();
        long dosyaId = testDizini.DosyaEkle(@"C:\Belgeler\eski.pdf", "eski.pdf");
        testDizini.SayfaEkle(dosyaId, 1, "Sayfa 1", "Toplantı tutanağı.");
        testDizini.EskiSemayaIndir();

        using var kaynak = testDizini.DosyadanKaynakAc();
        var sonuclar = new IcerikAramaMotoru(kaynak).Ara("toplantı");
        var sayfa = new SayfaDeposu(kaynak).Getir(dosyaId, 1);

        Assert.Single(sonuclar);
        Assert.False(sonuclar[0].CevirideEslesti);
        Assert.NotNull(sayfa);
        Assert.Null(sayfa!.Dil);
        Assert.Null(sayfa.Ceviri);
    }

    [Fact]
    public void SayfaDeposu_YeniSema_DilVeCeviriOkunur()
    {
        using var testDizini = new TestDizini();
        long dosyaId = testDizini.DosyaEkle(@"C:\Belgeler\rapor.pdf", "rapor.pdf");
        long sayfaId = testDizini.SayfaEkle(dosyaId, 1, "Sayfa 1", "Встреча назначена на завтра.");
        testDizini.SayfaCevirisiEkle(sayfaId, "ru", "Toplantı yarına planlandı.");

        using var kaynak = testDizini.DosyadanKaynakAc();
        var sayfalar = new SayfaDeposu(kaynak).Getir(dosyaId);

        Assert.Equal("ru", sayfalar[0].Dil);
        Assert.Equal("Toplantı yarına planlandı.", sayfalar[0].Ceviri);
    }
}
