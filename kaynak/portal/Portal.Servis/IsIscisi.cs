namespace Portal.Servis;

using System.Diagnostics;
using System.Text;
using Portal.Cekirdek.Yonetim;

/// <summary>İş kaydından başlatılacak süreci üretir.</summary>
public interface ISurecPlani
{
    ProcessStartInfo Olustur(IsKaydi kayit);
}

/// <summary>Atölye Python'unu .bat dosyalarıyla aynı ortamda başlatır. Kabuk kullanmaz.</summary>
public sealed class AtolyeSurecPlani : ISurecPlani
{
    private readonly string _kok;
    private readonly string _depo;

    public AtolyeSurecPlani(string atolyeKoku, string depo)
    {
        _kok = atolyeKoku;
        _depo = depo;
    }

    public static List<string> Argumanlar(IsKaydi k, string depo)
    {
        var a = new List<string> { "-m", "sarnicatolye" };
        switch (k.Tur)
        {
            case IsSabitleri.TurIsle:
                a.AddRange(new[] { "isle", k.Yol });
                break;
            case IsSabitleri.TurBelge:
                a.AddRange(new[] { "belge", k.Yol });
                break;
            case IsSabitleri.TurDizinle:
                return new List<string>(a) { "dizinle", "--kok", k.Yol, "--depo", depo, "--bir-kez" };
            default:
                throw new InvalidOperationException("Bilinmeyen iş türü: " + k.Tur);
        }
        if (!string.IsNullOrWhiteSpace(k.Cikis))
        {
            a.AddRange(new[] { "--cikis", k.Cikis });
        }
        return a;
    }

    public ProcessStartInfo Olustur(IsKaydi kayit)
    {
        var bilgi = new ProcessStartInfo(Path.Combine(_kok, "python", "python.exe")) { WorkingDirectory = _kok };
        foreach (string arguman in Argumanlar(kayit, _depo))
        {
            bilgi.ArgumentList.Add(arguman);
        }
        bilgi.Environment["HF_HUB_OFFLINE"] = "1";
        bilgi.Environment["TRANSFORMERS_OFFLINE"] = "1";
        bilgi.Environment["HF_HUB_DISABLE_TELEMETRY"] = "1";
        bilgi.Environment["PYTHONNOUSERSITE"] = "1";
        bilgi.Environment["PYTHONUTF8"] = "1";
        bilgi.Environment["PYTHONIOENCODING"] = "utf-8";
        bilgi.Environment["SARNICATOLYE_KOK"] = _kok + Path.DirectorySeparatorChar;
        bilgi.Environment["SARNICATOLYE_PAUSE"] = "0";
        string sistem = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        bilgi.Environment["PATH"] = string.Join(';', Path.Combine(_kok, "ffmpeg", "bin"), Path.Combine(_kok, "cuda"),
            Path.Combine(_kok, "python"), Path.Combine(sistem, "System32"), sistem);
        return bilgi;
    }
}

/// <summary>
/// Tek işçi: aynı anda tek iş, her iş ayrı süreç.
/// Kullanıcı kararı: yalnız işlem gören model ayakta olur. Süreç bitince model bellekten çıkar.
/// </summary>
public sealed class IsIscisi
{
    private readonly IsKuyrugu _kuyruk;
    private readonly ISurecPlani _plan;
    private readonly string _gunlukKlasoru;
    private readonly TimeSpan _bekleme;
    private readonly AjanIscisi? _ajan;
    /// <summary>Art arda en çok bu kadar soru: sorular uzun işi süresiz bekletmesin.</summary>
    private const int ArtArdaEnCokSoru = 5;
    private readonly object _kilit = new();
    private Process? _surec;
    private long _calisanId;
    private bool _iptalIstendi;
    private static readonly TimeSpan SatirAraligi = TimeSpan.FromSeconds(1);

    public IsIscisi(IsKuyrugu kuyruk, ISurecPlani plan, string gunlukKlasoru, TimeSpan? bekleme = null, AjanIscisi? ajan = null)
    {
        _kuyruk = kuyruk;
        _plan = plan;
        _gunlukKlasoru = gunlukKlasoru;
        _bekleme = bekleme ?? TimeSpan.FromSeconds(1);
        _ajan = ajan;
        Directory.CreateDirectory(gunlukKlasoru);
    }

    /// <summary>Çalışan işi süreç ağacıyla durdurur.</summary>
    public bool CalisaniIptalEt(long id)
    {
        lock (_kilit)
        {
            if (_calisanId != id)
            {
                return false;
            }
            // Süreç henüz başlamadıysa işaret yeterlidir; başlatma adımı işareti görür.
            _iptalIstendi = true;
            if (_surec != null)
            {
                Oldur(_surec);
            }
            return true;
        }
    }

