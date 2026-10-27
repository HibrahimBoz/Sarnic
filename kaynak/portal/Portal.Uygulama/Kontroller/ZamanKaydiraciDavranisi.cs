namespace Portal.Uygulama.Kontroller;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Portal.Cekirdek;

/// <summary>İze basma ve basılı sürüklemeyi her iki oynatıcıda aynı şekilde işler.</summary>
public sealed class ZamanKaydiraciDavranisi
{
    private readonly Slider _kaydirac;
    private readonly Func<double> _sure;
    private readonly Action<double> _ara;
    private double _tutamacOfseti;

    public bool Surukleniyor { get; private set; }

    public ZamanKaydiraciDavranisi(Slider kaydirac, Func<double> sure, Action<double> ara)
    {
        _kaydirac = kaydirac;
        _sure = sure;
        _ara = ara;
        kaydirac.PreviewMouseLeftButtonDown += Basildi;
        kaydirac.PreviewMouseMove += Tasindi;
        kaydirac.PreviewMouseLeftButtonUp += Birakildi;
        kaydirac.LostMouseCapture += (_, _) => Surukleniyor = false;
        kaydirac.Unloaded += (_, _) => Bitir();
        kaydirac.PreviewKeyDown += TusBasildi;
    }

    private Track? Iz => _kaydirac.Template?.FindName("PART_Track", _kaydirac) as Track;

    private double? KonumdanZaman(MouseEventArgs e)
    {
        if (Iz is not { } iz) return null;
        return KaydiracHesabi.ZamanHesapla(e.GetPosition(iz).X - _tutamacOfseti,
            iz.ActualWidth, iz.Thumb?.ActualWidth ?? 0, _sure(), iz.IsDirectionReversed);
    }

    private void Basildi(object sender, MouseButtonEventArgs e)
    {
        // RepeatButton komutları temadan bağımsız olarak devre dışı kalır.
        e.Handled = true;
        _tutamacOfseti = 0;
        if (Iz?.Thumb is { IsMouseOver: true } tutamac)
        {
            _tutamacOfseti = e.GetPosition(tutamac).X - tutamac.ActualWidth / 2;
        }
        if (KonumdanZaman(e) is not double zaman) return;

        _kaydirac.Focus();
        Surukleniyor = _kaydirac.CaptureMouse();
        Ara(zaman);
    }

    private void Tasindi(object sender, MouseEventArgs e)
    {
        if (!Surukleniyor) return;
        if (e.LeftButton != MouseButtonState.Pressed) { Bitir(); return; }
        if (KonumdanZaman(e) is double zaman) Ara(zaman);
        e.Handled = true;
    }

    private void Birakildi(object sender, MouseButtonEventArgs e)
    {
        if (!Surukleniyor) return;
        if (KonumdanZaman(e) is double zaman) Ara(zaman);
        Bitir();
        e.Handled = true;
    }

    private void Bitir()
    {
        Surukleniyor = false;
        if (_kaydirac.IsMouseCaptured) _kaydirac.ReleaseMouseCapture();
    }

    private void TusBasildi(object sender, KeyEventArgs e)
    {
        double sure = _sure();
        double? zaman = e.Key switch
        {
            Key.Left or Key.Down => _kaydirac.Value - _kaydirac.SmallChange,
            Key.Right or Key.Up => _kaydirac.Value + _kaydirac.SmallChange,
            Key.PageDown => _kaydirac.Value - _kaydirac.LargeChange,
            Key.PageUp => _kaydirac.Value + _kaydirac.LargeChange,
            Key.Home => 0,
            Key.End => sure,
            _ => null
        };
        if (zaman is null) return;
        e.Handled = true;
        if (double.IsFinite(sure) && sure > 0) Ara(Math.Clamp(zaman.Value, 0, sure));
    }

    private void Ara(double zaman)
    {
        _kaydirac.SetCurrentValue(Slider.ValueProperty, zaman);
        _ara(zaman);
    }
}
