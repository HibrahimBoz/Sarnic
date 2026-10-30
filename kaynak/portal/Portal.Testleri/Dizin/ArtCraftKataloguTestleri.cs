namespace Portal.Testleri.Dizin;

using System;
using System.IO;
using System.Linq;
using Portal.Cekirdek.Dizin;
using Xunit;

/// <summary>
/// ArtCraftKatalogu keşif, sürümleme, uzantı eşleme ve kopya hazırlama testleri.
/// </summary>
[Collection("ArtCraftKanca")]
public sealed class ArtCraftKataloguTestleri : IDisposable
{
    private readonly string _geciciKlasor;

    public ArtCraftKataloguTestleri()
    {
        _geciciKlasor = Path.Combine(Path.GetTempPath(), "artcraft_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_geciciKlasor);
        ArtCraftKatalogu.SurumOkuyucu = null;
    }

    public void Dispose()
    {
        ArtCraftKatalogu.SurumOkuyucu = null;
        try
        {
            if (Directory.Exists(_geciciKlasor))
            {
                Directory.Delete(_geciciKlasor, true);
            }
        }
        catch
        {
            // Temizlik hatalarını yut
        }
    }

    [Fact]
    public void PaketliVeKuruluUygulamalariBulur()
    {
        try
        {
            // (1) Paketli uygulama: <paket>\artcraft\wordcraft\wordcraft.exe
            string paketKlasoru = Path.Combine(_geciciKlasor, "paket");
            string wordcraftPaket = Path.Combine(paketKlasoru, "artcraft", "wordcraft", "wordcraft.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(wordcraftPaket)!);
            File.WriteAllText(wordcraftPaket, string.Empty);

            // (2) Kurulu uygulama: <aramaKoku>\GridCraft\gridcraft.exe
            string aramaKoku = Path.Combine(_geciciKlasor, "program_files");
            string gridcraftKurulu = Path.Combine(aramaKoku, "GridCraft", "gridcraft.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(gridcraftKurulu)!);
            File.WriteAllText(gridcraftKurulu, string.Empty);

            ArtCraftKatalogu.SurumOkuyucu = exe =>
            {
                if (exe.Contains("wordcraft", StringComparison.OrdinalIgnoreCase))
                    return new Version(0, 12, 0);
                if (exe.Contains("gridcraft", StringComparison.OrdinalIgnoreCase))
                    return new Version(1, 0, 0);
                return null;
            };

            var katalog = new ArtCraftKatalogu(paketKlasoru, [aramaKoku]);
            var uygulamalar = katalog.Uygulamalar;

            Assert.Equal(2, uygulamalar.Count);

            var wc = uygulamalar.FirstOrDefault(u => u.Ad == "WordCraft");
            Assert.NotNull(wc);
            Assert.True(wc.Paketli);
            Assert.Equal(new Version(0, 12, 0), wc.Surum);
            Assert.Equal(Path.GetFullPath(wordcraftPaket), wc.ExeYolu);

            var gc = uygulamalar.FirstOrDefault(u => u.Ad == "GridCraft");
            Assert.NotNull(gc);
            Assert.False(gc.Paketli);
            Assert.Equal(new Version(1, 0, 0), gc.Surum);
            Assert.Equal(Path.GetFullPath(gridcraftKurulu), gc.ExeYolu);
        }
        finally
        {
            ArtCraftKatalogu.SurumOkuyucu = null;
        }
    }

    [Fact]
    public void UzantiEsleme_NoktaliNoktasizVeBuyukKucukDuyarsiz()
    {
        try
        {
            string paketKlasoru = Path.Combine(_geciciKlasor, "paket");
            string wordcraftExe = Path.Combine(paketKlasoru, "artcraft", "wordcraft", "wordcraft.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(wordcraftExe)!);
            File.WriteAllText(wordcraftExe, string.Empty);

            ArtCraftKatalogu.SurumOkuyucu = _ => new Version(0, 12, 0);

            var katalog = new ArtCraftKatalogu(paketKlasoru, []);

            // Noktasız küçük harf
            var u1 = katalog.UzantiIcin("docx");
            Assert.Single(u1);
            Assert.Equal("WordCraft", u1[0].Ad);

            // Noktalı büyük harf
            var u2 = katalog.UzantiIcin(".DOCX");
            Assert.Single(u2);
            Assert.Equal("WordCraft", u2[0].Ad);

            // Noktalı karışık harf
            var u3 = katalog.UzantiIcin(".Txt");
            Assert.Single(u3);
            Assert.Equal("WordCraft", u3[0].Ad);

            // Desteklenmeyen uzantı
            var u4 = katalog.UzantiIcin("xyz");
            Assert.Empty(u4);

            // Boş veya geçersiz
            var u5 = katalog.UzantiIcin("");
            Assert.Empty(u5);
        }
        finally
        {
            ArtCraftKatalogu.SurumOkuyucu = null;
        }
    }

    [Fact]
    public void EnYuksekSurumSecilir()
    {
        try
        {
            string paketKlasoru = Path.Combine(_geciciKlasor, "paket");
            string wordcraftPaket = Path.Combine(paketKlasoru, "artcraft", "wordcraft", "wordcraft.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(wordcraftPaket)!);
            File.WriteAllText(wordcraftPaket, string.Empty);

            string aramaKoku = Path.Combine(_geciciKlasor, "program_files");
            string wordcraftKurulu = Path.Combine(aramaKoku, "WordCraft", "wordcraft.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(wordcraftKurulu)!);
            File.WriteAllText(wordcraftKurulu, string.Empty);

            // Paketli: 0.10.0, Kurulu: 0.12.0 -> Kurulu kazanmalı
            ArtCraftKatalogu.SurumOkuyucu = exe =>
            {
                if (exe.Equals(Path.GetFullPath(wordcraftPaket), StringComparison.OrdinalIgnoreCase))
                    return new Version(0, 10, 0);
                if (exe.Equals(Path.GetFullPath(wordcraftKurulu), StringComparison.OrdinalIgnoreCase))
                    return new Version(0, 12, 0);
                return null;
            };

            var katalog = new ArtCraftKatalogu(paketKlasoru, [aramaKoku]);
            var uygulamalar = katalog.Uygulamalar;

            Assert.Single(uygulamalar);
            var secilen = uygulamalar[0];
            Assert.Equal("WordCraft", secilen.Ad);
            Assert.Equal(new Version(0, 12, 0), secilen.Surum);
            Assert.False(secilen.Paketli);
            Assert.Equal(Path.GetFullPath(wordcraftKurulu), secilen.ExeYolu);
        }
        finally
        {
            ArtCraftKatalogu.SurumOkuyucu = null;
        }
    }

    [Fact]
    public void EsitSurumdePaketliTercihEdilir()
    {
        try
        {
            string paketKlasoru = Path.Combine(_geciciKlasor, "paket");
            string wordcraftPaket = Path.Combine(paketKlasoru, "artcraft", "wordcraft", "wordcraft.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(wordcraftPaket)!);
            File.WriteAllText(wordcraftPaket, string.Empty);

            string aramaKoku = Path.Combine(_geciciKlasor, "program_files");
            string wordcraftKurulu = Path.Combine(aramaKoku, "WordCraft", "wordcraft.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(wordcraftKurulu)!);
            File.WriteAllText(wordcraftKurulu, string.Empty);

            // İkisi de 0.12.0 -> Paketli olan tercih edilmeli
            ArtCraftKatalogu.SurumOkuyucu = _ => new Version(0, 12, 0);

            var katalog = new ArtCraftKatalogu(paketKlasoru, [aramaKoku]);
            var uygulamalar = katalog.Uygulamalar;

            Assert.Single(uygulamalar);
            var secilen = uygulamalar[0];
            Assert.Equal("WordCraft", secilen.Ad);
            Assert.Equal(new Version(0, 12, 0), secilen.Surum);
            Assert.True(secilen.Paketli);
            Assert.Equal(Path.GetFullPath(wordcraftPaket), secilen.ExeYolu);
        }
        finally
        {
            ArtCraftKatalogu.SurumOkuyucu = null;
        }
    }

    [Fact]
    public void DuzenlenebilirKopyaHazirla_CakismaEki()
    {
        string kaynak = Path.Combine(_geciciKlasor, "duzenlenebilir.docx");
        File.WriteAllText(kaynak, "örnek içerik");

        string hedefKlasor = Path.Combine(_geciciKlasor, "hedef_belgeler");

        // 1. çağrı -> sozlesme.docx
        string yol1 = ArtCraftKatalogu.DuzenlenebilirKopyaHazirla(kaynak, "sozlesme.doc", hedefKlasor);
        Assert.Equal(Path.Combine(hedefKlasor, "sozlesme.docx"), yol1);
        Assert.True(File.Exists(yol1));

        // 2. çağrı -> sozlesme (2).docx
        string yol2 = ArtCraftKatalogu.DuzenlenebilirKopyaHazirla(kaynak, "sozlesme.doc", hedefKlasor);
        Assert.Equal(Path.Combine(hedefKlasor, "sozlesme (2).docx"), yol2);
        Assert.True(File.Exists(yol2));

        // 3. çağrı -> sozlesme (3).docx
        string yol3 = ArtCraftKatalogu.DuzenlenebilirKopyaHazirla(kaynak, "sozlesme.doc", hedefKlasor);
        Assert.Equal(Path.Combine(hedefKlasor, "sozlesme (3).docx"), yol3);
        Assert.True(File.Exists(yol3));
    }

    [Fact]
    public void BelgeYollari_Duzenlenebilir()
    {
        string depo = Path.Combine(_geciciKlasor, "depo");
        string sha = "abcdef1234567890";
        string belgeKlasoru = Path.Combine(depo, "belgeler", sha[..2], sha);
        Directory.CreateDirectory(belgeKlasoru);

        // Henüz dosya yokken null dönmeli
        Assert.Null(BelgeYollari.Duzenlenebilir(depo, sha));
        Assert.Null(BelgeYollari.Duzenlenebilir(depo, null));
        Assert.Null(BelgeYollari.Duzenlenebilir(depo, ""));

        // duzenlenebilir.docx oluşturulunca yolu dönmeli
        string kopyaYolu = Path.Combine(belgeKlasoru, "duzenlenebilir.docx");
        File.WriteAllText(kopyaYolu, "kopya içerik");

        string? bulunan = BelgeYollari.Duzenlenebilir(depo, sha);
        Assert.NotNull(bulunan);
        Assert.Equal(kopyaYolu, bulunan);
    }

    [Fact]
    public void AcmaBilgisi_DogruProcessStartInfoDondurur()
    {
        var app = new ArtCraftUygulamasi("WordCraft", @"C:\artcraft\wordcraft.exe", new Version(0, 12, 0), ["docx"], true);
        string dosya = @"C:\belgelerim\rapor.docx";

        var psi = ArtCraftKatalogu.AcmaBilgisi(app, dosya);

        Assert.Equal(@"C:\artcraft\wordcraft.exe", psi.FileName);
        Assert.False(psi.UseShellExecute);
        Assert.Equal(@"C:\artcraft", psi.WorkingDirectory);
        Assert.Single(psi.ArgumentList);
        Assert.Equal(dosya, psi.ArgumentList[0]);
    }

    [Fact]
    public void DizinAyari_VarsayilanVeriDedektifiVeEskiBulguGecisi()
    {
        string testKok = Path.Combine(_geciciKlasor, "kok");
        Directory.CreateDirectory(testKok);

        // Durum 1: İki klasör de yokken -> <kok>\_veridedektifi
        string yol1 = DizinAyari.Coz(testKok);
        Assert.Equal(Path.Combine(testKok, "_veridedektifi"), yol1);

        // Durum 2: Yalnızca eski _bulgu varken -> <kok>\_bulgu
        string eskiBulgu = Path.Combine(testKok, "_bulgu");
        Directory.CreateDirectory(eskiBulgu);
        string yol2 = DizinAyari.Coz(testKok);
        Assert.Equal(eskiBulgu, yol2);

        // Durum 3: Yeni _veridedektifi de varken -> <kok>\_veridedektifi öncelikli
        string yeniVeriDedektifi = Path.Combine(testKok, "_veridedektifi");
        Directory.CreateDirectory(yeniVeriDedektifi);
        string yol3 = DizinAyari.Coz(testKok);
        Assert.Equal(yeniVeriDedektifi, yol3);
    }

    [Theory]
    [InlineData("0.12.0", 0, 12, 0, 0)]
    [InlineData("v0.12.0", 0, 12, 0, 0)]
    [InlineData("0.12.0+abc", 0, 12, 0, 0)]
    [InlineData("0.12.0-beta", 0, 12, 0, 0)]
    [InlineData("1.2", 1, 2, 0, 0)]
    [InlineData("", 0, 0, 0, 0)]
    [InlineData("bozuk dize", 0, 0, 0, 0)]
    public void SurumAyristir_CesitliGirdileriDortBileseneNormalizeEder(string? girdi, int major, int minor, int build, int revision)
    {
        var sonuc = ArtCraftKatalogu.SurumAyristir(girdi);
        Assert.Equal(new Version(major, minor, build, revision), sonuc);
    }

    [Fact]
    public void SurumAyristir_UcIleDortBilesenliKarsilastirma()
    {
        var v3 = ArtCraftKatalogu.SurumAyristir("0.12.0");
        var v4 = ArtCraftKatalogu.SurumAyristir("0.12.0.0");

        Assert.Equal(v3, v4);
        Assert.Equal(0, v3.CompareTo(v4));

        var vYuksek = ArtCraftKatalogu.SurumAyristir("0.12.1");
        var vDusuk = ArtCraftKatalogu.SurumAyristir("0.12.0.9");
        Assert.True(vYuksek > vDusuk);
        Assert.True(vYuksek.CompareTo(vDusuk) > 0);
    }

    [Fact]
    public void DuzenlenebilirKopyaHazirla_GecersizKarakterliAd()
    {
        string kaynak = Path.Combine(_geciciKlasor, "kaynak.docx");
        File.WriteAllText(kaynak, "örnek");

        string hedefKlasor = Path.Combine(_geciciKlasor, "gecersiz_ad_hedef");
        string gecersizAd = "sozlesme:2024*ozet?.doc";
        string sonucYol = ArtCraftKatalogu.DuzenlenebilirKopyaHazirla(kaynak, gecersizAd, hedefKlasor);

        Assert.True(File.Exists(sonucYol));
        string dosyaAdi = Path.GetFileName(sonucYol);
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            Assert.DoesNotContain(c, dosyaAdi);
        }
        Assert.Equal("sozlesme_2024_ozet_.docx", dosyaAdi);
    }

    [Fact]
    public void Tara_IceIceKlasorlerdeIgnoreInaccessibleDavranisi()
    {
        // paket\artcraft\wordcraft\alt1\alt2\wordcraft.exe
        string paketKlasoru = Path.Combine(_geciciKlasor, "paket_derin");
        string derinKlasor = Path.Combine(paketKlasoru, "artcraft", "wordcraft", "alt1", "alt2");
        Directory.CreateDirectory(derinKlasor);
        string wordcraftDerin = Path.Combine(derinKlasor, "wordcraft.exe");
        File.WriteAllText(wordcraftDerin, string.Empty);

        ArtCraftKatalogu.SurumOkuyucu = _ => new Version(0, 15, 0);

        try
        {
            var katalog = new ArtCraftKatalogu(paketKlasoru, []);
            var uygulamalar = katalog.Uygulamalar;

            var wc = uygulamalar.FirstOrDefault(u => u.Ad == "WordCraft");
            Assert.NotNull(wc);
            Assert.True(wc.Paketli);
            Assert.Equal(Path.GetFullPath(wordcraftDerin), wc.ExeYolu);
            Assert.Equal(new Version(0, 15, 0), wc.Surum);
        }
        finally
        {
            ArtCraftKatalogu.SurumOkuyucu = null;
        }
    }
}
