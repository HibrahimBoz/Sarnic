namespace Portal.Cekirdek;

/// <summary>LibVLC başlatma seçeneklerini birleştiren saf mantık (test edilebilir).</summary>
public static class VlcSecenekleri
{
    public static readonly string[] Varsayilan =
    [
        "--avcodec-hw=any",
        "--no-video-title-show",
        "--no-snapshot-preview",
        "--no-osd",
        "--no-stats",
        "--file-caching=300",
        "--network-caching=500",
        "--drop-late-frames",
        "--skip-frames",
        "--sub-autodetect-file"
    ];

    public static string[] Birlestir(IEnumerable<string>? ek)
    {
        return Varsayilan
            .Concat(ek ?? Array.Empty<string>())
            .Where(secenek => !string.IsNullOrWhiteSpace(secenek))
            .Select(secenek => secenek.Trim())
            .GroupBy(Anahtar, StringComparer.Ordinal)
            .Select(grup => grup.Last())
            .ToArray();
    }

    private static string Anahtar(string secenek)
    {
        string ad = secenek.Split('=', 2)[0];
        return ad.StartsWith("--no-", StringComparison.Ordinal) ? "--" + ad[5..] : ad;
    }
}
