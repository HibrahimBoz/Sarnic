namespace Portal.Cekirdek;

/// <summary>
/// Sarnıç Atölye çıktı klasörlerini özyinelemeli olarak tarayan sınıf.
/// </summary>
public static class Tarayici
{
    private static readonly string[] VideoUzantilari = [".mp4", ".mkv", ".mov", ".avi"];
    private static readonly string[] DersKlasoruEkleri = ["_islenmis", "_videoperisi"];

    /// <summary>
    /// Kök klasör altındaki yeni ve eski çıktı klasörlerini bulur ve ders kayıtlarını listeler.
    /// </summary>
    public static IReadOnlyList<DersKaydi> Tara(string kokKlasor)
    {
        var sonuclar = new List<DersKaydi>();
        if (string.IsNullOrWhiteSpace(kokKlasor) || !Directory.Exists(kokKlasor))
        {
            return sonuclar;
        }

        KlasorDolas(kokKlasor, sonuclar);
        return sonuclar;
    }

    private static void KlasorDolas(string mevcutDizin, List<DersKaydi> sonuclar)
    {
        try
        {
            var dirInfo = new DirectoryInfo(mevcutDizin);
            if (DersKlasoruEki(dirInfo.Name) is not null)
            {
                KlasoruKayitOlarakIsle(dirInfo.FullName, sonuclar);
            }

            string[] altDizinler;
            try
            {
                altDizinler = Directory.GetDirectories(mevcutDizin);
            }
            catch (Exception)
            {
                // Erişilemeyen klasörü atla, taramaya devam et
                return;
            }

            foreach (var alt in altDizinler)
            {
                KlasorDolas(alt, sonuclar);
            }
        }
        catch (Exception)
        {
            // Bozuk veya erişim engeli olan klasörü atla
        }
    }

    private static void KlasoruKayitOlarakIsle(string klasorYolu, List<DersKaydi> sonuclar)
    {
        try
        {
            string[] jsonDosyalari = Directory.GetFiles(klasorYolu, "*.json");
            if (jsonDosyalari.Length == 0)
            {
                return;
            }

            string klasorAdi = Path.GetFileName(klasorYolu);
            string beklenenAd = string.Empty;
            string? ek = DersKlasoruEki(klasorAdi);
            if (ek is not null)
            {
                beklenenAd = klasorAdi.Substring(0, klasorAdi.Length - ek.Length);
            }

            // Klasör adından türetilen JSON dosyası varsa öncelikle onu al
            string beklenenJson = Path.Combine(klasorYolu, beklenenAd + ".json");
            if (!string.IsNullOrEmpty(beklenenAd) && File.Exists(beklenenJson))
            {
                KayitEkle(beklenenAd, klasorYolu, beklenenJson, sonuclar);
                return;
            }

            // Diğer JSON dosyalarını kayda dönüştür
            foreach (var jsonYolu in jsonDosyalari)
            {
                string ad = Path.GetFileNameWithoutExtension(jsonYolu);
                KayitEkle(ad, klasorYolu, jsonYolu, sonuclar);
            }
        }
        catch (Exception)
        {
            // Klasör içi dosya listeleme hatasında atla
        }
    }

    private static string? DersKlasoruEki(string klasorAdi)
    {
        return DersKlasoruEkleri.FirstOrDefault(ek =>
            klasorAdi.EndsWith(ek, StringComparison.OrdinalIgnoreCase));
    }

    private static void KayitEkle(string ad, string klasorYolu, string jsonYolu, List<DersKaydi> sonuclar)
    {
        // Kimlik JsonYolu (normalize tam yol). Ad yalnız görünen ad; aynı adlı dersler birbirini ezmez.
        string klasorTam = DersKaydi.YoluNormalizeEt(klasorYolu);
        string jsonTam = DersKaydi.YoluNormalizeEt(jsonYolu);
        if (string.IsNullOrEmpty(klasorTam))
        {
            klasorTam = klasorYolu;
        }
        if (string.IsNullOrEmpty(jsonTam))
        {
            jsonTam = jsonYolu;
        }

        string islenmisMp4 = Path.Combine(klasorTam, $"{ad}_islenmis.mp4");
        string? videoYolu = null;

        // 1. Öncelik: aynı klasörde <ad>_islenmis.mp4
        if (File.Exists(islenmisMp4))
        {
            videoYolu = islenmisMp4;
        }
        else
        {
            // 2. Öncelik: çıktı klasörünün üstünde <ad>.* video uzantılarından ilki (.mp4 .mkv .mov .avi)
            string? ustKlasor = Path.GetDirectoryName(klasorTam);
            if (!string.IsNullOrEmpty(ustKlasor) && Directory.Exists(ustKlasor))
            {
                foreach (var uzanti in VideoUzantilari)
                {
                    string adayVideo = Path.Combine(ustKlasor, ad + uzanti);
                    if (File.Exists(adayVideo))
                    {
                        videoYolu = adayVideo;
                        break;
                    }
                }
            }
        }

        sonuclar.Add(new DersKaydi(ad, klasorTam, jsonTam, videoYolu));
    }
}
