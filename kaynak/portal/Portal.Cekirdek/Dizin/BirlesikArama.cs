namespace Portal.Cekirdek.Dizin;

using System.Collections.Concurrent;
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
    /// Tek bir arama motorunun hata fırlatması diğer sonuçların kaybolmasına yol açmaz.
    /// </summary>
    public async Task<BirlesikSonuclar> AraAsync(
        string sorgu,
        int turBasinaEnFazla = 50,
        CancellationToken iptal = default,
        Siralama? dosyaSiralama = null)
    {
        iptal.ThrowIfCancellationRequested();

        var dosyaSorgusu = DosyaSorgusu.Ayristir(sorgu);
        bool dizinHazir = _dizin != null && _dizin.Durum == DizinDurumu.Hazir;
        string? dizinIletisi = dizinHazir ? null : _dizin?.Ileti;
        var hatalar = new ConcurrentBag<string>();

        var videoTask = Task.Run<IReadOnlyList<AramaSonucu>>(() =>
        {
            iptal.ThrowIfCancellationRequested();
            try
            {
                if (_video != null && !string.IsNullOrWhiteSpace(dosyaSorgusu.DuzMetin))
                {
                    return _video.Ara(dosyaSorgusu.DuzMetin, turBasinaEnFazla);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Video arama hatası sessizce yutulur
            }

            return Array.Empty<AramaSonucu>();
        }, iptal);

        var belgeTask = Task.Run<IReadOnlyList<IcerikSonucu>>(() =>
        {
            iptal.ThrowIfCancellationRequested();
            try
            {
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
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                hatalar.Add($"Belge arama hatası: {ex.Message}");
            }

            return Array.Empty<IcerikSonucu>();
        }, iptal);

        var dosyaTask = Task.Run<IReadOnlyList<DosyaSonucu>>(() =>
        {
            iptal.ThrowIfCancellationRequested();
            try
            {
                if (dizinHazir)
                {
                    return new DosyaAramaMotoru(_dizin!).Ara(
                        dosyaSorgusu,
                        dosyaSiralama,
                        turBasinaEnFazla,
                        iptal: iptal);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                hatalar.Add($"Dosya arama hatası: {ex.Message}");
            }

            return Array.Empty<DosyaSonucu>();
        }, iptal);

        await Task.WhenAll(videoTask, belgeTask, dosyaTask);

        if (hatalar.Count > 0 && string.IsNullOrEmpty(dizinIletisi))
        {
            dizinIletisi = string.Join("; ", hatalar);
        }

        return new BirlesikSonuclar(
            await videoTask,
            await belgeTask,
            await dosyaTask,
            dizinIletisi);
    }
}
