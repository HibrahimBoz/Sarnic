namespace Portal.Cekirdek;

using System.Text;

/// <summary>
/// Arama motoru belleğinde tutulan normalize edilmiş segment kaydı.
/// </summary>
internal sealed class IndeksliSegment
{
    public string DersAd { get; }
    public string JsonYolu { get; }
    public int SegmentNo { get; }
    public double BasSn { get; }
    public double SonSn { get; }
    public string Metin { get; }
    public string NormMetin { get; }
    public (string Kelime, int Bas, int Uzunluk)[] KelimelerVeKonumlar { get; }
    public string? Ceviri { get; }
    public string? NormCeviri { get; }
    public (string Kelime, int Bas, int Uzunluk)[]? CeviriKelimelerVeKonumlar { get; }

    public IndeksliSegment(
        string dersAd,
        string jsonYolu,
        int segmentNo,
        double basSn,
        double sonSn,
        string metin,
        string? ceviri = null)
    {
        DersAd = dersAd;
        JsonYolu = jsonYolu;
        SegmentNo = segmentNo;
        BasSn = basSn;
        SonSn = sonSn;
        Metin = metin;
        NormMetin = TrNormalizasyon.Normalle(metin);
        KelimelerVeKonumlar = KonumlariBul(NormMetin);
        Ceviri = ceviri;
        if (!string.IsNullOrWhiteSpace(ceviri))
        {
            NormCeviri = TrNormalizasyon.Normalle(ceviri);
            CeviriKelimelerVeKonumlar = KonumlariBul(NormCeviri);
        }
    }

    private static (string Kelime, int Bas, int Uzunluk)[] KonumlariBul(string norm)
    {
        if (string.IsNullOrEmpty(norm))
        {
            return Array.Empty<(string, int, int)>();
        }

        var liste = new List<(string, int, int)>();
        int i = 0;
        while (i < norm.Length)
        {
            while (i < norm.Length && norm[i] == ' ')
            {
                i++;
            }
            if (i >= norm.Length)
            {
                break;
            }

            int bas = i;
            while (i < norm.Length && norm[i] != ' ')
            {
                i++;
            }

            int uz = i - bas;
            liste.Add((norm.Substring(bas, uz), bas, uz));
        }

        return liste.ToArray();
    }
}

/// <summary>
/// İndeksleme sırasında atlanan (bozuk veya kilitli) ders.
/// </summary>
public sealed record AtlananDers(string DersAd, string JsonYolu, string Neden);

/// <summary>
/// Türkçe transkriptler üzerinde hızlı bellek içi metin arama motoru.
/// </summary>
public class MetinAramaMotoru : IAramaMotoru, IDisposable
{
    private readonly IndeksOnbellek? _onbellek;
    private readonly DuzeltmeDeposu _duzeltmeDeposu;
    private readonly Func<string, Transkript>? _transkriptOkuyucu;
    private readonly ReaderWriterLockSlim _kilit = new();
    private bool _disposed;
    private readonly object _indekslemeOturumKilidi = new();
    private int _aktifIndekslemeSayaci;
    private readonly Dictionary<string, (List<IndeksliSegment> Segmentler, OnbellekDers? OnbellekDers)> _indekslemeSirasindaGuncellenenler = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, List<IndeksliSegment>> _dersSegmentleri = new(StringComparer.OrdinalIgnoreCase);
    private IndeksliSegment[] _tumSegmentler = Array.Empty<IndeksliSegment>();

    /// <summary>
    /// Son indeksleme sırasında diskten fiilen okunan ders sayısı.
    /// </summary>
    public int DiskOkumaSayisi { get; private set; }

    /// <summary>
    /// Son indekslemede atlanan dersler (bozuk şema, kilitli dosya, okunamayan transkript).
    /// </summary>
    public IReadOnlyList<AtlananDers> AtlananDersler { get; private set; } = Array.Empty<AtlananDers>();

    /// <summary>Son indekslemede atlanan ders sayısı.</summary>
    public int AtlananDersSayisi => AtlananDersler.Count;

    public MetinAramaMotoru(
        IndeksOnbellek? onbellek = null,
        DuzeltmeDeposu? duzeltmeDeposu = null,
        Func<string, Transkript>? transkriptOkuyucu = null)
    {
        _onbellek = onbellek;
        _duzeltmeDeposu = duzeltmeDeposu ?? new DuzeltmeDeposu();
        _transkriptOkuyucu = transkriptOkuyucu;
    }

