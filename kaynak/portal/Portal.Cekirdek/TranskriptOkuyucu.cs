namespace Portal.Cekirdek;

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// JSON v2 ve eski SRT biçimindeki transkript dosyalarını okur.
/// </summary>
public static class TranskriptOkuyucu
{
    private static readonly Regex ZamanRegex = new(@"(\d+):(\d{1,2}):(\d{1,2})[,.](\d{1,3})", RegexOptions.Compiled);

    /// <summary>
    /// Belirtilen JSON dosyasından transkripti okur. v2 biçimi yoksa SRT yedeğine bakar.
    /// </summary>
    public static Transkript Oku(string jsonYolu)
    {
        if (string.IsNullOrWhiteSpace(jsonYolu))
        {
            throw new ArgumentException("JSON dosya yolu boş olamaz.", nameof(jsonYolu));
        }

        if (!File.Exists(jsonYolu))
        {
            throw new FileNotFoundException("Transkript JSON dosyası bulunamadı.", jsonYolu);
        }

        string jsonMetni;
        try
        {
            jsonMetni = File.ReadAllText(jsonYolu, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is not FileNotFoundException)
        {
            throw new InvalidDataException("JSON dosyası okunamadı.", ex);
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(jsonMetni);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("JSON biçimi geçersiz veya bozuk.", ex);
        }

        using (doc)
        {
            var kok = doc.RootElement;
            if (kok.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("JSON kök elemanı bir nesne olmalıdır.");
            }

            string dosyaAdi = Path.GetFileNameWithoutExtension(jsonYolu);
            string ad = dosyaAdi;
            if (TryGetProp(kok, "ad", out var adElem) && adElem.ValueKind == JsonValueKind.String)
            {
                string? adDegeri = adElem.GetString();
                if (!string.IsNullOrWhiteSpace(adDegeri))
                {
                    ad = adDegeri;
                }
            }

            // Sema sürümü ve segmentler kontrolü
            bool segmentlerAlaniVar = TryGetProp(kok, "segmentler", out var segElem);
            bool semaAlaniVar = TryGetProp(kok, "sema_surumu", out var semaElem);

            if (segmentlerAlaniVar || semaAlaniVar)
            {
                if (!semaAlaniVar || semaElem.ValueKind != JsonValueKind.Number || !semaElem.TryGetInt32(out int sema) || sema != 2)
                {
                    throw new InvalidDataException("Geçersiz şema sürümü: Şema sürümü 2 olmalıdır.");
                }

                if (!segmentlerAlaniVar || segElem.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidDataException("Segmentler alanı geçerli bir dizi olmalıdır.");
                }

                return JsonV2TranskriptOlustur(kok, segElem, ad, jsonYolu);
            }

            // Eski biçim (segmentler yok): aynı klasördeki <ad>.srt dosyasından oku
            return SrtTranskriptOlustur(kok, dosyaAdi, ad, jsonYolu);
        }
    }

    private static Transkript JsonV2TranskriptOlustur(JsonElement kok, JsonElement segElem, string ad, string jsonYolu)
    {
        var segmentler = new List<Segment>();

        foreach (var item in segElem.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Segment bir nesne olmalıdır.");
            }

            if (!TryGetProp(item, "no", out var noElem) || noElem.ValueKind != JsonValueKind.Number || !noElem.TryGetInt32(out int no))
            {
                throw new InvalidDataException("Segment 'no' alanı eksik veya geçerli bir tamsayı değil.");
            }

            if (!TryGetProp(item, "bas", out var basElem) || basElem.ValueKind != JsonValueKind.Number || !basElem.TryGetDouble(out double bas) || !double.IsFinite(bas))
            {
                throw new InvalidDataException("Segment 'bas' alanı eksik veya geçerli bir sayı değil.");
            }

            if (!TryGetProp(item, "son", out var sonElem) || sonElem.ValueKind != JsonValueKind.Number || !sonElem.TryGetDouble(out double son) || !double.IsFinite(son))
            {
                throw new InvalidDataException("Segment 'son' alanı eksik veya geçerli bir sayı değil.");
            }

            if (bas < 0 || son < 0)
            {
                throw new InvalidDataException("Segment zamanları negatif olamaz.");
            }

            if (son < bas)
            {
                throw new InvalidDataException("Segment 'son' zamanı 'bas' zamanından küçük olamaz.");
            }

            if (!TryGetProp(item, "metin", out var metinElem) || metinElem.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("Segment 'metin' alanı eksik veya geçerli bir metin değil.");
            }

            string? metin = metinElem.GetString();
            if (metin is null)
            {
                throw new InvalidDataException("Segment 'metin' alanı null olamaz.");
            }

            // Çeviri isteğe bağlıdır; metin değilse yok sayılır (döküm yine okunur).
            string? ceviri = TryGetProp(item, "ceviri", out var ceviriElem) && ceviriElem.ValueKind == JsonValueKind.String
                ? ceviriElem.GetString()
                : null;

            segmentler.Add(new Segment(no, bas, son, metin, ceviri));
        }

        string? kaynakOzet = null;
        if (TryGetProp(kok, "kaynak_ozet", out var ozetElem))
        {
            if (ozetElem.ValueKind == JsonValueKind.String)
            {
                kaynakOzet = ozetElem.GetString();
            }
            else if (ozetElem.ValueKind != JsonValueKind.Null)
            {
                throw new InvalidDataException("'kaynak_ozet' alanı metin olmalıdır.");
            }
        }

        double sureSn = 0;
        if (TryGetProp(kok, "sure_sn", out var sureElem))
        {
            if (sureElem.ValueKind != JsonValueKind.Null && (sureElem.ValueKind != JsonValueKind.Number || !sureElem.TryGetDouble(out sureSn) || !double.IsFinite(sureSn) || sureSn < 0))
            {
                throw new InvalidDataException("'sure_sn' alanı geçerli bir sayı olmalıdır.");
            }
        }

        if (sureSn <= 0 && segmentler.Count > 0)
        {
            sureSn = segmentler[^1].Son;
        }

        return new Transkript
        {
            Ad = ad,
            Segmentler = segmentler,
            EskiBicim = false,
            KaynakOzet = kaynakOzet,
            SureSn = sureSn,
            JsonYolu = jsonYolu,
            CeviriDil = TryGetProp(kok, "ceviri_dil", out var dilElem) && dilElem.ValueKind == JsonValueKind.String ? dilElem.GetString() : null,
            CeviriModel = TryGetProp(kok, "ceviri_model", out var modelElem) && modelElem.ValueKind == JsonValueKind.String ? modelElem.GetString() : null
        };
    }

