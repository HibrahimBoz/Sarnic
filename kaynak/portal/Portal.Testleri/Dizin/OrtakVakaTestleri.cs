namespace Portal.Testleri.Dizin;

using System.Text.Json;
using System.Text.Json.Serialization;
using Portal.Cekirdek;
using Portal.Cekirdek.Dizin;
using Xunit;

/// <summary>
/// Ortak JSON normalizasyon vakalarını doğrulayan testler.
/// </summary>
public class OrtakVakaTestleri
{
    private sealed class Vaka
    {
        [JsonPropertyName("girdi")]
        public string? Girdi { get; set; }

        [JsonPropertyName("beklenen")]
        public string? Beklenen { get; set; }
    }

    public static IEnumerable<object?[]> IcerikNormalizasyonVakalari()
    {
        string dosyaYolu = Path.Combine(AppContext.BaseDirectory, "ortak", "normalizasyon_vakalari.json");
        string json = File.ReadAllText(dosyaYolu);
        var vakalar = JsonSerializer.Deserialize<List<Vaka>>(json)!;
        foreach (var vaka in vakalar)
        {
            yield return new object?[] { vaka.Girdi, vaka.Beklenen };
        }
    }

    public static IEnumerable<object?[]> DosyaNormalizasyonVakalari()
    {
        string dosyaYolu = Path.Combine(AppContext.BaseDirectory, "ortak", "dosya_normalizasyon_vakalari.json");
        string json = File.ReadAllText(dosyaYolu);
        var vakalar = JsonSerializer.Deserialize<List<Vaka>>(json)!;
        foreach (var vaka in vakalar)
        {
            yield return new object?[] { vaka.Girdi, vaka.Beklenen };
        }
    }

    /// <summary>
    /// Ortak vakalarda TrNormalizasyon.Normalle çıktısını doğrular.
    /// </summary>
    [Theory]
    [MemberData(nameof(IcerikNormalizasyonVakalari))]
    public void TrNormalizasyon_OrtakVakalar_BeklenenleEslesir(string? girdi, string? beklenen)
    {
        string sonuc = TrNormalizasyon.Normalle(girdi);
        Assert.Equal(beklenen ?? string.Empty, sonuc);
    }

    /// <summary>
    /// MetinEsleme.Esle çıktısındaki Norm alanının TrNormalizasyon ile aynı olduğunu doğrular.
    /// </summary>
    [Theory]
    [MemberData(nameof(IcerikNormalizasyonVakalari))]
    public void MetinEsleme_OrtakVakalar_NormAlaniTrNormalizasyonIleAynidir(string? girdi, string? beklenen)
    {
        var (norm, ozgun) = MetinEsleme.Esle(girdi);
        Assert.Equal(beklenen ?? string.Empty, norm);
        Assert.Equal(TrNormalizasyon.Normalle(girdi), norm);
        Assert.Equal(norm.Length, ozgun.Length);
    }

    /// <summary>
    /// Ortak dosya normalizasyon vakalarını DosyaNormalizasyon.Normalle ile doğrular.
    /// </summary>
    [Theory]
    [MemberData(nameof(DosyaNormalizasyonVakalari))]
    public void DosyaNormalizasyon_OrtakVakalar_BeklenenleEslesir(string? girdi, string? beklenen)
    {
        string sonuc = DosyaNormalizasyon.Normalle(girdi);
        Assert.Equal(beklenen ?? string.Empty, sonuc);
    }

    /// <summary>
    /// Null ve boş girdilerde normalizasyon işlevlerinin boş metin döndürdüğünü doğrular.
    /// </summary>
    [Fact]
    public void Normalizasyon_NullVeBosGirdiler_BosMetinDondurur()
    {
        Assert.Equal(string.Empty, TrNormalizasyon.Normalle(null));
        Assert.Equal(string.Empty, TrNormalizasyon.Normalle(string.Empty));
        Assert.Equal(string.Empty, DosyaNormalizasyon.Normalle(null));
        Assert.Equal(string.Empty, DosyaNormalizasyon.Normalle(string.Empty));

        var (normNull, ozgunNull) = MetinEsleme.Esle(null);
        Assert.Equal(string.Empty, normNull);
        Assert.Empty(ozgunNull);

        var (normEmpty, ozgunEmpty) = MetinEsleme.Esle(string.Empty);
        Assert.Equal(string.Empty, normEmpty);
        Assert.Empty(ozgunEmpty);
    }
}
