namespace Portal.Cekirdek.Dizin;

using System.IO;
using System.Text.Json;
using Portal.Cekirdek;

/// <summary>
/// Veri Dedektifi dizin klasör yolunu ayar.json veya varsayılan kurallara göre çözen sınıf.
/// </summary>
public static class DizinAyari
{
    /// <summary>
    /// Kök klasör ve isteğe bağlı paket ayar yolu kullanarak etkin dizin klasör yolunu belirler.
    /// paket ayar.json'da "dizin" alanı doluysa o döner, aksi halde Path.Combine(kok, "_veridedektifi") döner.
    /// "_veridedektifi" yoksa ve "_bulgu" varsa eski kurulum klasörü kullanılır.
    /// </summary>
    public static string Coz(string kok, string? paketAyarYolu = null)
    {
        string kokDizin = kok ?? string.Empty;
        string yeniVarsayilan = Path.Combine(kokDizin, "_veridedektifi");
        string eskiVarsayilan = Path.Combine(kokDizin, "_bulgu");

        bool yeniVar = false;
        bool eskiVar = false;
        try
        {
            yeniVar = Directory.Exists(yeniVarsayilan);
            eskiVar = Directory.Exists(eskiVarsayilan);
        }
        catch
        {
        }

        string varsayilan = !yeniVar && eskiVar ? eskiVarsayilan : yeniVarsayilan;
        string ayarYolu = string.IsNullOrWhiteSpace(paketAyarYolu)
            ? KokYapilandirma.VarsayilanPaketAyarYolu
            : paketAyarYolu;

        if (!File.Exists(ayarYolu))
        {
            return varsayilan;
        }

        try
        {
            string json = File.ReadAllText(ayarYolu);
            if (string.IsNullOrWhiteSpace(json))
            {
                return varsayilan;
            }

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("dizin", out var dizinEleman) &&
                dizinEleman.ValueKind == JsonValueKind.String)
            {
                string? deger = dizinEleman.GetString();
                if (!string.IsNullOrWhiteSpace(deger))
                {
                    return deger;
                }
            }
        }
        catch
        {
            // Okuma hatası → varsayılan
        }

        return varsayilan;
    }
}
