namespace Portal.Cekirdek.Dizin;

using System.IO;

/// <summary>
/// Depo altındaki işlenmiş belge dosyalarının yollarını çözümleyen yardımcı.
/// </summary>
public static class BelgeYollari
{
    /// <summary>
    /// Belgenin içerik deposundaki klasör yolunu döner.
    /// </summary>
    public static string BelgeKlasoru(string depo, string sha)
    {
        string onEk = sha.Length >= 2 ? sha[..2] : sha;
        return Path.Combine(depo, "belgeler", onEk, sha);
    }

    /// <summary>
    /// Aranabilir PDF dosyası varsa yolunu, yoksa null döner.
    /// </summary>
    public static string? AranabilirPdf(string depo, string? sha)
    {
        if (string.IsNullOrWhiteSpace(sha))
        {
            return null;
        }

        string pdfYolu = Path.Combine(BelgeKlasoru(depo, sha), "aranabilir.pdf");
        return File.Exists(pdfYolu) ? pdfYolu : null;
    }
}
