namespace Portal.Testleri.Dizin;

using Portal.Cekirdek.Dizin;
using Xunit;

/// <summary>
/// DosyaSorgusu ayrıştırıcı sözdizimi kurallarını doğrulayan testler.
/// </summary>
public class DosyaSorgusuTestleri
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t  ")]
    public void Ayristir_BosVeNullGirdi_BosSorguDondurur(string? metin)
    {
        var sorgu = DosyaSorgusu.Ayristir(metin);

        Assert.True(sorgu.Bos);
        Assert.Empty(sorgu.Kosullar);
        Assert.Empty(sorgu.Uzantilar);
        Assert.Empty(sorgu.Turler);
        Assert.Null(sorgu.Boyut);
        Assert.Null(sorgu.Tarih);
        Assert.Null(sorgu.Icerik);
        Assert.Null(sorgu.DersBagli);
        Assert.Equal(string.Empty, sorgu.DuzMetin);
    }

    [Fact]
    public void Ayristir_VeSemantigi_CokluKosulUretir()
    {
        var sorgu = DosyaSorgusu.Ayristir("rapor plan 2026");

        Assert.False(sorgu.Bos);
        Assert.Equal(3, sorgu.Kosullar.Count);
        Assert.Single(sorgu.Kosullar[0]);
        Assert.Single(sorgu.Kosullar[1]);
        Assert.Single(sorgu.Kosullar[2]);

        Assert.Equal("rapor", sorgu.Kosullar[0][0].Metin);
        Assert.Equal("plan", sorgu.Kosullar[1][0].Metin);
        Assert.Equal("2026", sorgu.Kosullar[2][0].Metin);

        Assert.All(sorgu.Kosullar, k => Assert.False(k[0].Degil));
        Assert.All(sorgu.Kosullar, k => Assert.False(k[0].Joker));
        Assert.All(sorgu.Kosullar, k => Assert.Equal(TerimAlani.Ad, k[0].Alan));
    }

    [Fact]
    public void Ayristir_VeyaSemantigi_AyniKosulIcerisindeTerimlerUretir()
    {
        var sorgu = DosyaSorgusu.Ayristir("rapor|plan|ozet");

        Assert.Single(sorgu.Kosullar);
        Assert.Equal(3, sorgu.Kosullar[0].Count);

        Assert.Equal("rapor", sorgu.Kosullar[0][0].Metin);
        Assert.Equal("plan", sorgu.Kosullar[0][1].Metin);
        Assert.Equal("ozet", sorgu.Kosullar[0][2].Metin);
    }

    [Fact]
    public void Ayristir_VeVeVeyaBirlikte_KombinasyonuDogruAyristirir()
    {
        // "a b|c" -> [[a], [b, c]]
        var sorgu = DosyaSorgusu.Ayristir("a b|c");

        Assert.Equal(2, sorgu.Kosullar.Count);
        Assert.Single(sorgu.Kosullar[0]);
        Assert.Equal(2, sorgu.Kosullar[1].Count);

        Assert.Equal("a", sorgu.Kosullar[0][0].Metin);
        Assert.Equal("b", sorgu.Kosullar[1][0].Metin);
        Assert.Equal("c", sorgu.Kosullar[1][1].Metin);
    }

    [Fact]
    public void Ayristir_DegilSemantigi_DegilOzelliginiDogruIsaretler()
    {
        var sorgu = DosyaSorgusu.Ayristir("rapor !taslak");

        Assert.Equal(2, sorgu.Kosullar.Count);
        Assert.False(sorgu.Kosullar[0][0].Degil);
        Assert.True(sorgu.Kosullar[1][0].Degil);
        Assert.Equal("taslak", sorgu.Kosullar[1][0].Metin);
    }

    [Fact]
    public void Ayristir_DuzMetin_YalnizOlumluAdTerimlerininOzgunYaziminiDondurur()
    {
        var sorgu = DosyaSorgusu.Ayristir("Rapor Plan !eski yol:gizli uz:pdf");

        Assert.Equal("Rapor Plan", sorgu.DuzMetin);
    }

    [Fact]
    public void Ayristir_TirnakliIfade_BosluguTekTerimOlarakKorur()
    {
        var sorgu = DosyaSorgusu.Ayristir("\"yillik faaliyet raporu\"");

        Assert.Single(sorgu.Kosullar);
        Assert.Single(sorgu.Kosullar[0]);
        Assert.Equal("yillik faaliyet raporu", sorgu.Kosullar[0][0].Metin);
        Assert.False(sorgu.Kosullar[0][0].Joker);
    }

    [Fact]
    public void Ayristir_OnekliTirnak_YolTeriminiDogruKurar()
    {
        var sorgu = DosyaSorgusu.Ayristir("yol:\"proje dosyalari\" path:\"arsiv/eski\"");

        Assert.Equal(2, sorgu.Kosullar.Count);

        Assert.Equal(TerimAlani.Yol, sorgu.Kosullar[0][0].Alan);
        Assert.Equal("proje dosyalari", sorgu.Kosullar[0][0].Metin);

        Assert.Equal(TerimAlani.Yol, sorgu.Kosullar[1][0].Alan);
        Assert.Equal("arsiv/eski", sorgu.Kosullar[1][0].Metin);
    }

    [Fact]
    public void Ayristir_JokerKarakterler_JokerOzelliginiIsaretler()
    {
        var sorgu = DosyaSorgusu.Ayristir("*.pdf rap?r belge");

        Assert.Equal(3, sorgu.Kosullar.Count);
        Assert.True(sorgu.Kosullar[0][0].Joker);
        Assert.Equal("*.pdf", sorgu.Kosullar[0][0].Metin);

        Assert.True(sorgu.Kosullar[1][0].Joker);
        Assert.Equal("rap?r", sorgu.Kosullar[1][0].Metin);

        Assert.False(sorgu.Kosullar[2][0].Joker);
        Assert.Equal("belge", sorgu.Kosullar[2][0].Metin);
    }

    [Fact]
    public void Ayristir_Uzantilar_CokluVeExtOnekiniDestekler()
    {
        var sorgu = DosyaSorgusu.Ayristir("uz:pdf;docx ext:XLSX");

        Assert.Equal(3, sorgu.Uzantilar.Count);
        Assert.Contains("pdf", sorgu.Uzantilar);
        Assert.Contains("docx", sorgu.Uzantilar);
        Assert.Contains("xlsx", sorgu.Uzantilar);
    }

    [Fact]
    public void Ayristir_Turler_CokluVeTypeOnekiniDestekler()
    {
        var sorgu = DosyaSorgusu.Ayristir("tur:belge|video type:goruntu");

        Assert.Equal(3, sorgu.Turler.Count);
        Assert.Contains("belge", sorgu.Turler);
        Assert.Contains("video", sorgu.Turler);
        Assert.Contains("goruntu", sorgu.Turler);
    }

    [Theory]
    [InlineData("boyut:1024", 1024L, 1024L)]
    [InlineData("boyut:100b", 100L, 100L)]
    [InlineData("boyut:>10kb", 10241L, null)]
    [InlineData("boyut:>=10kb", 10240L, null)]
    [InlineData("boyut:<500kb", null, 511999L)]
    [InlineData("boyut:<=500kb", null, 512000L)]
    [InlineData("size:>1gb", 1073741825L, null)]
    [InlineData("boyut:1mb..5mb", 1048576L, 5242880L)]
    [InlineData("boyut:1.5mb", 1572864L, 1572864L)]
    [InlineData("size:>1.5mb", 1572865L, null)]
    public void Ayristir_Boyut_TumIsleclerVeBirimleriDogruCozumler(string girdi, long? beklenenEnAz, long? beklenenEnCok)
    {
        var sorgu = DosyaSorgusu.Ayristir(girdi);

        Assert.NotNull(sorgu.Boyut);
        Assert.Equal(beklenenEnAz, sorgu.Boyut.EnAz);
        Assert.Equal(beklenenEnCok, sorgu.Boyut.EnCok);
    }

    [Fact]
    public void Ayristir_Tarih_YilDonemi()
    {
        var sorgu = DosyaSorgusu.Ayristir("tarih:2026");

        Assert.NotNull(sorgu.Tarih);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0), sorgu.Tarih.Bas);
        Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0), sorgu.Tarih.Son);
    }

    [Fact]
    public void Ayristir_Tarih_YilAyDonemi()
    {
        var sorgu = DosyaSorgusu.Ayristir("tarih:2026-10");

        Assert.NotNull(sorgu.Tarih);
        Assert.Equal(new DateTime(2026, 10, 1, 0, 0, 0), sorgu.Tarih.Bas);
        Assert.Equal(new DateTime(2026, 11, 1, 0, 0, 0), sorgu.Tarih.Son);
    }

    [Fact]
    public void Ayristir_Tarih_TamGunDonemi()
    {
        var sorgu = DosyaSorgusu.Ayristir("tarih:2026-10-15");

        Assert.NotNull(sorgu.Tarih);
        Assert.Equal(new DateTime(2026, 10, 15, 0, 0, 0), sorgu.Tarih.Bas);
        Assert.Equal(new DateTime(2026, 10, 16, 0, 0, 0), sorgu.Tarih.Son);
    }

    [Fact]
    public void Ayristir_Tarih_IsleclerVeDmOneki()
    {
        var buyuktur = DosyaSorgusu.Ayristir("tarih:>2026-01-01");
        Assert.Equal(new DateTime(2026, 1, 2, 0, 0, 0), buyuktur.Tarih!.Bas);
        Assert.Null(buyuktur.Tarih.Son);

        var buyukEsit = DosyaSorgusu.Ayristir("tarih:>=2026-01-01");
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0), buyukEsit.Tarih!.Bas);
        Assert.Null(buyukEsit.Tarih.Son);

        var kucuktur = DosyaSorgusu.Ayristir("tarih:<2026-01-01");
        Assert.Null(kucuktur.Tarih!.Bas);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0), kucuktur.Tarih.Son);

        var kucukEsit = DosyaSorgusu.Ayristir("tarih:<=2026-01-01");
        Assert.Null(kucukEsit.Tarih!.Bas);
        Assert.Equal(new DateTime(2026, 1, 2, 0, 0, 0), kucukEsit.Tarih.Son);

        var aralik = DosyaSorgusu.Ayristir("dm:2026-01-01..2026-03-31");
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0), aralik.Tarih!.Bas);
        Assert.Equal(new DateTime(2026, 4, 1, 0, 0, 0), aralik.Tarih.Son);
    }

    [Fact]
    public void Ayristir_Icerik_ContentOnekiVeBirlestirme()
    {
        var tekil = DosyaSorgusu.Ayristir("icerik:sozlesme");
        Assert.Equal("sozlesme", tekil.Icerik);

        var coklu = DosyaSorgusu.Ayristir("icerik:bir content:iki icerik:\"uc dort\"");
        Assert.Equal("bir iki uc dort", coklu.Icerik);
    }

    [Fact]
    public void Ayristir_DersBagli_DersVeDegilDers()
    {
        var dersVar = DosyaSorgusu.Ayristir("ders:");
        Assert.True(dersVar.DersBagli);

        var dersYok = DosyaSorgusu.Ayristir("!ders:");
        Assert.False(dersYok.DersBagli);

        var belirtilmemis = DosyaSorgusu.Ayristir("dosya.txt");
        Assert.Null(belirtilmemis.DersBagli);
    }

    [Fact]
    public void Ayristir_GecersizSuzgecVeBilinmeyenOnek_DuzAdTerimiKalir()
    {
        var sorgu = DosyaSorgusu.Ayristir("boyut:abc tarih:gecersiz bilinmeyen:deger");

        Assert.Equal(3, sorgu.Kosullar.Count);
        Assert.Null(sorgu.Boyut);
        Assert.Null(sorgu.Tarih);

        Assert.Equal("boyut:abc", sorgu.Kosullar[0][0].Metin);
        Assert.Equal(TerimAlani.Ad, sorgu.Kosullar[0][0].Alan);

        Assert.Equal("tarih:gecersiz", sorgu.Kosullar[1][0].Metin);
        Assert.Equal(TerimAlani.Ad, sorgu.Kosullar[1][0].Alan);

        Assert.Equal("bilinmeyen:deger", sorgu.Kosullar[2][0].Metin);
        Assert.Equal(TerimAlani.Ad, sorgu.Kosullar[2][0].Alan);
    }
}
