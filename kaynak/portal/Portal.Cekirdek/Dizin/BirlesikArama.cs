namespace Portal.Cekirdek.Dizin;

using Portal.Cekirdek;

/// <summary>
/// Video transkriptleri, belge içerikleri ve dosya kataloğundan dönen birleşik arama sonuçları.
/// </summary>
public sealed record BirlesikSonuclar(
    IReadOnlyList<AramaSonucu> Videolar,
    IReadOnlyList<IcerikSonucu> Belgeler,
    IReadOnlyList<DosyaSonucu> Dosyalar,
    string? DizinIletisi);

/// <summary>
/// Video, belge ve dosya aramalarını eşzamanlı yürüten birleşik arama motoru.
/// </summary>
public sealed class BirlesikArama
{
    private readonly IAramaMotoru? _video;
    private readonly DizinKaynagi? _dizin;

    public BirlesikArama(IAramaMotoru? video, DizinKaynagi? dizin)
    {
        _video = video;
        _dizin = dizin;
    }

    /// <summary>
    /// Sorguyu ayrıştırıp üç arama türünü paralel olarak koşturur.
    /// </summary>
    public async Task<BirlesikSonuclar> AraAsync(string sorgu, int turBasinaEnFazla = 50, CancellationToken iptal = default)
    {
        iptal.ThrowIfCancellationRequested();

        var dosyaSorgusu = DosyaSorgusu.Ayristir(sorgu);
        bool dizinHazir = _dizin != null && _dizin.Durum == DizinDurumu.Hazir;
        string? dizinIletisi = dizinHazir ? null : _dizin?.Ileti;

        var videoTask = Task.Run<IReadOnlyList<AramaSonucu>>(() =>
        {
            iptal.ThrowIfCancellationRequested();
            if (_video != null && !string.IsNullOrWhiteSpace(dosyaSorgusu.DuzMetin))
            {
                return _video.Ara(dosyaSorgusu.DuzMetin, turBasinaEnFazla);
            }

            return Array.Empty<AramaSonucu>();
        }, iptal);

        var belgeTask = Task.Run<IReadOnlyList<IcerikSonucu>>(() =>
        {
            iptal.ThrowIfCancellationRequested();
            if (dizinHazir)
            {
                string aranan = dosyaSorgusu.Icerik ?? dosyaSorgusu.DuzMetin;
                if (!string.IsNullOrWhiteSpace(aranan))
                {
                    return new IcerikAramaMotoru(_dizin!).Ara(
                        aranan,
                        dosyaSorgusu,
                        turBasinaEnFazla,
                        iptal: iptal);
                }
            }

            return Array.Empty<IcerikSonucu>();
        }, iptal);

        var dosyaTask = Task.Run<IReadOnlyList<DosyaSonucu>>(() =>
        {
            iptal.ThrowIfCancellationRequested();
            if (dizinHazir)
            {
                return new DosyaAramaMotoru(_dizin!).Ara(
                    dosyaSorgusu,
                    null,
                    turBasinaEnFazla,
                    iptal: iptal);
            }

            return Array.Empty<DosyaSonucu>();
        }, iptal);

        await Task.WhenAll(videoTask, belgeTask, dosyaTask);

        return new BirlesikSonuclar(
            await videoTask,
            await belgeTask,
            await dosyaTask,
            dizinIletisi);
    }
}
