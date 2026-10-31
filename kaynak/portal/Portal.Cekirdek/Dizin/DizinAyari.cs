namespace Portal.Cekirdek.Dizin;

using System.IO;
using System.Text.Json;
using Portal.Cekirdek;

/// <summary>
/// Sarnıç dizin klasör yolunu ayar.json veya varsayılan kurallara göre çözen sınıf.
/// </summary>
public static class DizinAyari
{
    /// <summary>
    /// Kök klasör ve isteğe bağlı paket ayar yolu kullanarak etkin dizin klasör yolunu belirler.
    /// paket ayar.json'da "dizin" alanı doluysa o döner.
    /// Aksi halde seçim sırası: "_sarnic" varsa o, yoksa "_veridedektifi" varsa o, yoksa "_bulgu" varsa o, hiçbiri yoksa "_sarnic".
    /// </summary>
    public static string Coz(string kok, string? paketAyarYolu = null)
    {
        string kokDizin = kok ?? string.Empty;
        string sarnicYolu = Path.Combine(kokDizin, "_sarnic");
        string veriDedektifiYolu = Path.Combine(kokDizin, "_veridedektifi");
        string bulguYolu = Path.Combine(kokDizin, "_bulgu");

        bool sarnicVar = false;
        bool veriDedektifiVar = false;
        bool bulguVar = false;
        try
        {
            sarnicVar = Directory.Exists(sarnicYolu);
            veriDedektifiVar = Directory.Exists(veriDedektifiYolu);
            bulguVar = Directory.Exists(bulguYolu);
        }
        catch
        {
        }

        string varsayilan = sarnicVar
            ? sarnicYolu
            : veriDedektifiVar
                ? veriDedektifiYolu
                : bulguVar
                    ? bulguYolu
                    : sarnicYolu;

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
