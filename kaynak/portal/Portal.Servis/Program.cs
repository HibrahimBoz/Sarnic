using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Portal.Cekirdek.Yonetim;
using Portal.Servis;

// Kullanım: SarnicServis --depo \\sunucu\Sarnic-Veri [--port 8765] [--kok <atölye>]
//           SarnicServis --parola-ayarla [--kok <atölye>]
string? Arguman(string ad)
{
    int i = Array.IndexOf(args, ad);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

string kok = Path.GetFullPath(Arguman("--kok") ?? Path.Combine(AppContext.BaseDirectory, ".."));
string isKlasoru = Path.Combine(kok, "is");
string parolaYolu = Path.Combine(isKlasoru, "yonetim_parola.json");
string ayarYolu = Path.Combine(isKlasoru, "servis_ayar.json");

if (args.Contains("--parola-ayarla"))
{
    return ParolaAyarla(parolaYolu);
}

var kayitli = File.Exists(ayarYolu) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ayarYolu)) : null;
string? depo = Arguman("--depo") ?? kayitli?.GetValueOrDefault("depo");
if (string.IsNullOrWhiteSpace(depo))
{
    Console.Error.WriteLine("Depo klasörü gerekli: servis.bat --depo \\\\sunucu\\Sarnic-Veri");
    return 2;
}
int port = int.TryParse(Arguman("--port") ?? kayitli?.GetValueOrDefault("port"), out int p) ? p : 8765;
Directory.CreateDirectory(isKlasoru);
File.WriteAllText(ayarYolu, JsonSerializer.Serialize(new Dictionary<string, string> { ["depo"] = depo, ["port"] = port.ToString() }));

using var tekKopya = new Mutex(true, "Global\\SarnicAtolyeServisi", out bool ilkKopya);
if (!ilkKopya)
{
    Console.Error.WriteLine("Servis zaten çalışıyor.");
    return 3;
}

YonetimParolasi? parola = YonetimParolasi.Oku(parolaYolu);
if (parola == null)
{
    Console.WriteLine("UYARI: Yönetim parolası kurulmamış. Kuyruk temizleme kapalı. Kurmak için: servis.bat --parola-ayarla");
}

X509Certificate2 sertifika = SertifikaYukle(Path.Combine(isKlasoru, "servis.pfx"));
string yonetimKlasoru = Path.Combine(depo, YonetimDosyalari.KlasorAdi);

var kuyruk = new IsKuyrugu(Path.Combine(isKlasoru, "kuyruk.db"));
var isci = new IsIscisi(kuyruk, new AtolyeSurecPlani(kok, depo), Path.Combine(isKlasoru, "gunluk", "servis"));
var cekirdek = new YonetimCekirdegi(kuyruk, isci, parola, Path.Combine(isKlasoru, "denetim.log"), kok);
var dosyaKanali = new DosyaKanali(cekirdek, yonetimKlasoru);

var bilgi = new ServisBilgisi
{
    Url = $"https://{Dns.GetHostEntry(string.Empty).HostName}:{port}/",
    ParmakIzi = sertifika.GetCertHashString(HashAlgorithmName.SHA256),
    Tuz = parola?.Tuz,
    Iter = parola?.Iter ?? 0,
};
YonetimDosyalari.AtomikYaz(Path.Combine(yonetimKlasoru, YonetimDosyalari.ServisDosyasi), Path.Combine(yonetimKlasoru, "gecici"), bilgi, uzerineYaz: true);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
builder.WebHost.ConfigureKestrel(k => k.ListenAnyIP(port, l => l.UseHttps(sertifika)));
builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
builder.Services.AddAuthorization();
var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/durum", () => cekirdek.Durum()).RequireAuthorization();
app.MapPost("/api/komut", async (YonetimKomutu komut, HttpContext ctx) =>
    await cekirdek.IsleAsync(komut, ctx.User.Identity?.Name ?? "bilinmiyor", "ag")).RequireAuthorization();

