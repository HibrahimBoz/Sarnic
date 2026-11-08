// Atölye servisi yük testi. Paket içine girmez.
// Yük:  Portal.YukTesti --depo <depo> --kullanici 10,50,100 [--sure 60]
using System.Collections.Concurrent;
using System.Diagnostics;
using Portal.Cekirdek.Yonetim;

string? Arg(string ad)
{
    int i = Array.IndexOf(args, ad);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

string depo = Arg("--depo") ?? throw new ArgumentException("--depo gerekli");
int sure = int.Parse(Arg("--sure") ?? "60");
string guven = Path.Combine(Path.GetTempPath(), "yuk_guven_" + Guid.NewGuid().ToString("N") + ".txt");
string yonetim = Path.Combine(depo, YonetimDosyalari.KlasorAdi);

foreach (int n in (Arg("--kullanici") ?? "10,50,100").Split(',').Select(int.Parse))
{
    await TurAsync(n);
}
File.Delete(guven);
return 0;

async Task TurAsync(int n)
{
    using var baglanti = await YonetimBaglayici.BaglanAsync(depo, default, guven)
        ?? throw new IOException("Servis bulunamadı");
    string kanal = baglanti.Ad;
    var durum = new ConcurrentBag<double>();
    var hatalar = new ConcurrentDictionary<string, int>();
    using var bitis = new CancellationTokenSource(TimeSpan.FromSeconds(sure));

    async Task IstemciAsync(int i)
    {
        var r = new Random(i);
        await Task.Delay(r.Next(2000));
        while (!bitis.IsCancellationRequested)
        {
            try
            {
                var s = Stopwatch.StartNew();
                await baglanti.DurumAsync(default);
                durum.Add(s.Elapsed.TotalMilliseconds);

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
    Console.WriteLine($"  hata  : {hatalar.Values.Sum()} {string.Join(" | ", hatalar.Select(h => $"{h.Value}x {h.Key}").Take(5))}");
}
