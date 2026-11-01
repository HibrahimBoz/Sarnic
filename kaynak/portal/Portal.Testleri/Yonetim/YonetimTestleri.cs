namespace Portal.Testleri.Yonetim;

using System.Diagnostics;
using Portal.Cekirdek.Yonetim;
using Portal.Servis;
using Xunit;

public sealed class YonetimTestleri : IDisposable
{
    private readonly string _kok = Path.Combine(Path.GetTempPath(), "yon_" + Guid.NewGuid().ToString("N"));
    private const string DogruParola = "cok-gizli-parola-123";

    public YonetimTestleri() => Directory.CreateDirectory(_kok);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_kok, true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class CmdPlani : ISurecPlani
    {
        public string Komut { get; set; } = "echo merhaba";
        public int Sayac;
        public ProcessStartInfo Olustur(IsKaydi kayit)
        {
            Interlocked.Increment(ref Sayac);
            var b = new ProcessStartInfo("cmd.exe");
            b.ArgumentList.Add("/c");
            b.ArgumentList.Add(Komut);
            return b;
        }
    }

    private (IsKuyrugu Kuyruk, IsIscisi Isci, YonetimCekirdegi Cekirdek, YonetimParolasi Parola, CmdPlani Plan) Kur()
    {
        var kuyruk = new IsKuyrugu(Path.Combine(_kok, "kuyruk.db"));
        var plan = new CmdPlani();
        var isci = new IsIscisi(kuyruk, plan, Path.Combine(_kok, "gunluk"), TimeSpan.FromMilliseconds(20));
        var parola = YonetimParolasi.Olustur(DogruParola);
        var cekirdek = new YonetimCekirdegi(kuyruk, isci, parola, Path.Combine(_kok, "denetim.log"), _kok, TimeSpan.Zero);
        return (kuyruk, isci, cekirdek, parola, plan);
    }

