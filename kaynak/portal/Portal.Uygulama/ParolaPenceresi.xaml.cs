namespace Portal.Uygulama;

using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Portal.Cekirdek;

/// <summary>
/// Düzenleme moduna ilk girişte sorulan parola doğrulama penceresi.
/// </summary>
public partial class ParolaPenceresi : Window
{
    public const string VarsayilanParola = "ehpm";

    private readonly string? _kokKlasor;
    private readonly ParolaGecikmeSayaci? _sayac;

    public string ParolaMetni => ParolaKutusu.Password;

    public ParolaPenceresi()
        : this(null, null)
    {
    }

    public ParolaPenceresi(string? kokKlasor, ParolaGecikmeSayaci? sayac)
    {
        _kokKlasor = string.IsNullOrWhiteSpace(kokKlasor) ? Directory.GetCurrentDirectory() : kokKlasor;
        _sayac = sayac;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (Owner == null)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            ParolaKutusu.Focus();
        };
    }

    private void Tamam_Click(object sender, RoutedEventArgs e)
    {
        if (_sayac != null && _sayac.KilitliMi())
        {
            HataMetni.Text = $"Çok fazla hatalı deneme yapıldı. Lütfen {_sayac.KalanBeklemeSaniyesi()} saniye bekleyin.";
            return;
        }

        string girilen = ParolaKutusu.Password;
        bool gecerli = ParolaDogrulaKok(_kokKlasor ?? Directory.GetCurrentDirectory(), girilen);

        if (!gecerli)
        {
            _sayac?.HataliGiris();
            if (_sayac != null && _sayac.KilitliMi())
            {
                HataMetni.Text = $"Parola hatalı! 5 kez yanlış girildi, lütfen {_sayac.KilitSuresiSaniye} saniye bekleyin.";
            }
            else
            {
                int kalan = _sayac != null ? _sayac.AzamiHataliDeneme - _sayac.HataliDenemeSayisi : 0;
                HataMetni.Text = kalan > 0
                    ? $"Parola hatalı! Kalan deneme hakkı: {kalan}"
                    : "Parola hatalı!";
            }
            ParolaKutusu.SelectAll();
            return;
        }

        _sayac?.BasariliGiris();
        DialogResult = true;
    }

    private void Iptal_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ParolaKutusu_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Tamam_Click(sender, e);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Kök klasör ve üst klasörlerinde .portal/parola.json dosyasını doğrular.
    /// </summary>
    public static bool ParolaDogrulaKok(string kokKlasor, string girilenParola) =>
        DuzenlemeParolasi.DogrulaKok(kokKlasor, girilenParola, VarsayilanParola);

    /// <summary>
    /// Kök klasörde veya üst dizinlerinde .portal/parola.json dosyasını arar.
    /// </summary>
    public static string? ParolaDosyasiBul(string? kokKlasor) => DuzenlemeParolasi.DosyaBul(kokKlasor);

    /// <summary>
    /// Oturum için parola doğrulaması yapar. Yanlış girişte kullanıcıyı bilgilendirir.
    /// 5 yanlışta 30 saniye kilit uygular.
    /// </summary>
    public static bool DogrulaVeIzinVer(Window? sahip, string? kokKlasor, ref bool oturumDogrulandi, ParolaGecikmeSayaci sayac)
    {
        if (oturumDogrulandi)
        {
            return true;
        }

        if (sayac.KilitliMi())
        {
            string kilitMesaji = $"Çok fazla hatalı deneme yapıldı. Lütfen {sayac.KalanBeklemeSaniyesi()} saniye bekleyin.";
            if (sahip != null)
            {
                MessageBox.Show(
                    sahip,
                    kilitMesaji,
                    "Bekleyin",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
            else
            {
                MessageBox.Show(
                    kilitMesaji,
                    "Bekleyin",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
            return false;
        }

        var dlg = new ParolaPenceresi(kokKlasor, sayac);
        if (sahip != null)
        {
            dlg.Owner = sahip;
        }

        bool? sonuc = dlg.ShowDialog();
        if (sonuc == true)
        {
            oturumDogrulandi = true;
            return true;
        }

        return false;
    }
}
