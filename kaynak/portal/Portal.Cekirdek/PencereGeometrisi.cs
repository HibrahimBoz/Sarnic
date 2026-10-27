namespace Portal.Cekirdek;

public readonly record struct PikselDikdortgen(int Sol, int Ust, int Sag, int Alt)
{
    public int Genislik => Sag - Sol;
    public int Yukseklik => Alt - Ust;
}

/// <summary>Win32 monitör koordinatlarını DPI'den bağımsız fiziksel piksel olarak hesaplar.</summary>
public static class PencereGeometrisi
{
    public static (int X, int Y, int Genislik, int Yukseklik) BuyutmeSiniri(
        PikselDikdortgen monitor, PikselDikdortgen calismaAlani, bool tamEkran)
    {
        var alan = tamEkran ? monitor : calismaAlani;
        return (alan.Sol - monitor.Sol, alan.Ust - monitor.Ust, alan.Genislik, alan.Yukseklik);
    }

    public static (double Sol, double Ust, double Sag, double Alt) TasmaPayi(
        PikselDikdortgen pencere, PikselDikdortgen alan, double olcekX, double olcekY)
    {
        if (!double.IsFinite(olcekX) || olcekX <= 0 || !double.IsFinite(olcekY) || olcekY <= 0)
            throw new ArgumentOutOfRangeException(nameof(olcekX));

        return (Math.Max(0, alan.Sol - pencere.Sol) / olcekX,
            Math.Max(0, alan.Ust - pencere.Ust) / olcekY,
            Math.Max(0, pencere.Sag - alan.Sag) / olcekX,
            Math.Max(0, pencere.Alt - alan.Alt) / olcekY);
    }
}