    private static void Oldur(Process surec)
    {
        try
        {
            surec.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException)
        {
            // Süreç o anda bitmiş olabilir.
        }
    }

    private IsKaydi? SiradakiniAl()
    {
        lock (_kilit)
        {
            IsKaydi? kayit = _kuyruk.SiradakiniAl();
            if (kayit != null)
            {
                _calisanId = kayit.Id;
                _iptalIstendi = false;
            }
            return kayit;
        }
    }

    public async Task CalistirAsync(CancellationToken durdur)
    {
        int artArda = 0;
        while (!durdur.IsCancellationRequested)
        {
            // Tek GPU: soru ve iş aynı anda çalışmaz. Model yalnız soru varken ayaktadır.
            if (_ajan != null && artArda < ArtArdaEnCokSoru && await _ajan.SoruIsleAsync(durdur))
            {
                artArda++;
                continue;
            }
            artArda = 0;
            IsKaydi? kayit = SiradakiniAl();
            if (kayit == null)
            {
                _ajan?.BostaysaKapat();
                try
                {
                    await Task.Delay(_bekleme, durdur);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                continue;
            }
            _ajan?.Kapat();
            await TekIsAsync(kayit, durdur);
        }
        _ajan?.Kapat();
    }

    public async Task TekIsAsync(IsKaydi kayit, CancellationToken durdur)
    {
        string gunluk = Path.Combine(_gunlukKlasoru, $"is_{kayit.Id}.log");
        lock (_kilit)
        {
            if (_calisanId != kayit.Id)
            {
                _calisanId = kayit.Id;
                _iptalIstendi = false;
            }
        }
        try
        {
            var bilgi = _plan.Olustur(kayit);
            bilgi.UseShellExecute = false;
            bilgi.CreateNoWindow = true;
            bilgi.RedirectStandardOutput = true;
            bilgi.RedirectStandardError = true;
            bilgi.StandardOutputEncoding = Encoding.UTF8;
            bilgi.StandardErrorEncoding = Encoding.UTF8;

            using var surec = new Process { StartInfo = bilgi };
            await using var yazici = new StreamWriter(gunluk, append: false, Encoding.UTF8);
            var yazmaKilidi = new object();
            string? sonSatir = null;
            DateTime sonKayit = DateTime.MinValue;
            DataReceivedEventHandler satir = (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data))
                {
                    return;
                }
                bool yaz;
                string kisa = e.Data.Length > 400 ? e.Data[..400] : e.Data;
                lock (yazmaKilidi)
                {
                    yazici.WriteLine(e.Data);
                    yazici.Flush();
                    sonSatir = kisa;
                    yaz = DateTime.UtcNow - sonKayit >= SatirAraligi;
                    if (yaz)
                    {
                        sonKayit = DateTime.UtcNow;
                    }
                }
                // Veritabanına saniyede en çok bir kez yazılır.
                if (yaz)
                {
                    _kuyruk.SonSatirYaz(kayit.Id, kisa);
                }
            };
            surec.OutputDataReceived += satir;
            surec.ErrorDataReceived += satir;

            lock (_kilit)
            {
                if (_iptalIstendi)
                {
                    _calisanId = 0;
                    _kuyruk.Bitir(kayit.Id, IsSabitleri.Iptal, null);
                    return;
                }
                surec.Start();
                _surec = surec;
            }
            surec.BeginOutputReadLine();
            surec.BeginErrorReadLine();

            try
            {
                await surec.WaitForExitAsync(durdur);
            }
            catch (OperationCanceledException)
            {
                // Servis kapanıyor: iş kuyruğa geri döner, açılışta yeniden başlar.
                Oldur(surec);
                await surec.WaitForExitAsync(CancellationToken.None);
                surec.WaitForExit();
                lock (_kilit)
                {
                    _surec = null;
                    _calisanId = 0;
                }
                return;
            }
            surec.WaitForExit();
            if (sonSatir != null)
            {
                _kuyruk.SonSatirYaz(kayit.Id, sonSatir);
            }

            bool iptal;
            lock (_kilit)
            {
                iptal = _iptalIstendi;
                _surec = null;
                _calisanId = 0;
            }
            _kuyruk.Bitir(kayit.Id, iptal ? IsSabitleri.Iptal : surec.ExitCode == 0 ? IsSabitleri.Bitti : IsSabitleri.Hata, surec.ExitCode);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            lock (_kilit)
            {
                _surec = null;
                _calisanId = 0;
            }
            _kuyruk.SonSatirYaz(kayit.Id, "Başlatılamadı: " + e.Message);
            _kuyruk.Bitir(kayit.Id, IsSabitleri.Hata, null);
        }
    }
}
