using System.IO;
using System.Windows;
using LibVLCSharp.Shared;
using Microsoft.Win32;
using Portal.Cekirdek;

namespace Portal.Uygulama;

/// <summary>
/// Bulgu WPF uygulaması ana giriş sınıfı.
/// </summary>
public partial class App : Application
{
    private static readonly string HataLogYolu = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Bulgu",
        "hata.log"
    );

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--vlc-onbellek", StringComparer.Ordinal))
        {
            try
            {
                LibVlcAyarlari.EklentiOnbellegiOlustur();
                Shutdown(0);
            }
            catch
            {
                Shutdown(1);
            }
            return;
        }

        // Yakalanmamış istisnalar için genel dinleyiciler
        SetupHataYakali();

        // LibVLCSharp Core.Initialize() uygulama başında bir kez
        try
        {
            Core.Initialize();
            LibVlcAyarlari.OnIsit();
        }
        catch (Exception ex)
        {
            HataKaydetVeGoster(ex, "LibVLC başlatılamadı");
        }

        // Kök klasör yapılandırmasını çöz (1. --kok, 2. kullanici.json, 3. ayar.json, 4. dialog)
        string? kokKlasor = KokYapilandirma.UygulamaKokuBelirle(
            e.Args,
            uyariVeDegistirSor: erisilemeyenYol =>
            {
                var cevap = MessageBox.Show(
                    $"Kök klasöre erişilemiyor: {erisilemeyenYol}\n\nKlasörü değiştirmek ister misiniz?",
                    "Kök Klasör Hatası",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );
                return cevap == MessageBoxResult.Yes;
            },
            klasorSec: KlasorSecDialog
        );

        if (string.IsNullOrWhiteSpace(kokKlasor))
        {
            Shutdown();
            return;
        }

        try
        {
            var anaPencere = new AramaPenceresi(kokKlasor);
            MainWindow = anaPencere;
            anaPencere.Show();
        }
        catch (Exception ex)
        {
            HataKaydetVeGoster(ex, "Arama penceresi açılamadı");
            Shutdown();
        }
    }

    /// <summary>
    /// Kullanıcıya klasör seçtirir ve geçerli seçimi kullanici.json dosyasına kaydeder.
    /// </summary>
    public static string? KokKlasoruSecVeKaydet()
    {
        string? secilen = KlasorSecDialog();
        if (!string.IsNullOrWhiteSpace(secilen) && KokYapilandirma.KokErisilebilirMi(secilen))
        {
            KokYapilandirma.Kaydet(secilen);
            return secilen;
        }

        return null;
    }

    private static string? KlasorSecDialog()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Ders videoları kök klasörünü seçin",
            Multiselect = false
        };

        bool? sonuc = dialog.ShowDialog();
        return (sonuc == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
            ? dialog.FolderName
            : null;
    }

    private static string? ParseKokArgumani(string[] args)
    {
        return KokYapilandirma.ParseKokArgumani(args);
    }

    private void SetupHataYakali()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                HataKaydetVeGoster(ex, "Kritik uygulama hatası");
            }
        };

        DispatcherUnhandledException += (s, args) =>
        {
            HataKaydetVeGoster(args.Exception, "Arayüz hatası");
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            HataKaydetVeGoster(args.Exception, "Arka plan işlem hatası");
            args.SetObserved();
        };
    }

    private static void HataKaydetVeGoster(Exception ex, string baslik)
    {
        try
        {
            string? dizin = Path.GetDirectoryName(HataLogYolu);
            if (!string.IsNullOrEmpty(dizin))
            {
                Directory.CreateDirectory(dizin);
            }

            string logIcerigi = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{baslik}] {ex}\n\n";
            File.AppendAllText(HataLogYolu, logIcerigi);
        }
        catch
        {
            // Günlük yazma hatasını yut
        }

        try
        {
            MessageBox.Show(
                $"Bir hata oluştu:\n{ex.Message}\n\nDetaylar hata günlüğüne kaydedildi:\n{HataLogYolu}",
                "Hata - Bulgu",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
        catch
        {
            // İleti kutusu gösterilemezse yut
        }
    }
}
