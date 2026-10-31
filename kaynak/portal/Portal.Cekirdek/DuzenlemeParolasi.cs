namespace Portal.Cekirdek;

/// <summary>Düzenleme girişinin dosya arama ve doğrulama mantığı.</summary>
public static class DuzenlemeParolasi
{
    public static bool DogrulaKok(string kokKlasor, string? girilenParola, string varsayilanParola)
    {
        if (string.IsNullOrEmpty(girilenParola)) return false;
        if (string.Equals(girilenParola, varsayilanParola, StringComparison.Ordinal)) return true;

        string? dosyaYolu = DosyaBul(kokKlasor);
        if (dosyaYolu == null) return false;

        string? klasor = Path.GetDirectoryName(dosyaYolu);
        if (klasor != null && Path.GetFileName(klasor).Equals(".portal", StringComparison.OrdinalIgnoreCase))
        {
            string? ust = Path.GetDirectoryName(klasor);
            if (ust != null && Parola.Dogrula(ust, girilenParola)) return true;
        }

        return Parola.Dogrula(kokKlasor, girilenParola);
    }

    public static string? DosyaBul(string? kokKlasor)
    {
        if (string.IsNullOrWhiteSpace(kokKlasor)) kokKlasor = Directory.GetCurrentDirectory();
        string? mevcut = Path.GetFullPath(kokKlasor);
        while (!string.IsNullOrEmpty(mevcut))
        {
            string aday = Path.Combine(mevcut, ".portal", "parola.json");
            if (File.Exists(aday)) return aday;

            var ust = Directory.GetParent(mevcut);
            if (ust == null || ust.FullName == mevcut) break;
            mevcut = ust.FullName;
        }

        string simdikiPortal = Path.Combine(Directory.GetCurrentDirectory(), ".portal", "parola.json");
        return File.Exists(simdikiPortal) ? simdikiPortal : null;
    }
}
