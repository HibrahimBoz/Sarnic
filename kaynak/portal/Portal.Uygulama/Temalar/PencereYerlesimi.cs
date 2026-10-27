namespace Portal.Uygulama.Temalar;

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Portal.Cekirdek;

/// <summary>Özel kabuklu pencerelerin çalışma alanı, DPI ve tam ekran geçişlerini yönetir.</summary>
public static class PencereYerlesimi
{
    public static readonly DependencyProperty EtkinProperty = DependencyProperty.RegisterAttached(
        "Etkin", typeof(bool), typeof(PencereYerlesimi), new PropertyMetadata(false, EtkinDegisti));
    private static readonly DependencyProperty DurumProperty = DependencyProperty.RegisterAttached(
        "Durum", typeof(PencereDurumu), typeof(PencereYerlesimi));
    private static readonly DependencyPropertyKey TamEkranAnahtari = DependencyProperty.RegisterAttachedReadOnly(
        "TamEkran", typeof(bool), typeof(PencereYerlesimi), new PropertyMetadata(false));
    public static readonly DependencyProperty TamEkranProperty = TamEkranAnahtari.DependencyProperty;
    private static readonly DependencyPropertyKey IcerikPayiAnahtari = DependencyProperty.RegisterAttachedReadOnly(
        "IcerikPayi", typeof(Thickness), typeof(PencereYerlesimi), new PropertyMetadata(new Thickness(0)));
    public static readonly DependencyProperty IcerikPayiProperty = IcerikPayiAnahtari.DependencyProperty;

    public static bool GetEtkin(DependencyObject obj) => (bool)obj.GetValue(EtkinProperty);
    public static void SetEtkin(DependencyObject obj, bool value) => obj.SetValue(EtkinProperty, value);
    public static bool GetTamEkran(DependencyObject obj) => (bool)obj.GetValue(TamEkranProperty);
    public static Thickness GetIcerikPayi(DependencyObject obj) => (Thickness)obj.GetValue(IcerikPayiProperty);

    public static void TamEkraniAyarla(Window pencere, bool tamEkran)
    {
        SetEtkin(pencere, true);
        ((PencereDurumu)pencere.GetValue(DurumProperty)).TamEkraniAyarla(tamEkran);
    }

    private static void EtkinDegisti(DependencyObject obj, DependencyPropertyChangedEventArgs e)
    {
        if (obj is not Window pencere) return;
        if (e.NewValue is true) pencere.SetValue(DurumProperty, new PencereDurumu(pencere));
        else if (pencere.GetValue(DurumProperty) is PencereDurumu durum)
        {
            durum.Ayir();
            pencere.ClearValue(DurumProperty);
        }
    }

    private sealed class PencereDurumu
    {
        private readonly Window _pencere;
        private HwndSource? _kaynak;
        private WindowChrome? _kabuk;
        private Thickness _kenar;
        private YERLESIM _oncekiYerlesim;
        private WindowState _oncekiDurum;
        private WindowStyle _oncekiStil;
        private ResizeMode _oncekiBoyutlandirma;
        private double _oncekiMinGenislik, _oncekiMinYukseklik;
        private bool _kapandi;

        public PencereDurumu(Window pencere)
        {
            _pencere = pencere;
            pencere.SourceInitialized += KaynakHazir;
            pencere.StateChanged += DurumDegisti;
            pencere.SizeChanged += BoyutDegisti;
            pencere.DpiChanged += DpiDegisti;
            pencere.Closed += Kapandi;
            if (PresentationSource.FromVisual(pencere) is HwndSource) KaynakHazir(null, EventArgs.Empty);
        }

        private void KaynakHazir(object? sender, EventArgs e)
        {
            if (_kaynak != null) return;
            _kaynak = HwndSource.FromHwnd(new WindowInteropHelper(_pencere).Handle);
            _kaynak?.AddHook(PencereMesaji);
            _kabuk = WindowChrome.GetWindowChrome(_pencere)?.CloneCurrentValue() as WindowChrome;
            if (_kabuk != null)
            {
                _kenar = _kabuk.ResizeBorderThickness;
                WindowChrome.SetWindowChrome(_pencere, _kabuk);
            }
            DurumDegisti(null, EventArgs.Empty);
        }