    private static YonetimKomutu Komut(string eylem) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Zaman = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        Eylem = eylem,
        Kullanici = "deneme",
    };

    private static YonetimKomutu Imzali(YonetimKomutu k, YonetimParolasi p, string parola)
    {
        k.Imza = YonetimImza.Imzala(k, YonetimImza.AnahtarTuret(parola, Convert.FromBase64String(p.Tuz), p.Iter));
        return k;
    }

    private static YonetimKomutu IsKomutu(string yol = @"C:\arsiv\a.mp4")
    {
        var k = Komut(Eylemler.IsEkle);
        k.Tur = IsSabitleri.TurIsle;
        k.Yol = yol;
        return k;
    }

    [Fact]
    public void Temizleme_parolasiz_reddedilir_ve_kuyruk_kalir()
    {
        var o = Kur();
        o.Kuyruk.Ekle(IsSabitleri.TurIsle, @"C:\a", null, "x");
        var s = o.Cekirdek.IsleAsync(Komut(Eylemler.Temizle), "x", "ag").Result;
        Assert.False(s.Basarili);
        Assert.True(s.YetkiGerekli);
        Assert.Single(o.Kuyruk.SonIsler());
    }

    [Fact]
    public void Temizleme_yanlis_parolayla_reddedilir()
    {
        var o = Kur();
        o.Kuyruk.Ekle(IsSabitleri.TurIsle, @"C:\a", null, "x");
        var s = o.Cekirdek.IsleAsync(Imzali(Komut(Eylemler.Temizle), o.Parola, "yanlis-parola-123"), "x", "ag").Result;
        Assert.False(s.Basarili);
        Assert.Single(o.Kuyruk.SonIsler());
    }

    [Fact]
    public void Temizleme_dogru_parolayla_kayitlari_siler_calisan_kalir()
    {
        var o = Kur();
        o.Kuyruk.Ekle(IsSabitleri.TurIsle, @"C:\calisan", null, "x");
        o.Kuyruk.SiradakiniAl();
        o.Kuyruk.Ekle(IsSabitleri.TurIsle, @"C:\bekleyen", null, "x");
        var s = o.Cekirdek.IsleAsync(Imzali(Komut(Eylemler.Temizle), o.Parola, DogruParola), "x", "ag").Result;
        Assert.True(s.Basarili);
        var kalan = Assert.Single(o.Kuyruk.SonIsler());
        Assert.Equal(IsSabitleri.Calisiyor, kalan.Durum);
    }

    [Fact]
    public void Imzadan_sonra_degistirilen_komut_reddedilir()
    {
        var o = Kur();
        var k = Imzali(Komut(Eylemler.Temizle), o.Parola, DogruParola);
        k.Kullanici = "baskasi";
        Assert.False(o.Cekirdek.IsleAsync(k, "x", "ag").Result.Basarili);
    }

    [Fact]
    public void Ayni_komut_ikinci_kez_islenmez()
    {
        var o = Kur();
        var k = Imzali(Komut(Eylemler.Temizle), o.Parola, DogruParola);
        Assert.True(o.Cekirdek.IsleAsync(k, "x", "ag").Result.Basarili);
        var ikinci = o.Cekirdek.IsleAsync(k, "x", "ag").Result;
        Assert.False(ikinci.Basarili);
        Assert.Contains("daha önce", ikinci.Mesaj);
    }

    [Fact]
    public void Eski_zamanli_komut_reddedilir()
    {
        var o = Kur();
        var k = Komut(Eylemler.Temizle);
        k.Zaman -= 3600;
        Assert.False(o.Cekirdek.IsleAsync(Imzali(k, o.Parola, DogruParola), "x", "ag").Result.Basarili);
    }

    [Theory]
    [InlineData("goreli\\yol")]
    [InlineData("--zararli")]
    [InlineData("")]
    public void Gecersiz_yol_reddedilir(string yol)
    {
        var o = Kur();
        Assert.False(o.Cekirdek.IsleAsync(IsKomutu(yol), "x", "ag").Result.Basarili);
        Assert.Empty(o.Kuyruk.SonIsler());
    }

    [Fact]
    public void Unc_yolu_kabul_edilir_ve_ekleyen_tasiyici_kimligidir()
    {
        var o = Kur();
        Assert.True(o.Cekirdek.IsleAsync(IsKomutu(@"\\sunucu\Arsiv\a.mp4"), @"ETKI\ali", "ag").Result.Basarili);
        Assert.Equal(@"ETKI\ali", Assert.Single(o.Kuyruk.SonIsler()).Ekleyen);
    }

    [Fact]
    public void Kuyruk_siniri_asilinca_reddedilir()
    {
        var o = Kur();
        for (int i = 0; i < IsKuyrugu.EnCokBekleyen; i++)
        {
            Assert.NotNull(o.Kuyruk.Ekle(IsSabitleri.TurIsle, @"C:\a", null, "x"));
        }
        Assert.Null(o.Kuyruk.Ekle(IsSabitleri.TurIsle, @"C:\a", null, "x"));
    }

    [Fact]
    public void Yeniden_acilista_yarim_is_siraya_doner()
    {
        var o = Kur();
        o.Kuyruk.Ekle(IsSabitleri.TurIsle, @"C:\a", null, "x");
        o.Kuyruk.SiradakiniAl();
        var yeni = new IsKuyrugu(Path.Combine(_kok, "kuyruk.db"));
        Assert.Equal(IsSabitleri.Bekliyor, Assert.Single(yeni.SonIsler()).Durum);
    }

    [Fact]
    public async Task Isci_isi_calistirir_ve_son_satiri_yazar()
    {
        var o = Kur();
        o.Kuyruk.Ekle(IsSabitleri.TurIsle, @"C:\a", null, "x");
        await o.Isci.TekIsAsync(o.Kuyruk.SiradakiniAl()!, CancellationToken.None);
        var k = Assert.Single(o.Kuyruk.SonIsler());
        Assert.Equal(IsSabitleri.Bitti, k.Durum);
        Assert.Equal(0, k.CikisKodu);
        Assert.Contains("merhaba", k.SonSatir);
    }

    [Fact]
    public async Task Basarisiz_surec_hata_olur()
    {
        var o = Kur();
        o.Plan.Komut = "exit 7";
        o.Kuyruk.Ekle(IsSabitleri.TurIsle, @"C:\a", null, "x");
        await o.Isci.TekIsAsync(o.Kuyruk.SiradakiniAl()!, CancellationToken.None);
        var k = Assert.Single(o.Kuyruk.SonIsler());
        Assert.Equal(IsSabitleri.Hata, k.Durum);
        Assert.Equal(7, k.CikisKodu);
    }

    [Fact]
    public async Task Calisan_is_iptal_edilir_ve_sonraki_baslar()
    {
        var o = Kur();
        o.Plan.Komut = "ping -n 30 127.0.0.1";
        o.Kuyruk.Ekle(IsSabitleri.TurIsle, @"C:\a", null, "x");
        var kayit = o.Kuyruk.SiradakiniAl()!;
        Task gorev = o.Isci.TekIsAsync(kayit, CancellationToken.None);
        await Task.Delay(500);
        var k = Komut(Eylemler.Iptal);
        k.IsId = kayit.Id;
        Assert.True((await o.Cekirdek.IsleAsync(k, "x", "ag")).Basarili);
        await gorev.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(IsSabitleri.Iptal, Assert.Single(o.Kuyruk.SonIsler()).Durum);
    }

    [Fact]
    public async Task Isci_tek_seferde_tek_is_calistirir()
    {
        var o = Kur();
        string kayit = Path.Combine(_kok, "sira.txt");
        string betik = Path.Combine(_kok, "sira.bat");
        File.WriteAllText(betik, $"@echo b>>\"{kayit}\"\r\n@ping -n 2 127.0.0.1 >nul\r\n@echo s>>\"{kayit}\"\r\n");
        o.Plan.Komut = betik;
        o.Kuyruk.Ekle(IsSabitleri.TurIsle, @"C:\a", null, "x");
        o.Kuyruk.Ekle(IsSabitleri.TurIsle, @"C:\b", null, "x");
        using var iptal = new CancellationTokenSource();
        Task gorev = o.Isci.CalistirAsync(iptal.Token);
        var saat = Stopwatch.StartNew();
        while (o.Kuyruk.SonIsler().Any(i => i.Durum != IsSabitleri.Bitti) && saat.Elapsed < TimeSpan.FromSeconds(20))
        {
            await Task.Delay(50);
        }
        iptal.Cancel();
        await gorev;
        Assert.Equal(new[] { "b", "s", "b", "s" }, File.ReadAllLines(kayit).Select(s => s.Trim()));
    }

    [Theory]
    [InlineData(IsSabitleri.TurIsle, "isle")]
    [InlineData(IsSabitleri.TurBelge, "belge")]
    public void Arguman_listesi_cikis_ile_olusur(string tur, string komut)
    {
        var a = AtolyeSurecPlani.Argumanlar(new IsKaydi { Tur = tur, Yol = @"\\s\a b", Cikis = @"\\s\c" }, @"\\s\d");
        Assert.Equal(new[] { "-m", "sarnicatolye", komut, @"\\s\a b", "--cikis", @"\\s\c" }, a);
    }

    [Fact]
    public void Dizinle_ayri_depoyu_kullanir()
    {
        var a = AtolyeSurecPlani.Argumanlar(new IsKaydi { Tur = IsSabitleri.TurDizinle, Yol = @"\\s\a" }, @"\\s\d");
        Assert.Equal(new[] { "-m", "sarnicatolye", "dizinle", "--kok", @"\\s\a", "--depo", @"\\s\d", "--bir-kez" }, a);
    }

    [Fact]
    public async Task Paylasim_klasoru_uctan_uca_calisir()
    {
        var o = Kur();
        string yonetim = Path.Combine(_kok, "paylasim", "yonetim");
        var kanal = new DosyaKanali(o.Cekirdek, yonetim, TimeSpan.FromMilliseconds(50));
        using var iptal = new CancellationTokenSource();
        Task gorev = kanal.CalistirAsync(iptal.Token);

        using var istemci = new DosyaYonetimBaglantisi(yonetim, TimeSpan.FromSeconds(10));
        Assert.True((await istemci.GonderAsync(IsKomutu())).Basarili);
        var yanlis = await istemci.GonderAsync(Imzali(Komut(Eylemler.Temizle), o.Parola, "yanlis-parola-123"));
        Assert.False(yanlis.Basarili);
        Assert.True((await istemci.GonderAsync(Imzali(Komut(Eylemler.Temizle), o.Parola, DogruParola))).Basarili);

        await Task.Delay(200);
        var durum = await istemci.DurumAsync();
        Assert.NotNull(durum);
        Assert.Empty(durum!.Isler);
        Assert.Empty(Directory.GetFiles(Path.Combine(yonetim, "komut")));

        iptal.Cancel();
        await gorev;
    }

    [Fact]
    public async Task Bozuk_komut_dosyasi_servisi_durdurmaz()
    {
        var o = Kur();
        string yonetim = Path.Combine(_kok, "p2", "yonetim");
        Directory.CreateDirectory(Path.Combine(yonetim, "komut"));
        File.WriteAllText(Path.Combine(yonetim, "komut", Guid.NewGuid().ToString("N") + ".json"), "{bozuk");
        await new DosyaKanali(o.Cekirdek, yonetim).TurAsync();
        Assert.Empty(Directory.GetFiles(Path.Combine(yonetim, "komut")));
    }

    [Fact]
    public void Kisa_parola_reddedilir_ve_parola_dosyada_durmaz()
    {
        Assert.Throws<ArgumentException>(() => YonetimParolasi.Olustur("kisa"));
        string yol = Path.Combine(_kok, "p.json");
        YonetimParolasi.Olustur(DogruParola).Yaz(yol);
        Assert.DoesNotContain(DogruParola, File.ReadAllText(yol));
    }

    [Fact]
    public void Servis_bilgisi_yoksa_baglanti_yok()
    {
        Assert.Null(YonetimBaglayici.BaglanAsync(_kok).Result);
    }

    [Fact]
    public async Task Servis_erisilemezse_paylasima_gecer()
    {
        string yonetim = Path.Combine(_kok, YonetimDosyalari.KlasorAdi);
        YonetimDosyalari.AtomikYaz(Path.Combine(yonetim, YonetimDosyalari.ServisDosyasi), Path.Combine(yonetim, "gecici"),
            new ServisBilgisi { Url = "https://127.0.0.1:1/", ParmakIzi = "AA" }, uzerineYaz: true);
        using var b = await YonetimBaglayici.BaglanAsync(_kok, guvenYolu: Path.Combine(_kok, "guven.txt"));
        Assert.IsType<DosyaYonetimBaglantisi>(b);
    }

    [Fact]
    public void Servis_adresi_degisirse_guvenilmez()
    {
        string guven = Path.Combine(_kok, "g", "guven.txt");
        Assert.True(YonetimBaglayici.GuvenilirMi("https://a:1/", "AA", guven));
        Assert.True(YonetimBaglayici.GuvenilirMi("https://a:1/", "AA", guven));
        Assert.False(YonetimBaglayici.GuvenilirMi("https://a:1/", "BB", guven));
        Assert.False(YonetimBaglayici.GuvenilirMi("https://kotu:1/", "AA", guven));
    }

    [Fact]
    public async Task Guvenilmeyen_serviste_paylasima_gecer()
    {
        string yonetim = Path.Combine(_kok, YonetimDosyalari.KlasorAdi);
        YonetimDosyalari.AtomikYaz(Path.Combine(yonetim, YonetimDosyalari.ServisDosyasi), Path.Combine(yonetim, "gecici"),
            new ServisBilgisi { Url = "https://kotu:1/", ParmakIzi = "BB" }, uzerineYaz: true);
        string guven = Path.Combine(_kok, "guven.txt");
        File.WriteAllText(guven, "https://a:1/|AA");
        using var b = await YonetimBaglayici.BaglanAsync(_kok, guvenYolu: guven);
        Assert.Contains("UYARI", b!.Ad);
    }

    [Fact]
    public async Task Siradan_alinip_baslamadan_iptal_edilen_is_calismaz()
    {
        var o = Kur();
        o.Plan.Komut = "ping -n 30 127.0.0.1";
        o.Kuyruk.Ekle(IsSabitleri.TurIsle, @"C:\a", null, "x");
        using var iptal = new CancellationTokenSource();
        Task gorev = o.Isci.CalistirAsync(iptal.Token);
        var saat = Stopwatch.StartNew();
        long id = 0;
        while (id == 0 && saat.Elapsed < TimeSpan.FromSeconds(5))
        {
            id = o.Kuyruk.SonIsler().FirstOrDefault(i => i.Durum == IsSabitleri.Calisiyor)?.Id ?? 0;
        }
        Assert.True(o.Isci.CalisaniIptalEt(id));
        while (o.Kuyruk.SonIsler()[0].Durum == IsSabitleri.Calisiyor && saat.Elapsed < TimeSpan.FromSeconds(15))
        {
            await Task.Delay(20);
        }
        iptal.Cancel();
        await gorev;
        Assert.Equal(IsSabitleri.Iptal, Assert.Single(o.Kuyruk.SonIsler()).Durum);
    }

    [Fact]
    public async Task Paylasimda_komutlar_gelis_sirasiyla_islenir()
    {
        var o = Kur();
        string yonetim = Path.Combine(_kok, "p3", "yonetim");
        string komutlar = Path.Combine(yonetim, "komut");
        Directory.CreateDirectory(komutlar);
        var ilk = IsKomutu(@"C:\ilk");
        var ikinci = IsKomutu(@"C:\ikinci");
        // İkinci komutun adı alfabede önce gelir.
        ilk.Id = "f" + ilk.Id[1..];
        ikinci.Id = "0" + ikinci.Id[1..];
        string yolIlk = Path.Combine(komutlar, ilk.Id + ".json");
        string yolIkinci = Path.Combine(komutlar, ikinci.Id + ".json");
        File.WriteAllText(yolIlk, System.Text.Json.JsonSerializer.Serialize(ilk));
        File.WriteAllText(yolIkinci, System.Text.Json.JsonSerializer.Serialize(ikinci));
        File.SetCreationTimeUtc(yolIlk, DateTime.UtcNow.AddMinutes(-2));
        File.SetCreationTimeUtc(yolIkinci, DateTime.UtcNow.AddMinutes(-1));
        await new DosyaKanali(o.Cekirdek, yonetim).TurAsync();
        var isler = o.Kuyruk.SonIsler();
        Assert.Equal(@"C:\ikinci", isler[0].Yol);
        Assert.Equal(@"C:\ilk", isler[1].Yol);
    }
}
