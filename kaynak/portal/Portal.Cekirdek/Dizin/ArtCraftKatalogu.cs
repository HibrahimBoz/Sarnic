namespace Portal.Cekirdek.Dizin;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

/// <summary>
/// Sistemde ve paket içerisinde bulunan ArtCraft uygulaması kaydı.
/// </summary>
public sealed record ArtCraftUygulamasi(string Ad, string ExeYolu, Version Surum, IReadOnlyList<string> Uzantilar, bool Paketli);

/// <summary>
/// Taşınabilir ve sisteme kurulu ArtCraft uygulamalarını keşfeden ve dosya açma bilgilerini oluşturan katalog.
/// </summary>
public sealed class ArtCraftKatalogu
{
    private static readonly (string Ad, string[] Uzantilar)[] TanimliUygulamalar =
    [
        ("WordCraft", ["docx", "docm", "dotx", "dotm", "doc", "dot", "rtf", "odt", "md", "txt", "html", "tex"]),
        ("GridCraft", ["xlsx", "xlsm", "xlsb", "xls", "ods", "csv", "tsv"]),
        ("DeckCraft", ["pptx", "potx", "ppsx"]),
        ("PdfCraft", ["pdf"]),
        ("PhotoCraft", ["png", "jpg", "jpeg", "tif", "tiff", "bmp", "gif", "webp", "tga", "psd", "ico", "avif"]),
        ("FigCraft", ["svg"]),
        ("CADCraft", ["dwg", "dxf"])
    ];

    private readonly string _paketKlasoru;
    private readonly List<string> _aramaKokleri;
    private readonly object _kilit = new();
    private IReadOnlyList<ArtCraftUygulamasi>? _uygulamalar;

    /// <summary>
    /// Testler için özel sürüm çözümleme kancası. Null değilse FileVersionInfo yerine bu çağrılır.
    /// </summary>
    public static Func<string, Version?>? SurumOkuyucu { get; set; }

    /// <summary>
    /// ArtCraft kataloğunu paket klasörü ve sistem arama kökleriyle ilklendirir.
    /// </summary>
    public ArtCraftKatalogu(string? paketKlasoru = null, IEnumerable<string>? aramaKokleri = null)
    {
        _paketKlasoru = string.IsNullOrWhiteSpace(paketKlasoru)
            ? AppContext.BaseDirectory
            : paketKlasoru;

        _aramaKokleri = aramaKokleri != null
            ? aramaKokleri.Where(k => !string.IsNullOrWhiteSpace(k)).ToList()
            : VarsayilanAramaKokleri();
    }

    /// <summary>
    /// Keşfedilen güncel ArtCraft uygulamaları. İlk erişimde taranır.
    /// </summary>
    public IReadOnlyList<ArtCraftUygulamasi> Uygulamalar
    {
        get
        {
            if (_uygulamalar == null)
            {
                lock (_kilit)
                {
                    _uygulamalar ??= Tara();
                }
            }
            return _uygulamalar;
        }
    }

    /// <summary>
    /// Uygulama listesini temizler ve yeniden tarar.
    /// </summary>
    public void Yenile()
    {
        lock (_kilit)
        {
            _uygulamalar = Tara();
        }
    }

    /// <summary>
    /// Belirtilen dosya uzantısını destekleyen ArtCraft uygulamalarını döner.
    /// </summary>
    public IReadOnlyList<ArtCraftUygulamasi> UzantiIcin(string uzanti)
    {
        if (string.IsNullOrWhiteSpace(uzanti))
        {
            return Array.Empty<ArtCraftUygulamasi>();
        }

        string temiz = uzanti.Trim().TrimStart('.').ToLowerInvariant();
        var eslesenler = new List<ArtCraftUygulamasi>();
        foreach (var u in Uygulamalar)
        {
            if (u.Uzantilar.Any(x => string.Equals(x, temiz, StringComparison.OrdinalIgnoreCase)))
            {
                eslesenler.Add(u);
            }
        }
        return eslesenler;
    }

