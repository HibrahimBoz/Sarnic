namespace Portal.Servis;

using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Portal.Cekirdek.Yonetim;

/// <summary>Modeli gerektiğinde açıp kapatan kaynak.</summary>
public interface IModelKaynagi
{
    bool Kurulu { get; }
    Task<ISohbetModeli> AcAsync(CancellationToken iptal);
    void Kapat();
}

/// <summary>llama-server sürecini yalnız 127.0.0.1 üzerinde açar. Ağa açılmaz.</summary>
public sealed class LlamaModelKaynagi : IModelKaynagi
{
    private static readonly TimeSpan AcilisSiniri = TimeSpan.FromMinutes(3);
    private readonly string _exe;
    private readonly string? _model;
    private Process? _surec;
    private LlamaSohbetModeli? _sohbet;

    public LlamaModelKaynagi(string exe, string? model)
    {
        _exe = exe;
        _model = model;
    }

    public bool Kurulu => File.Exists(_exe) && _model != null && File.Exists(_model);

    public async Task<ISohbetModeli> AcAsync(CancellationToken iptal)
    {
        if (_sohbet != null && _surec is { HasExited: false })
        {
            return _sohbet;
        }
        Kapat();
        ArtiklariDurdur();

        int port = BosPort();
        var bilgi = new ProcessStartInfo(_exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(_exe)!,
        };
        foreach (string a in new[] { "-m", _model!, "--host", "127.0.0.1", "--port", port.ToString(), "-ngl", "99", "-c", "8192", "-np", "1", "--jinja", "--no-webui" })
        {
            bilgi.ArgumentList.Add(a);
        }
        var surec = new Process { StartInfo = bilgi };
        surec.OutputDataReceived += (_, _) => { };
        surec.ErrorDataReceived += (_, _) => { };
        surec.Start();
        surec.BeginOutputReadLine();
        surec.BeginErrorReadLine();
        _surec = surec;

        var sohbet = new LlamaSohbetModeli($"http://127.0.0.1:{port}/");
        _sohbet = sohbet;
        DateTime son = DateTime.UtcNow + AcilisSiniri;
        try
        {
            while (!await sohbet.HazirMiAsync(iptal))
            {
                if (surec.HasExited)
                {
                    throw new IOException($"Model süreci kapandı (kod {surec.ExitCode}).");
                }
                if (DateTime.UtcNow > son)
                {
                    throw new TimeoutException("Model 3 dakikada açılmadı.");
                }
                await Task.Delay(500, iptal);
            }
        }
        catch
        {
            // Yarım açılmış model GPU belleğini tutmasın.
            Kapat();
            throw;
        }
        return sohbet;
    }

    public void Kapat()
    {
        _sohbet?.Dispose();
        _sohbet = null;
        if (_surec != null)
        {
            try
            {
                if (!_surec.HasExited)
                {
                    _surec.Kill(entireProcessTree: true);
                    _surec.WaitForExit(10_000);
                }
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException)
            {
                // Süreç o anda bitmiş olabilir.
            }
            _surec.Dispose();
            _surec = null;
        }
    }

    /// <summary>Servis çökmüşse eski model GPU belleğini tutar. Yalnız aynı yoldaki süreç durdurulur.</summary>
    public void ArtiklariDurdur()
    {
        string ad = Path.GetFileNameWithoutExtension(_exe);
        foreach (Process p in Process.GetProcessesByName(ad))
        {
            try
            {
                if (string.Equals(p.MainModule?.FileName, Path.GetFullPath(_exe), StringComparison.OrdinalIgnoreCase))
                {
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(10_000);
                }
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException)
            {
            }
            finally
            {
                p.Dispose();
            }
        }
    }

    private static int BosPort()
    {
        var dinleyici = new TcpListener(IPAddress.Loopback, 0);
        dinleyici.Start();
        int port = ((IPEndPoint)dinleyici.LocalEndpoint).Port;
        dinleyici.Stop();
        return port;
    }
}

/// <summary>llama-server OpenAI uyumlu sohbet uç noktası.</summary>
public sealed class LlamaSohbetModeli : ISohbetModeli, IDisposable
{
    private readonly HttpClient _istemci;

    public LlamaSohbetModeli(string adres)
    {
        _istemci = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(adres), Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<bool> HazirMiAsync(CancellationToken iptal)
    {
        try
        {
            using var kisa = CancellationTokenSource.CreateLinkedTokenSource(iptal);
            kisa.CancelAfter(TimeSpan.FromSeconds(2));
            using var cevap = await _istemci.GetAsync("health", kisa.Token);
            return cevap.IsSuccessStatusCode;
        }
        catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !iptal.IsCancellationRequested))
        {
            return false;
        }
    }

    public async Task<ModelCevabi> TamamlaAsync(JsonArray mesajlar, JsonArray? araclar, CancellationToken iptal)
    {
        var govde = new JsonObject
        {
            ["messages"] = mesajlar.DeepClone(),
            ["temperature"] = 0.2,
            ["max_tokens"] = 1500,
        };
        if (araclar != null)
        {
            govde["tools"] = araclar;
        }
        using var cevap = await _istemci.PostAsJsonAsync("v1/chat/completions", govde, iptal);
        cevap.EnsureSuccessStatusCode();
        JsonNode kok = await cevap.Content.ReadFromJsonAsync<JsonNode>(iptal) ?? throw new IOException("Model boş cevap verdi.");
        JsonObject mesaj = kok["choices"]?[0]?["message"]?.AsObject() ?? throw new IOException("Model cevabı okunamadı.");
        if (kok["timings"] is JsonObject olcum)
        {
            Console.WriteLine($"Ajan turu: {olcum["prompt_n"]} giriş, {olcum["predicted_n"]} çıkış belirteci, {olcum["predicted_per_second"]} belirteç/sn.");
        }
        var cagrilar = new List<AracCagrisi>();
        if (mesaj["tool_calls"] is JsonArray dizi)
        {
            foreach (JsonNode? c in dizi)
            {
                cagrilar.Add(new AracCagrisi(
                    c?["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
                    c?["function"]?["name"]?.GetValue<string>() ?? string.Empty,
                    c?["function"]?["arguments"]?.GetValue<string>() ?? "{}"));
            }
        }
        return new ModelCevabi(mesaj["content"]?.GetValue<string>(), cagrilar, (JsonObject)mesaj.DeepClone());
    }

    public void Dispose() => _istemci.Dispose();
}

