namespace Portal.Cekirdek;

using System;
using System.IO;
using System.Text;
using System.Text.Json;

/// <summary>
/// Sarnıç ürün adı ve kurum ön eki yardımcı sınıfı.
/// </summary>
public static class UrunAdi
{
    private static readonly JsonDocumentOptions DokumanAyar = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private static readonly Lazy<string> _etkin = new(() => Baslik(KurumOku()));

    public const string Ad = "Sarnıç";

    /// <summary>
    /// Kurum bilgisi varsa başına ekleyerek başlık üretir.
    /// Kurum boş veya boşluksa "Sarnıç", değilse "{kurum.Trim()} Sarnıç".
    /// </summary>
    public static string Baslik(string? kurum)
    {
        return string.IsNullOrWhiteSpace(kurum)
            ? Ad
            : $"{kurum.Trim()} {Ad}";
    }

    /// <summary>
    /// Paket ayar.json içindeki "kurum" metin alanını okur.
    /// Dosya yok, bozuk JSON, alan yok veya metin değilse null döner. Asla istisna fırlatmaz.
    /// </summary>
    public static string? KurumOku(string? paketAyarYolu = null)
    {
        string dosyaYolu = string.IsNullOrWhiteSpace(paketAyarYolu)
            ? KokYapilandirma.VarsayilanPaketAyarYolu
            : paketAyarYolu;

        if (string.IsNullOrWhiteSpace(dosyaYolu) || !File.Exists(dosyaYolu))
        {
            return null;
        }

        try
        {
            string metin = File.ReadAllText(dosyaYolu, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(metin))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(metin, DokumanAyar);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name, "kurum", StringComparison.OrdinalIgnoreCase))
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        return prop.Value.GetString();
                    }
                    return null;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Uygulama ömründe bir kez hesaplanan etkin ürün başlığı.
    /// </summary>
    public static string Etkin => _etkin.Value;
}