using var durdur = new CancellationTokenSource();
// Port açılamazsa Kestrel de "durduruluyor" der; o durumda işçi ve paylaşım kanalı çalışmaya devam etmeli.
bool agBasladi = false;
app.Lifetime.ApplicationStarted.Register(() => agBasladi = true);
app.Lifetime.ApplicationStopping.Register(() =>
{
    if (agBasladi)
    {
        durdur.Cancel();
    }
});
Task iscGorevi = isci.CalistirAsync(durdur.Token);
Task dosyaGorevi = dosyaKanali.CalistirAsync(durdur.Token);

Console.WriteLine($"Sarnıç Atölye Servisi çalışıyor. Depo: {depo}, adres: {bilgi.Url}");
try
{
    await app.RunAsync();
}
catch (IOException e)
{
    // Port açılamazsa paylaşım klasörü yolu çalışmaya devam eder.
    Console.Error.WriteLine($"Ağ servisi açılamadı ({e.Message}). Yalnız paylaşım klasörü çalışıyor.");
    // Ölü adres istemciyi her açılışta bekletmesin.
    bilgi.Url = null;
    bilgi.ParmakIzi = null;
    YonetimDosyalari.AtomikYaz(Path.Combine(yonetimKlasoru, YonetimDosyalari.ServisDosyasi), Path.Combine(yonetimKlasoru, "gecici"), bilgi, uzerineYaz: true);
    Console.CancelKeyPress += (_, c) =>
    {
        c.Cancel = true;
        durdur.Cancel();
    };
    await Task.WhenAll(iscGorevi, dosyaGorevi);
}
durdur.Cancel();
await Task.WhenAll(iscGorevi, dosyaGorevi);
return 0;

static int ParolaAyarla(string yol)
{
    string Oku(string istem)
    {
        Console.Write(istem);
        if (Console.IsInputRedirected)
        {
            return Console.ReadLine() ?? string.Empty;
        }
        var parola = new System.Text.StringBuilder();
        while (true)
        {
            var tus = Console.ReadKey(intercept: true);
            if (tus.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return parola.ToString();
            }
            if (tus.Key == ConsoleKey.Backspace)
            {
                if (parola.Length > 0)
                {
                    parola.Length--;
                }
            }
            else if (!char.IsControl(tus.KeyChar))
            {
                parola.Append(tus.KeyChar);
            }
        }
    }

    string birinci = Oku($"Yeni yönetim parolası (en az {YonetimImza.EnAzParolaUzunlugu} karakter): ");
    if (birinci != Oku("Yeniden girin: "))
    {
        Console.Error.WriteLine("Parolalar aynı değil.");
        return 1;
    }
    try
    {
        YonetimParolasi.Olustur(birinci).Yaz(yol);
    }
    catch (ArgumentException e)
    {
        Console.Error.WriteLine(e.Message);
        return 1;
    }
    Console.WriteLine("Parola kaydedildi. Servisi yeniden başlatın.");
    return 0;
}

static X509Certificate2 SertifikaYukle(string yol)
{
    if (File.Exists(yol))
    {
        return new X509Certificate2(File.ReadAllBytes(yol), (string?)null, X509KeyStorageFlags.UserKeySet);
    }
    using var rsa = RSA.Create(2048);
    string makine = Dns.GetHostEntry(string.Empty).HostName;
    var istek = new CertificateRequest($"CN={makine}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    var san = new SubjectAlternativeNameBuilder();
    san.AddDnsName(makine);
    san.AddDnsName(Environment.MachineName);
    istek.CertificateExtensions.Add(san.Build());
    using var uretilen = istek.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
    byte[] pfx = uretilen.Export(X509ContentType.Pfx);
    File.WriteAllBytes(yol, pfx);
    return new X509Certificate2(pfx, (string?)null, X509KeyStorageFlags.UserKeySet);
}