/// <summary>
/// Soruları tek tek cevaplar. İş işçisinin döngüsü içinde çalışır, bu nedenle GPU'yu işlerle paylaşmaz.
/// Kullanıcı kararı: model yalnız işlem varken ayaktadır; boşta kalınca kapanır.
/// </summary>
public sealed class AjanIscisi
{
    private readonly IsKuyrugu _kuyruk;
    private readonly IModelKaynagi _model;
    private readonly IAramaKaynagi _kaynak;
    private readonly TimeSpan _bostaSiniri;
    private readonly TimeSpan _soruSiniri;
    private readonly object _kilit = new();
    private long _calisanId;
    private string? _calisanKullanici;
    private CancellationTokenSource? _calisanIptal;
    private DateTime? _sonKullanim;
    private DateTime _sonYenileme = DateTime.MinValue;

    public AjanIscisi(IsKuyrugu kuyruk, IModelKaynagi model, IAramaKaynagi kaynak, TimeSpan? bostaSiniri = null, TimeSpan? soruSiniri = null)
    {
        _kuyruk = kuyruk;
        _model = model;
        _kaynak = kaynak;
        _bostaSiniri = bostaSiniri ?? TimeSpan.FromSeconds(60);
        _soruSiniri = soruSiniri ?? TimeSpan.FromMinutes(5);
    }

    public bool Kurulu => _model.Kurulu;

    /// <summary>Önbellek anahtarı için sürüm. Dizin en çok 30 saniyede bir yenilenir: yeni yayın bayat cevap döndürmesin.</summary>
    public async Task<string> DizinSurumuAsync()
    {
        if (DateTime.UtcNow - _sonYenileme >= TimeSpan.FromSeconds(30))
        {
            _sonYenileme = DateTime.UtcNow;
            try
            {
                await _kaynak.HazirlaAsync(CancellationToken.None);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // Paylaşım geçici erişilemez olabilir; eldeki sürüm kullanılır.
            }
        }
        return _kaynak.Surum();
    }

    /// <summary>Kişi yalnız kendi çalışan sorusunu iptal eder.</summary>
    public bool CalisaniIptalEt(long id, string kullanici)
    {
        lock (_kilit)
        {
            if (_calisanId != id || _calisanKullanici != kullanici || _calisanIptal == null)
            {
                return false;
            }
            _calisanIptal.Cancel();
            return true;
        }
    }

    /// <summary>Bir soru işlediyse true döner.</summary>
    public async Task<bool> SoruIsleAsync(CancellationToken durdur)
    {
        if (!_model.Kurulu)
        {
            return false;
        }
        SoruKaydi? soru = _kuyruk.SiradakiSoruyuAl();
        if (soru == null)
        {
            return false;
        }
        using var iptal = CancellationTokenSource.CreateLinkedTokenSource(durdur);
        lock (_kilit)
        {
            _calisanId = soru.Id;
            _calisanKullanici = soru.Kullanici;
            _calisanIptal = iptal;
        }
        iptal.CancelAfter(_soruSiniri);
        try
        {
            await _kaynak.HazirlaAsync(iptal.Token);
            ISohbetModeli model = await _model.AcAsync(iptal.Token);
            AjanCevabi cevap = await new AjanDongusu(_kaynak).YanitlaAsync(soru.Soru, model, iptal.Token);
            _kuyruk.SoruBitir(soru.Id, cevap.Basarisiz ? IsSabitleri.Hata : IsSabitleri.Bitti, cevap.Metin, cevap.Kaynaklar);
        }
        catch (OperationCanceledException) when (durdur.IsCancellationRequested)
        {
            _kuyruk.SoruyuGeriKoy(soru.Id);
        }
        catch (OperationCanceledException)
        {
            // İptal düğmesi de süre sınırı da buraya gelir; model süreci çalışmaya devam etmesin.
            _model.Kapat();
            _kuyruk.SoruBitir(soru.Id, IsSabitleri.Iptal, "Soru iptal edildi ya da 5 dakika sınırını aştı.", null);
        }
        catch (Exception e)
        {
            // Tek sorunun hatası işçiyi durdurmasın: işler ve öteki sorular beklemede kalır.
            _model.Kapat();
            _kuyruk.SoruBitir(soru.Id, IsSabitleri.Hata, "Ajan cevap üretemedi: " + e.Message, null);
        }
        finally
        {
            lock (_kilit)
            {
                _calisanId = 0;
                _calisanKullanici = null;
                _calisanIptal = null;
            }
            _sonKullanim = DateTime.UtcNow;
        }
        return true;
    }

    public void BostaysaKapat()
    {
        if (_sonKullanim is DateTime zaman && DateTime.UtcNow - zaman >= _bostaSiniri)
        {
            Kapat();
        }
    }

    public void Kapat()
    {
        _model.Kapat();
        _sonKullanim = null;
    }
}