        public void TamEkraniAyarla(bool tamEkran)
        {
            if (GetTamEkran(_pencere) == tamEkran) return;
            nint hwnd = new WindowInteropHelper(_pencere).EnsureHandle();
            if (tamEkran)
            {
                if (!MonitorBilgisi(hwnd, out var monitor)) return;
                _oncekiYerlesim = new YERLESIM { Boyut = Marshal.SizeOf<YERLESIM>() };
                if (!GetWindowPlacement(hwnd, ref _oncekiYerlesim)) return;
                _oncekiDurum = _pencere.WindowState;
                _oncekiStil = _pencere.WindowStyle;
                _oncekiBoyutlandirma = _pencere.ResizeMode;
                _oncekiMinGenislik = _pencere.MinWidth;
                _oncekiMinYukseklik = _pencere.MinHeight;
                _pencere.SetValue(TamEkranAnahtari, true);
                _pencere.WindowState = WindowState.Normal;
                WindowChrome.SetWindowChrome(_pencere, null);
                _pencere.WindowStyle = WindowStyle.None;
                _pencere.ResizeMode = ResizeMode.NoResize;
                _pencere.MinWidth = 0;
                _pencere.MinHeight = 0;
                SiniraYerlestir(monitor.Monitor);
            }
            else
            {
                _pencere.SetValue(TamEkranAnahtari, false);
                _pencere.WindowStyle = _oncekiStil;
                _pencere.ResizeMode = _oncekiBoyutlandirma;
                _pencere.MinWidth = _oncekiMinGenislik;
                _pencere.MinHeight = _oncekiMinYukseklik;
                WindowChrome.SetWindowChrome(_pencere, _kabuk);
                _pencere.WindowState = _oncekiDurum;
                // WINDOWPLACEMENT normal boyutu ve monitör çalışma alanına göre konumu birlikte saklar.
                SetWindowPlacement(hwnd, ref _oncekiYerlesim);
            }
            DurumDegisti(null, EventArgs.Empty);
        }

        private nint PencereMesaji(nint hwnd, int mesaj, nint wParam, nint lParam, ref bool islendi)
        {
            if (mesaj == 0x0024 && MonitorBilgisi(hwnd, out var monitor)) // WM_GETMINMAXINFO
            {
                var bilgi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                var sinir = PencereGeometrisi.BuyutmeSiniri(monitor.Monitor.Dikdortgen,
                    monitor.Calisma.Dikdortgen, GetTamEkran(_pencere));
                bilgi.AzamiKonum = new NOKTA { X = sinir.X, Y = sinir.Y };
                bilgi.AzamiBoyut = new NOKTA { X = sinir.Genislik, Y = sinir.Yukseklik };
                var dpi = VisualTreeHelper.GetDpi(_pencere);
                // Sistem alt sınırı korunur: MinWidth/MinHeight 0 ise pencere 0x0'a küçülemez.
                bilgi.AsgariIzBoyutu.X = Math.Min(sinir.Genislik,
                    Math.Max(bilgi.AsgariIzBoyutu.X, (int)Math.Ceiling(_pencere.MinWidth * dpi.DpiScaleX)));
                bilgi.AsgariIzBoyutu.Y = Math.Min(sinir.Yukseklik,
                    Math.Max(bilgi.AsgariIzBoyutu.Y, (int)Math.Ceiling(_pencere.MinHeight * dpi.DpiScaleY)));
                bilgi.AzamiIzBoyutu.X = Math.Max(bilgi.AzamiIzBoyutu.X, sinir.Genislik);
                bilgi.AzamiIzBoyutu.Y = Math.Max(bilgi.AzamiIzBoyutu.Y, sinir.Yukseklik);
                Marshal.StructureToPtr(bilgi, lParam, false);
                islendi = true;
            }
            else if (mesaj is 0x007E or 0x001A) // Ekran veya çalışma alanı değişti.
            {
                YerlesimiYenile();
            }
            return 0;
        }

