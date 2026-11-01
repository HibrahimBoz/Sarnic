// Atölye servisi yük testi. Paket içine girmez.
// Yük:  Portal.YukTesti --depo <depo> --kullanici 10,50,100 [--sure 60] [--soru]
// Sahte model: Portal.YukTesti -m x --port P --gecikme 300   (llama-server yerine geçer; servis --llama ile başlatır)
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Portal.Cekirdek.Yonetim;

string? Arg(string ad)
{
    int i = Array.IndexOf(args, ad);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

if (Arg("-m") != null)
{
    return await SahteModelAsync(int.Parse(Arg("--port")!), int.Parse(Environment.GetEnvironmentVariable("SAHTE_GECIKME_MS") ?? "300"));
}

string depo = Arg("--depo") ?? throw new ArgumentException("--depo gerekli");
if (Arg("--sor") is string tekSoru)
{
    return await TekSoruAsync(tekSoru);
}
int sure = int.Parse(Arg("--sure") ?? "60");
bool soru = args.Contains("--soru");
string guven = Path.Combine(Path.GetTempPath(), "yuk_guven_" + Guid.NewGuid().ToString("N") + ".txt");
string yonetim = Path.Combine(depo, YonetimDosyalari.KlasorAdi);

foreach (int n in (Arg("--kullanici") ?? "10,50,100").Split(',').Select(int.Parse))
{
    await TurAsync(n);
}
File.Delete(guven);
return 0;

async Task<int> TekSoruAsync(string metin)
{
    string yol = Path.Combine(Path.GetTempPath(), "yuk_tek_guven.txt");
    File.Delete(yol);
    using var b = await YonetimBaglayici.BaglanAsync(depo, default, yol) ?? throw new IOException("Servis bulunamadı");
    long Z() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var sw = Stopwatch.StartNew();
    KomutSonucu s = await b.GonderAsync(new YonetimKomutu { Id = Guid.NewGuid().ToString("N"), Zaman = Z(), Eylem = Eylemler.SoruSor, Kullanici = "dene", Soru = metin });
    if (s.SoruId is not long id)
    {
        Console.WriteLine("RED: " + s.Mesaj);
        return 1;
    }
    while (true)
    {
        KomutSonucu l = await b.GonderAsync(new YonetimKomutu { Eylem = Eylemler.SoruListele, Kullanici = "dene" });
        SoruKaydi k = l.Sorular!.First(x => x.Id == id);
        if (k.Durum is not (IsSabitleri.Bekliyor or IsSabitleri.Calisiyor))
        {
            Console.WriteLine($"SORU: {metin}\nDURUM: {k.Durum}, süre {sw.Elapsed.TotalSeconds:F1} sn\nCEVAP: {k.Cevap}");
            foreach (AjanKaynagi y in k.Kaynaklar)
            {
                Console.WriteLine($"  [{y.No}] {y.Ad} — {y.Etiket}");
            }
            return 0;
        }
        await Task.Delay(500);
    }
}

async Task TurAsync(int n)
{
    using var baglanti = await YonetimBaglayici.BaglanAsync(depo, default, guven)
        ?? throw new IOException("Servis bulunamadı");
    string kanal = baglanti.Ad;
    var durum = new ConcurrentBag<double>();
    var komut = new ConcurrentBag<double>();
    var hatalar = new ConcurrentDictionary<string, int>();
    int reddedilen = 0, biten = 0, sorulan = 0;
    using var bitis = new CancellationTokenSource(TimeSpan.FromSeconds(sure));
    string tur = Guid.NewGuid().ToString("N")[..6];

    YonetimKomutu K(string eylem, int i) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Zaman = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        Eylem = eylem,
        Kullanici = "yuk" + i,
    };

    async Task IstemciAsync(int i)
    {
        var r = new Random(i);
        await Task.Delay(r.Next(2000));
        int soruNo = 0;
        while (!bitis.IsCancellationRequested)
        {
            try
            {
                var s = Stopwatch.StartNew();
                await baglanti.DurumAsync(default);
                durum.Add(s.Elapsed.TotalMilliseconds);

                if (soru)
                {
                    var k = K(Eylemler.SoruSor, i);
                    k.Soru = $"sarnıç deneme {tur} {i} {soruNo++}";
                    s.Restart();
                    KomutSonucu c = await baglanti.GonderAsync(k, default);
                    komut.Add(s.Elapsed.TotalMilliseconds);
                    if (c.Basarili)
                    {
                        Interlocked.Increment(ref sorulan);
                    }
                    else
                    {
                        Interlocked.Increment(ref reddedilen);
                    }
                    s.Restart();
                    KomutSonucu l = await baglanti.GonderAsync(K(Eylemler.SoruListele, i), default);
                    komut.Add(s.Elapsed.TotalMilliseconds);
                }
            }
            catch (Exception e)
            {
                hatalar.AddOrUpdate(e.GetType().Name + ": " + e.Message, 1, (_, v) => v + 1);
            }
            try
            {
                await Task.Delay(2000, bitis.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    var surec = Stopwatch.StartNew();
    await Task.WhenAll(Enumerable.Range(0, n).Select(IstemciAsync));
    YonetimDurumu? son = await baglanti.DurumAsync(default);

    static string Yuzde(ConcurrentBag<double> b)
    {
        if (b.IsEmpty)
        {
            return "yok";
        }
        double[] d = b.OrderBy(x => x).ToArray();
        return $"adet={d.Length} p50={d[d.Length / 2]:F0}ms p95={d[(int)(d.Length * 0.95)]:F0}ms en çok={d[^1]:F0}ms";
    }

    Console.WriteLine($"KULLANICI={n} kanal={kanal} süre={surec.Elapsed.TotalSeconds:F0}s");
    Console.WriteLine($"  durum : {Yuzde(durum)}");
    Console.WriteLine($"  komut : {Yuzde(komut)}");
    Console.WriteLine($"  soru  : kabul={sorulan} red={reddedilen} bekleyen={son?.SoruBekleyen}");
    Console.WriteLine($"  hata  : {hatalar.Values.Sum()} {string.Join(" | ", hatalar.Select(h => $"{h.Value}x {h.Key}").Take(5))}");
}

static async Task<int> SahteModelAsync(int port, int gecikme)
{
    using var d = new HttpListener();
    d.Prefixes.Add($"http://127.0.0.1:{port}/");
    d.Start();
    while (true)
    {
        HttpListenerContext c = await d.GetContextAsync();
        _ = Task.Run(async () =>
        {
            string yanit = "{}";
            if (c.Request.Url!.AbsolutePath.Contains("chat"))
            {
                await Task.Delay(gecikme);
                yanit = new JsonObject
                {
                    ["choices"] = new JsonArray(new JsonObject
                    {
                        ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = "Sahte cevap [1]." },
                    }),
                }.ToJsonString();
            }
            byte[] b = Encoding.UTF8.GetBytes(yanit);
            c.Response.ContentType = "application/json";
            await c.Response.OutputStream.WriteAsync(b);
            c.Response.Close();
        });
    }
}
