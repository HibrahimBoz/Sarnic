namespace Portal.Cekirdek;

/// <summary>Yatay izin tutamaç merkezleri arasındaki konumunu video zamanına çevirir.</summary>
public static class KaydiracHesabi
{
    public static double? ZamanHesapla(double konum, double izGenisligi, double tutamacGenisligi,
        double toplamSure, bool ters = false)
    {
        if (!double.IsFinite(konum) || !double.IsFinite(izGenisligi) ||
            !double.IsFinite(tutamacGenisligi) || tutamacGenisligi < 0 ||
            izGenisligi <= tutamacGenisligi || !double.IsFinite(toplamSure) || toplamSure <= 0)
        {
            return null;
        }

        double oran = Math.Clamp((konum - tutamacGenisligi / 2) / (izGenisligi - tutamacGenisligi), 0, 1);
        return (ters ? 1 - oran : oran) * toplamSure;
    }
}