        private void DurumDegisti(object? sender, EventArgs e)
        {
            if (_kabuk != null)
                _kabuk.ResizeBorderThickness = _pencere.WindowState == WindowState.Maximized
                    ? new Thickness(0) : _kenar;
            PayiGuncelle();
        }

        private void BoyutDegisti(object sender, SizeChangedEventArgs e) => PayiGuncelle();
        private void DpiDegisti(object sender, DpiChangedEventArgs e) => YerlesimiYenile();

        private void YerlesimiYenile()
        {
            _pencere.Dispatcher.InvokeAsync(() =>
            {
                if (_kapandi || _kaynak == null) return;
                if (MonitorBilgisi(_kaynak.Handle, out var monitor))
                {
                    if (GetTamEkran(_pencere)) SiniraYerlestir(monitor.Monitor);
                    else if (_pencere.WindowState == WindowState.Maximized) SiniraYerlestir(monitor.Calisma);
                }
                PayiGuncelle();
            });
        }

        private void SiniraYerlestir(DIKDORTGEN sinir)
        {
            if (_kaynak == null) return;
            SetWindowPos(_kaynak.Handle, 0, sinir.Sol, sinir.Ust, sinir.Sag - sinir.Sol,
                sinir.Alt - sinir.Ust, 0x0004 | 0x0010 | 0x0020 | 0x0200); // Konum değişirken odağı koru.
        }

        private void PayiGuncelle()
        {
            Thickness pay = new(0);
            if (!GetTamEkran(_pencere) && _pencere.WindowState == WindowState.Maximized &&
                _kaynak != null && MonitorBilgisi(_kaynak.Handle, out var monitor) &&
                GetWindowRect(_kaynak.Handle, out var sinir))
            {
                var dpi = VisualTreeHelper.GetDpi(_pencere);
                var tasma = PencereGeometrisi.TasmaPayi(sinir.Dikdortgen, monitor.Calisma.Dikdortgen,
                    dpi.DpiScaleX, dpi.DpiScaleY);
                pay = new Thickness(tasma.Sol, tasma.Ust, tasma.Sag, tasma.Alt);
            }
            _pencere.SetValue(IcerikPayiAnahtari, pay);
        }

        private void Kapandi(object? sender, EventArgs e) => Ayir();

        public void Ayir()
        {
            _kapandi = true;
            _kaynak?.RemoveHook(PencereMesaji);
            _pencere.SourceInitialized -= KaynakHazir;
            _pencere.StateChanged -= DurumDegisti;
            _pencere.SizeChanged -= BoyutDegisti;
            _pencere.DpiChanged -= DpiDegisti;
            _pencere.Closed -= Kapandi;
        }
    }

    private static bool MonitorBilgisi(nint hwnd, out MONITORINFO bilgi)
    {
        bilgi = new MONITORINFO { Boyut = Marshal.SizeOf<MONITORINFO>() };
        return GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref bilgi);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NOKTA { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct DIKDORTGEN
    {
        public int Sol, Ust, Sag, Alt;
        public readonly PikselDikdortgen Dikdortgen => new(Sol, Ust, Sag, Alt);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public NOKTA Ayrilmis, AzamiBoyut, AzamiKonum, AsgariIzBoyutu, AzamiIzBoyutu;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int Boyut;
        public DIKDORTGEN Monitor, Calisma;
        public int Bayraklar;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct YERLESIM
    {
        public int Boyut, Bayraklar, Gosterim;
        public NOKTA AsgariKonum, AzamiKonum;
        public DIKDORTGEN NormalKonum;
    }

    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(nint monitor, ref MONITORINFO bilgi);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out DIKDORTGEN sinir);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(nint hwnd, ref YERLESIM yerlesim);
    [DllImport("user32.dll")] private static extern bool SetWindowPlacement(nint hwnd, ref YERLESIM yerlesim);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint sonra, int x, int y, int genislik, int yukseklik, int flags);
}
