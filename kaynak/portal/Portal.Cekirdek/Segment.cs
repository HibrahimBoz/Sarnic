namespace Portal.Cekirdek;

/// <summary>
/// Transkript içindeki tek bir zaman dilimi ve metnini temsil eder.
/// </summary>
public record Segment(int No, double Bas, double Son, string Metin, string? Ceviri = null);