    /// <summary>
    /// Verilen ders kayıtlarını indeksler. Değişmeyen dersler önbellekten, değişenler diskten yüklenir.
    /// </summary>
    public async Task IndeksleAsync(IEnumerable<DersKaydi> dersler, IProgress<int>? ilerleme = null, CancellationToken iptal = default)
    {
        iptal.ThrowIfCancellationRequested();

        var dersListesi = (dersler ?? Array.Empty<DersKaydi>()).ToList();
        DiskOkumaSayisi = 0;
        var atlanan = new List<AtlananDers>();
        AtlananDersler = atlanan;

        if (dersListesi.Count == 0)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                _kilit.EnterWriteLock();
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                if (_disposed || iptal.IsCancellationRequested)
                {
                    return;
                }

                _dersSegmentleri.Clear();
                _tumSegmentler = Array.Empty<IndeksliSegment>();
            }
            finally
            {
                _kilit.ExitWriteLock();
            }

            ilerleme?.Report(100);
            return;
        }

        lock (_indekslemeOturumKilidi)
        {
            _aktifIndekslemeSayaci++;
        }

        try
        {
            string kok = KokDizinBelirle(dersListesi);
            OnbellekVerisi? onbellekVeri = null;

            if (_onbellek != null)
            {
                onbellekVeri = _onbellek.Yukle(kok);
            }

            var yeniOnbellek = new OnbellekVerisi();
            var geciciDersler = new Dictionary<string, List<IndeksliSegment>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < dersListesi.Count; i++)
            {
                iptal.ThrowIfCancellationRequested();
                if (_disposed)
                {
                    return;
                }

                var ders = dersListesi[i];
                string anahtar = ders.IndeksAnahtari;
                var guncelAnahtar = DersOnbellekAnahtari.Uret(ders);

                bool onbellektenAlindi = false;

                try
                {
                    if (onbellekVeri != null &&
                        onbellekVeri.Dersler.TryGetValue(anahtar, out var onbellekDers) &&
                        onbellekDers != null &&
                        AnahtarEsitMi(onbellekDers.Anahtar, guncelAnahtar) &&
                        SegmentlerGecerliMi(onbellekDers.Segmentler))
                    {
                        var yuklenenSegmentler = new List<IndeksliSegment>(onbellekDers.Segmentler.Count);
                        foreach (var seg in onbellekDers.Segmentler)
                        {
                            yuklenenSegmentler.Add(new IndeksliSegment(
                                ders.Ad,
                                ders.JsonYolu,
                                seg.No,
                                seg.Bas,
                                seg.Son,
                                seg.Metin,
                                seg.Ceviri));
                        }

                        geciciDersler[anahtar] = yuklenenSegmentler;
                        yeniOnbellek.Dersler[anahtar] = onbellekDers;
                        onbellektenAlindi = true;
                    }

                    if (!onbellektenAlindi)
                    {
                        var tazeSegmentler = DersiDisktenYukle(ders);
                        DiskOkumaSayisi++;
                        geciciDersler[anahtar] = tazeSegmentler;

                        var yeniOnbellekDers = new OnbellekDers
                        {
                            DersAd = ders.Ad,
                            JsonYolu = string.IsNullOrEmpty(anahtar) ? ders.JsonYolu : anahtar,
                            Anahtar = guncelAnahtar,
                            Segmentler = tazeSegmentler.Select(s => new OnbellekSegment(s.SegmentNo, s.BasSn, s.SonSn, s.Metin, s.Ceviri)).ToList()
                        };
                        yeniOnbellek.Dersler[anahtar] = yeniOnbellekDers;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    atlanan.Add(new AtlananDers(ders.Ad ?? string.Empty, ders.JsonYolu ?? string.Empty, ex.Message));
                }

                if (ilerleme != null)
                {
                    int yuzde = (int)((i + 1) * 100.0 / dersListesi.Count);
                    ilerleme.Report(yuzde);
                }
            }

            iptal.ThrowIfCancellationRequested();
            if (_disposed)
            {
                return;
            }

            // İndeksleme sırasında gelen DersiYenile güncellemelerini önbelleğe ve listeye aktar
            lock (_indekslemeOturumKilidi)
            {
                foreach (var kvp in _indekslemeSirasindaGuncellenenler)
                {
                    geciciDersler[kvp.Key] = kvp.Value.Segmentler;
                    if (kvp.Value.OnbellekDers != null)
                    {
                        yeniOnbellek.Dersler[kvp.Key] = kvp.Value.OnbellekDers;
                    }
                }
            }

            if (_onbellek != null && (yeniOnbellek.Dersler.Count > 0 || atlanan.Count == 0))
            {
                try
                {
                    _onbellek.Kaydet(yeniOnbellek, kok);
                }
                catch
                {
                    // Önbellek yazma hatasını yut
                }
            }

            iptal.ThrowIfCancellationRequested();
            if (_disposed)
            {
                return;
            }

            try
            {
                _kilit.EnterWriteLock();
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                if (_disposed || iptal.IsCancellationRequested)
                {
                    return;
                }

                // Kilit bekleme esnasında da DersiYenile çağrılmış olabilir:
                lock (_indekslemeOturumKilidi)
                {
                    foreach (var kvp in _indekslemeSirasindaGuncellenenler)
                    {
                        geciciDersler[kvp.Key] = kvp.Value.Segmentler;
                    }
                }

                _dersSegmentleri.Clear();
                foreach (var kvp in geciciDersler)
                {
                    _dersSegmentleri[kvp.Key] = kvp.Value;
                }
                _tumSegmentler = _dersSegmentleri.Values.SelectMany(x => x).ToArray();
            }
            finally
            {
                _kilit.ExitWriteLock();
            }

            ilerleme?.Report(100);
            await Task.CompletedTask;
        }
        finally
        {
            lock (_indekslemeOturumKilidi)
            {
                _aktifIndekslemeSayaci--;
                if (_aktifIndekslemeSayaci == 0)
                {
                    _indekslemeSirasindaGuncellenenler.Clear();
                }
            }
        }
    }

    /// <summary>
    /// Sorgu ifadesine göre indekslenmiş segmentlerde arama yapar.
    /// </summary>
    public IReadOnlyList<AramaSonucu> Ara(string sorgu, int en_fazla = 50)
    {
        if (string.IsNullOrWhiteSpace(sorgu) || en_fazla <= 0)
        {
            return Array.Empty<AramaSonucu>();
        }

        string normSorgu = TrNormalizasyon.Normalle(sorgu);
        if (string.IsNullOrEmpty(normSorgu))
        {
            return Array.Empty<AramaSonucu>();
        }

        string[] kelimeler = TrNormalizasyon.Kelimeler(sorgu);
        if (kelimeler.Length == 0)
        {
            return Array.Empty<AramaSonucu>();
        }

        string[] benzersizKelimeler = kelimeler.Distinct(StringComparer.Ordinal).ToArray();

        if (_disposed)
        {
            return Array.Empty<AramaSonucu>();
        }

        IndeksliSegment[] segmentler;
        try
        {
            _kilit.EnterReadLock();
        }
        catch (ObjectDisposedException)
        {
            return Array.Empty<AramaSonucu>();
        }

        try
        {
            segmentler = _tumSegmentler;
        }
        finally
        {
            _kilit.ExitReadLock();
        }

        if (segmentler.Length == 0)
        {
            return Array.Empty<AramaSonucu>();
        }

        var adaylar = new List<(IndeksliSegment Seg, double Puan, int Tier, bool CevirideEslesti)>();

        for (int i = 0; i < segmentler.Length; i++)
        {
            var seg = segmentler[i];
            var (puan, tier) = Puanla(seg.NormMetin, seg.KelimelerVeKonumlar, normSorgu, benzersizKelimeler);
            bool cevirideEslesti = false;

            if (puan <= 0 && !string.IsNullOrWhiteSpace(seg.NormCeviri) && seg.CeviriKelimelerVeKonumlar != null)
            {
                (puan, tier) = Puanla(seg.NormCeviri, seg.CeviriKelimelerVeKonumlar, normSorgu, benzersizKelimeler);
                if (puan > 0)
                {
                    cevirideEslesti = true;
                }
            }

            if (puan > 0)
            {
                adaylar.Add((seg, puan, tier, cevirideEslesti));
            }
        }

        if (adaylar.Count == 0)
        {
            return Array.Empty<AramaSonucu>();
        }

        var siraliAdaylar = adaylar
            .OrderByDescending(x => x.Puan)
            .ThenBy(x => x.Seg.DersAd, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Seg.BasSn)
            .Take(en_fazla)
            .ToList();

        var sonuclar = new List<AramaSonucu>(siraliAdaylar.Count);

        foreach (var item in siraliAdaylar)
        {
            string metin = item.CevirideEslesti ? (item.Seg.Ceviri ?? item.Seg.Metin) : item.Seg.Metin;
            var kk = item.CevirideEslesti ? (item.Seg.CeviriKelimelerVeKonumlar ?? item.Seg.KelimelerVeKonumlar) : item.Seg.KelimelerVeKonumlar;
            var vurgular = VurgulariHesapla(metin, kk, item.Tier, normSorgu, benzersizKelimeler);
            sonuclar.Add(new AramaSonucu(
                item.Seg.DersAd,
                item.Seg.JsonYolu,
                item.Seg.SegmentNo,
                item.Seg.BasSn,
                metin,
                item.Puan,
                vurgular,
                item.CevirideEslesti));
        }

        return sonuclar;
    }

    /// <summary>
    /// Düzeltme sonrası tek bir dersin indeksini günceller.
    /// </summary>
    public void DersiYenile(DersKaydi ders)
    {
        if (ders == null || _disposed)
        {
            return;
        }

        var tazeSegmentler = DersiDisktenYukle(ders);
        string anahtar = ders.IndeksAnahtari;

        OnbellekDers? yeniOnbellekDers = null;
        if (_onbellek != null)
        {
            var guncelAnahtar = DersOnbellekAnahtari.Uret(ders);
            yeniOnbellekDers = new OnbellekDers
            {
                DersAd = ders.Ad,
                JsonYolu = string.IsNullOrEmpty(anahtar) ? ders.JsonYolu : anahtar,
                Anahtar = guncelAnahtar,
                Segmentler = tazeSegmentler.Select(s => new OnbellekSegment(s.SegmentNo, s.BasSn, s.SonSn, s.Metin, s.Ceviri)).ToList()
            };
        }

        try
        {
            _kilit.EnterWriteLock();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (_disposed)
            {
                return;
            }

            _dersSegmentleri[anahtar] = tazeSegmentler;
            _tumSegmentler = _dersSegmentleri.Values.SelectMany(x => x).ToArray();

            lock (_indekslemeOturumKilidi)
            {
                if (_aktifIndekslemeSayaci > 0)
                {
                    _indekslemeSirasindaGuncellenenler[anahtar] = (tazeSegmentler, yeniOnbellekDers);
                }
            }
        }
        finally
        {
            _kilit.ExitWriteLock();
        }

        if (_onbellek != null && yeniOnbellekDers != null)
        {
            try
            {
                string kok = KokDizinBelirle(new[] { ders });
                var mevcutVeri = _onbellek.Yukle(kok) ?? new OnbellekVerisi();
                mevcutVeri.Dersler[anahtar] = yeniOnbellekDers;
                _onbellek.Kaydet(mevcutVeri, kok);
            }
            catch
            {
                // Önbellek yazma hatasını yut
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _kilit.EnterWriteLock();
            try
            {
                _disposed = true;
            }
            finally
            {
                _kilit.ExitWriteLock();
            }
        }
        catch (ObjectDisposedException)
        {
        }

        _kilit.Dispose();
        GC.SuppressFinalize(this);
    }

    private List<IndeksliSegment> DersiDisktenYukle(DersKaydi ders)
    {
        Transkript transkript = _transkriptOkuyucu != null
            ? _transkriptOkuyucu(ders.JsonYolu)
            : TranskriptOkuyucu.Oku(ders.JsonYolu);

        string duzeltmeKlasoru = DersOnbellekAnahtari.DuzeltmelerKlasoruBul(ders);
        BirlestirmeSonucu sonuc = _duzeltmeDeposu.Birlestir(transkript, duzeltmeKlasoru);

        var segmentler = new List<IndeksliSegment>(sonuc.Segmentler.Count);
        foreach (var ef in sonuc.Segmentler)
        {
            segmentler.Add(new IndeksliSegment(
                ders.Ad,
                ders.JsonYolu,
                ef.No,
                ef.Bas,
                ef.Son,
                ef.Metin,
                ef.Ceviri));
        }

        return segmentler;
    }

    private static (double Puan, int Tier) Puanla(
        string normMetin,
        (string Kelime, int Bas, int Uzunluk)[] kelimelerVeKonumlar,
        string normSorgu,
        string[] benzersizKelimeler)
    {
        if (TamIfadeEslesmesiVarMi(normMetin, normSorgu))
        {
            return (100.0, 1);
        }

        int eslesenSayisi = 0;
        for (int k = 0; k < benzersizKelimeler.Length; k++)
        {
            string qw = benzersizKelimeler[k];
            if (normMetin.Contains(qw, StringComparison.Ordinal))
            {
                for (int w = 0; w < kelimelerVeKonumlar.Length; w++)
                {
                    if (kelimelerVeKonumlar[w].Kelime.StartsWith(qw, StringComparison.Ordinal))
                    {
                        eslesenSayisi++;
                        break;
                    }
                }
            }
        }

        if (eslesenSayisi == benzersizKelimeler.Length)
        {
            return (50.0, 2);
        }
        else if (benzersizKelimeler.Length >= 2)
        {
            double oran = (double)eslesenSayisi / benzersizKelimeler.Length;
            if (oran >= 0.60)
            {
                return (10.0 * oran, 3);
            }
        }

        return (0.0, 0);
    }

    private static bool TamIfadeEslesmesiVarMi(string normMetin, string normSorgu)
    {
        if (string.IsNullOrEmpty(normMetin) || string.IsNullOrEmpty(normSorgu))
        {
            return false;
        }

        int idx = 0;
        while ((idx = normMetin.IndexOf(normSorgu, idx, StringComparison.Ordinal)) >= 0)
        {
            bool basGecerli = (idx == 0 || normMetin[idx - 1] == ' ');
            bool sonGecerli = (idx + normSorgu.Length == normMetin.Length || normMetin[idx + normSorgu.Length] == ' ');

            if (basGecerli && sonGecerli)
            {
                return true;
            }

            idx++;
        }

        return false;
    }

    private static (int Bas, int Uzunluk)[] VurgulariHesapla(
        string metin,
        (string Kelime, int Bas, int Uzunluk)[] kelimelerVeKonumlar,
        int tier,
        string normSorgu,
        string[] sorguKelimeleri)
    {
        var (norm, normToOrig) = NormalleVeHarita(metin);
        if (normToOrig.Length == 0)
        {
            return Array.Empty<(int, int)>();
        }

        var araliklar = new List<(int Bas, int Uzunluk)>();

        if (tier == 1)
        {
            int idx = 0;
            while ((idx = norm.IndexOf(normSorgu, idx, StringComparison.Ordinal)) >= 0)
            {
                bool basGecerli = (idx == 0 || norm[idx - 1] == ' ');
                bool sonGecerli = (idx + normSorgu.Length == norm.Length || norm[idx + normSorgu.Length] == ' ');

                if (basGecerli && sonGecerli)
                {
                    int bas = idx;
                    int son = idx + normSorgu.Length - 1;
                    if (son < normToOrig.Length)
                    {
                        int origBas = normToOrig[bas];
                        int origSon = normToOrig[son];
                        araliklar.Add((origBas, origSon - origBas + 1));
                    }
                    idx += Math.Max(1, normSorgu.Length);
                }
                else
                {
                    idx++;
                }
            }
        }
        else
        {
            foreach (var qw in sorguKelimeleri)
            {
                foreach (var kk in kelimelerVeKonumlar)
                {
                    if (kk.Kelime.StartsWith(qw, StringComparison.Ordinal))
                    {
                        int bas = kk.Bas;
                        int son = kk.Bas + qw.Length - 1;
                        if (son < normToOrig.Length)
                        {
                            int origBas = normToOrig[bas];
                            int origSon = normToOrig[son];
                            araliklar.Add((origBas, origSon - origBas + 1));
                        }
                    }
                }
            }
        }

        return BirlestirVurgular(araliklar);
    }

    private static (int Bas, int Uzunluk)[] BirlestirVurgular(List<(int Bas, int Uzunluk)> vurgular)
    {
        if (vurgular.Count == 0)
        {
            return Array.Empty<(int, int)>();
        }

        var sirali = vurgular
            .Where(v => v.Uzunluk > 0 && v.Bas >= 0)
            .OrderBy(v => v.Bas)
            .ThenBy(v => v.Uzunluk)
            .ToList();

        if (sirali.Count == 0)
        {
            return Array.Empty<(int, int)>();
        }

        var sonuc = new List<(int Bas, int Uzunluk)>();
        var mevcut = sirali[0];

        for (int i = 1; i < sirali.Count; i++)
        {
            var sonraki = sirali[i];
            if (sonraki.Bas <= mevcut.Bas + mevcut.Uzunluk)
            {
                int yeniSon = Math.Max(mevcut.Bas + mevcut.Uzunluk, sonraki.Bas + sonraki.Uzunluk);
                mevcut = (mevcut.Bas, yeniSon - mevcut.Bas);
            }
            else
            {
                sonuc.Add(mevcut);
                mevcut = sonraki;
            }
        }

        sonuc.Add(mevcut);
        return sonuc.ToArray();
    }

    private static (string Norm, int[] NormToOrig) NormalleVeHarita(string? metin)
    {
        if (string.IsNullOrWhiteSpace(metin))
        {
            return (string.Empty, Array.Empty<int>());
        }

        var sb = new StringBuilder(metin.Length);
        var normToOrigList = new List<int>(metin.Length);
        bool oncekiBosluk = false;

        for (int origIdx = 0; origIdx < metin.Length; origIdx++)
        {
            char ch = metin[origIdx];
            char donusen = Donustur(ch);
            if (donusen == ' ')
            {
                if (!oncekiBosluk && sb.Length > 0)
                {
                    sb.Append(' ');
                    normToOrigList.Add(origIdx);
                    oncekiBosluk = true;
                }
            }
            else
            {
                sb.Append(donusen);
                normToOrigList.Add(origIdx);
                oncekiBosluk = false;
            }
        }

        while (sb.Length > 0 && sb[^1] == ' ')
        {
            sb.Length--;
            normToOrigList.RemoveAt(normToOrigList.Count - 1);
        }

        return (sb.ToString(), normToOrigList.ToArray());
    }

    private static char Donustur(char c)
    {
        switch (c)
        {
            case 'ı':
            case 'İ':
            case 'I':
            case 'i':
                return 'i';
            case 'ş':
            case 'Ş':
                return 's';
            case 'ğ':
            case 'Ğ':
                return 'g';
            case 'ü':
            case 'Ü':
                return 'u';
            case 'ö':
            case 'Ö':
                return 'o';
            case 'ç':
            case 'Ç':
                return 'c';
            case 'â':
            case 'Â':
                return 'a';
            case 'î':
            case 'Î':
                return 'i';
            case 'û':
            case 'Û':
                return 'u';
            case '\'':
            case '’':
            case '‘':
            case '`':
            case '´':
                return ' ';
            default:
                if (c >= 'A' && c <= 'Z')
                {
                    return (char)(c + 32);
                }

                if (char.IsPunctuation(c) || char.IsWhiteSpace(c) || char.IsSymbol(c) || char.IsControl(c))
                {
                    return ' ';
                }

                return char.ToLowerInvariant(c);
        }
    }

    private static bool AnahtarEsitMi(DersOnbellekAnahtari? a, DersOnbellekAnahtari? b)
    {
        if (a == null || b == null)
        {
            return false;
        }

        return a.JsonZamanUtcTicks == b.JsonZamanUtcTicks &&
               a.JsonBoyut == b.JsonBoyut &&
               a.DuzeltmeSayisi == b.DuzeltmeSayisi &&
               a.DuzeltmeEnYeniUtcTicks == b.DuzeltmeEnYeniUtcTicks;
    }

    private static bool SegmentlerGecerliMi(List<OnbellekSegment>? segmentler)
    {
        if (segmentler == null)
        {
            return false;
        }

        foreach (var seg in segmentler)
        {
            if (seg == null || seg.Metin == null)
            {
                return false;
            }
        }

        return true;
    }

    private static string KokDizinBelirle(IReadOnlyList<DersKaydi> dersler)
    {
        if (dersler.Count == 0)
        {
            return string.Empty;
        }

        var ilk = dersler[0].KlasorYolu;
        if (string.IsNullOrEmpty(ilk))
        {
            ilk = dersler[0].JsonYolu;
        }

        if (string.IsNullOrEmpty(ilk))
        {
            return string.Empty;
        }

        var ust = Path.GetDirectoryName(ilk);
        return ust ?? ilk;
    }
}