    private static Transkript SrtTranskriptOlustur(JsonElement kok, string dosyaAdi, string ad, string jsonYolu)
    {
        string klasor = Path.GetDirectoryName(Path.GetFullPath(jsonYolu)) ?? string.Empty;

        // Öncelik dosya adı ile eşleşen srt, ardından ad ile eşleşen srt
        string srtYolu = Path.Combine(klasor, dosyaAdi + ".srt");
        if (!File.Exists(srtYolu) && !string.Equals(dosyaAdi, ad, StringComparison.OrdinalIgnoreCase))
        {
            srtYolu = Path.Combine(klasor, ad + ".srt");
        }

        if (!File.Exists(srtYolu))
        {
            throw new InvalidDataException($"Transkript segmentleri bulunamadı ve SRT dosyası mevcut değil: {srtYolu}");
        }

        string srtMetni;
        try
        {
            srtMetni = File.ReadAllText(srtYolu, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("SRT dosyası okunamadı.", ex);
        }

        var segmentler = SrtCoz(srtMetni);
        if (segmentler.Count == 0)
        {
            throw new InvalidDataException($"SRT dosyasında geçerli segment bulunamadı: {srtYolu}");
        }

        string? kaynakOzet = null;
        if (TryGetProp(kok, "kaynak_ozet", out var ozetElem) && ozetElem.ValueKind == JsonValueKind.String)
        {
            kaynakOzet = ozetElem.GetString();
        }

        double sureSn = 0;
        if (TryGetProp(kok, "sure_sn", out var sureElem))
        {
            if (sureElem.ValueKind != JsonValueKind.Null && (sureElem.ValueKind != JsonValueKind.Number || !sureElem.TryGetDouble(out sureSn) || !double.IsFinite(sureSn) || sureSn < 0))
            {
                throw new InvalidDataException("'sure_sn' alanı geçerli bir sayı olmalıdır.");
            }
        }

        if (sureSn <= 0 && segmentler.Count > 0)
        {
            sureSn = segmentler[^1].Son;
        }

        return new Transkript
        {
            Ad = ad,
            Segmentler = segmentler,
            EskiBicim = true,
            KaynakOzet = kaynakOzet,
            SureSn = sureSn,
            JsonYolu = jsonYolu,
            CeviriDil = TryGetProp(kok, "ceviri_dil", out var dilElem) && dilElem.ValueKind == JsonValueKind.String ? dilElem.GetString() : null,
            CeviriModel = TryGetProp(kok, "ceviri_model", out var modelElem) && modelElem.ValueKind == JsonValueKind.String ? modelElem.GetString() : null
        };
    }

    internal static List<Segment> SrtCoz(string srtIcerik)
    {
        if (string.IsNullOrWhiteSpace(srtIcerik))
        {
            return new List<Segment>();
        }

        if (srtIcerik.StartsWith("\uFEFF", StringComparison.Ordinal))
        {
            srtIcerik = srtIcerik.Substring(1);
        }

        var segmentler = new List<Segment>();
        var satirlar = srtIcerik.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        int index = 0;
        int siraNo = 1;

        while (index < satirlar.Length)
        {
            while (index < satirlar.Length && string.IsNullOrWhiteSpace(satirlar[index]))
            {
                index++;
            }

            if (index >= satirlar.Length)
            {
                break;
            }

            int cueNo = siraNo;
            string suankiSatir = satirlar[index].Trim();

            if (int.TryParse(suankiSatir, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ayrilanNo) &&
                index + 1 < satirlar.Length &&
                satirlar[index + 1].Contains("-->"))
            {
                cueNo = ayrilanNo;
                index++;
            }
            else if (!suankiSatir.Contains("-->"))
            {
                index++;
                continue;
            }

            if (index >= satirlar.Length)
            {
                break;
            }

            string zamanSatiri = satirlar[index].Trim();
            index++;

            if (!ZamanAraligiCoz(zamanSatiri, out double bas, out double son))
            {
                continue;
            }

            var metinSatirlari = new List<string>();
            while (index < satirlar.Length && !string.IsNullOrWhiteSpace(satirlar[index]))
            {
                metinSatirlari.Add(satirlar[index].Trim());
                index++;
            }

            string metin = string.Join("\n", metinSatirlari);
            segmentler.Add(new Segment(cueNo, bas, son, metin));
            siraNo++;
        }

        return segmentler;
    }

    private static bool ZamanAraligiCoz(string zamanSatiri, out double bas, out double son)
    {
        bas = 0;
        son = 0;
        var okParcalari = zamanSatiri.Split(new[] { "-->" }, StringSplitOptions.None);
        if (okParcalari.Length < 2)
        {
            return false;
        }

        return ZamanCoz(okParcalari[0].Trim(), out bas) && ZamanCoz(okParcalari[1].Trim(), out son);
    }

    private static bool ZamanCoz(string zamanMetni, out double saniye)
    {
        saniye = 0;
        var match = ZamanRegex.Match(zamanMetni);
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int saat) ||
            !int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dakika) ||
            !int.TryParse(match.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sn))
        {
            return false;
        }

        string msStr = match.Groups[4].Value.PadRight(3, '0');
        if (msStr.Length > 3)
        {
            msStr = msStr.Substring(0, 3);
        }

        if (!int.TryParse(msStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ms))
        {
            return false;
        }

        saniye = Math.Round(saat * 3600.0 + dakika * 60.0 + sn + ms / 1000.0, 3);
        return true;
    }

    private static bool TryGetProp(JsonElement elem, string name, out JsonElement value)
    {
        if (elem.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var prop in elem.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prop.Name.Replace("_", ""), name.Replace("_", ""), StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
}