    /// <summary>
    /// Bir dosyayı ArtCraft uygulamasıyla başlatmak için gereken ProcessStartInfo nesnesini hazırlar.
    /// </summary>
    public static ProcessStartInfo AcmaBilgisi(ArtCraftUygulamasi u, string dosya)
    {
        ArgumentNullException.ThrowIfNull(u);
        ArgumentException.ThrowIfNullOrWhiteSpace(dosya);

        string exeKlasoru = Path.GetDirectoryName(Path.GetFullPath(u.ExeYolu)) ?? string.Empty;
        var psi = new ProcessStartInfo
        {
            FileName = u.ExeYolu,
            UseShellExecute = false,
            WorkingDirectory = exeKlasoru
        };
        psi.ArgumentList.Add(dosya);
        return psi;
    }

    /// <summary>
    /// Depodaki düzenlenebilir kopyayı kullanıcı Belgeler klasörüne çakışma ekiyle kopyalar ve hedef yolu döner.
    /// </summary>
    public static string DuzenlenebilirKopyaHazirla(string kaynak, string ozgunAd, string? hedefKlasor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kaynak);
        ArgumentException.ThrowIfNullOrWhiteSpace(ozgunAd);

        if (string.IsNullOrWhiteSpace(hedefKlasor))
        {
            string belgelerim = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(belgelerim))
            {
                string profil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                belgelerim = Path.Combine(profil, "Documents");
            }
            hedefKlasor = Path.Combine(belgelerim, "Sarnic");
        }

        Directory.CreateDirectory(hedefKlasor);

        string temizAd = ozgunAd;
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            temizAd = temizAd.Replace(c, '_');
        }

        string govde = Path.GetFileNameWithoutExtension(temizAd);
        string uzanti = Path.GetExtension(kaynak);
        if (!string.IsNullOrEmpty(uzanti) && !uzanti.StartsWith('.'))
        {
            uzanti = "." + uzanti;
        }

        string hedefDosyaAdi = $"{govde}{uzanti}";
        string hedefYol = Path.Combine(hedefKlasor, hedefDosyaAdi);

        if (File.Exists(hedefYol))
        {
            int sayac = 2;
            while (true)
            {
                hedefDosyaAdi = $"{govde} ({sayac}){uzanti}";
                hedefYol = Path.Combine(hedefKlasor, hedefDosyaAdi);
                if (!File.Exists(hedefYol))
                {
                    break;
                }
                sayac++;
            }
        }

        File.Copy(kaynak, hedefYol, overwrite: false);
        return hedefYol;
    }

    private IReadOnlyList<ArtCraftUygulamasi> Tara()
    {
        var sonuclar = new List<ArtCraftUygulamasi>();

        foreach (var tanim in TanimliUygulamalar)
        {
            string adKucuk = tanim.Ad.ToLowerInvariant();
            string exeAdi = $"{adKucuk}.exe";
            var adaylar = new List<ArtCraftUygulamasi>();

            // (1) <paket>\artcraft\<adküçük>\ altında özyinelemeli <adküçük>.exe
            string paketKlasor = Path.Combine(_paketKlasoru, "artcraft", adKucuk);
            if (Directory.Exists(paketKlasor))
            {
                try
                {
                    foreach (string dosya in Directory.EnumerateFiles(paketKlasor, exeAdi, new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                    {
                        Version surum = SurumBelirle(dosya);
                        adaylar.Add(new ArtCraftUygulamasi(tanim.Ad, Path.GetFullPath(dosya), surum, tanim.Uzantilar, Paketli: true));
                    }
                }
                catch
                {
                    // Okuma veya izin hatalarını yut
                }
            }

            // (2) her arama kökünde <Ad>\<adküçük>.exe veya <adküçük>\<adküçük>.exe
            foreach (string kok in _aramaKokleri)
            {
                if (!Directory.Exists(kok))
                {
                    continue;
                }

                string yolBuyuk = Path.Combine(kok, tanim.Ad, exeAdi);
                if (File.Exists(yolBuyuk))
                {
                    Version surum = SurumBelirle(yolBuyuk);
                    adaylar.Add(new ArtCraftUygulamasi(tanim.Ad, Path.GetFullPath(yolBuyuk), surum, tanim.Uzantilar, Paketli: false));
                }
                else
                {
                    string yolKucuk = Path.Combine(kok, adKucuk, exeAdi);
                    if (File.Exists(yolKucuk))
                    {
                        Version surum = SurumBelirle(yolKucuk);
                        adaylar.Add(new ArtCraftUygulamasi(tanim.Ad, Path.GetFullPath(yolKucuk), surum, tanim.Uzantilar, Paketli: false));
                    }
                }
            }

            if (adaylar.Count > 0)
            {
                // Aynı ad için EN YÜKSEK sürüm kalır; eşitse paketli olan.
                var enIyi = adaylar
                    .OrderByDescending(a => a.Surum)
                    .ThenByDescending(a => a.Paketli ? 1 : 0)
                    .First();
                sonuclar.Add(enIyi);
            }
        }

        return sonuclar;
    }

    private static Version SurumBelirle(string exeYolu)
    {
        var kanca = SurumOkuyucu;
        if (kanca != null)
        {
            try
            {
                var v = kanca(exeYolu);
                return v ?? new Version(0, 0, 0, 0);
            }
            catch
            {
                return new Version(0, 0, 0, 0);
            }
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(exeYolu);
            var parsed = SurumAyristir(info.ProductVersion);
            if (parsed != new Version(0, 0, 0, 0))
            {
                return parsed;
            }

            parsed = SurumAyristir(info.FileVersion);
            if (parsed != new Version(0, 0, 0, 0))
            {
                return parsed;
            }
        }
        catch
        {
            // Okuma hatası durumunda 0.0.0.0 döner
        }

        return new Version(0, 0, 0, 0);
    }

    /// <summary>
    /// Sürüm metnini 4 bileşenli Version nesnesine ayrıştırır. Eksik bileşenler 0 yapılır.
    /// Baştaki 'v'/'V' ve '+' ya da '-' sonrası ekler atılır.
    /// </summary>
    public static Version SurumAyristir(string? metin)
    {
        if (string.IsNullOrWhiteSpace(metin))
        {
            return new Version(0, 0, 0, 0);
        }

        string temiz = metin.Trim();
        if (temiz.StartsWith('v') || temiz.StartsWith('V'))
        {
            temiz = temiz[1..].Trim();
        }

        int arti = temiz.IndexOf('+');
        int eksi = temiz.IndexOf('-');
        int kesim = -1;
        if (arti >= 0 && eksi >= 0) kesim = Math.Min(arti, eksi);
        else if (arti >= 0) kesim = arti;
        else if (eksi >= 0) kesim = eksi;

        if (kesim >= 0)
        {
            temiz = temiz[..kesim].Trim();
        }

        if (Version.TryParse(temiz, out var v))
        {
            return NormalizeDortBilesen(v);
        }

        if (int.TryParse(temiz, out int tekSayi))
        {
            return new Version(tekSayi, 0, 0, 0);
        }

        return new Version(0, 0, 0, 0);
    }

    private static Version NormalizeDortBilesen(Version v)
    {
        int build = v.Build >= 0 ? v.Build : 0;
        int revision = v.Revision >= 0 ? v.Revision : 0;
        return new Version(v.Major, v.Minor, build, revision);
    }

    private static List<string> VarsayilanAramaKokleri()
    {
        var kokler = new List<string>();

        string? pf = Environment.GetEnvironmentVariable("ProgramFiles");
        if (!string.IsNullOrWhiteSpace(pf))
        {
            kokler.Add(pf);
        }

        string? pfx86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        if (!string.IsNullOrWhiteSpace(pfx86))
        {
            kokler.Add(pfx86);
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            kokler.Add(Path.Combine(localAppData, "Programs"));
        }

        return kokler;
    }
}
