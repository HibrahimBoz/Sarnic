namespace Portal.Cekirdek;

/// <summary>
/// Transkript içindeki tek bir zaman dilimi ve metnini temsil eder.
/// </summary>
public record Segment(int No, double Bas, double Son, string Metin, string? Ceviri = null)
{
    public static Segment FromOnbellek(OnbellekSegment os) => new(os.No, os.Bas, os.Son, os.Metin, os.Ceviri);
    public OnbellekSegment ToOnbellek() => new(No, Bas, Son, Metin, Ceviri);
}
