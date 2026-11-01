namespace Portal.Uygulama;

using System.Windows;

/// <summary>Yönetim parolasını sorar. Doğrulama serviste yapılır.</summary>
public partial class YonetimParolaPenceresi : Window
{
    public YonetimParolaPenceresi()
    {
        InitializeComponent();
        Loaded += (_, _) => ParolaKutusu.Focus();
    }

    public string Parola => ParolaKutusu.Password;

    private void Tamam_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
