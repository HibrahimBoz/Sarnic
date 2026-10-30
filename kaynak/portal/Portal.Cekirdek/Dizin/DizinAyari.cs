namespace Portal.Cekirdek.Dizin;

using System.IO;
using System.Text.Json;
using Portal.Cekirdek;

/// <summary>
/// Bulgu dizin klasör yolunu ayar.json veya varsayılan kurallara göre çözen sınıf.
/// </summary>
public static class DizinAyari
{
    /// <summary>
    /// Kök klasör ve isteğe bağlı paket ayar yolu kullanarak etkin dizin klasör yolunu belirler.
    /// paket ayar.json'da "dizin" alanı doluysa o döner, aksi halde Path.Combine(kok, "_bulgu") döner.
    /// </summary>
    public static string Coz(string kok, string? paketAyarYolu = null)
    {
        string varsayilan = Path.Combine(kok ?? string.Empty, "_bulgu");
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
