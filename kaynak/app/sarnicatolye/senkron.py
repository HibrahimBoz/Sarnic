"""Sarnıç Atölye çok kameralı ses senkronizasyon motoru."""
from dataclasses import dataclass
import numpy as np


__all__ = [
    "SenkronSonucu",
    "gcc_phat",
    "hazirla",
    "konusma_pencereleri",
    "olc",
]


@dataclass
class SenkronSonucu:
    """İki ses kaydı arasındaki senkronizasyon ölçüm sonucu."""
    ofset_sn: float
    drift_ppm: float
    guven: float
    pencere_olcumleri: list[tuple[float, float, float]]
    guvenilir: bool
    neden: str


def gcc_phat(
    ref: np.ndarray,
    x: np.ndarray,
    sr: int | float,
    max_gecikme_sn: float | None = None,
) -> tuple[float, float]:
    """GCC-PHAT çapraz ilinti ile zaman gecikmesini ve güven skorunu bulur.

    ofset > 0: x sinyali ref sinyalinden GEÇ (gecikmeli).
    """
    ref_dizi = np.asarray(ref, dtype=np.float64)
    x_dizi = np.asarray(x, dtype=np.float64)

    n_ref = len(ref_dizi)
    n_x = len(x_dizi)
    if n_ref == 0 or n_x == 0:
        return 0.0, 0.0

    # Doğru akım (DC) bileşenini temizle
    ref_sifir = ref_dizi - np.mean(ref_dizi)
    x_sifir = x_dizi - np.mean(x_dizi)

    std_ref = np.std(ref_sifir)
    std_x = np.std(x_sifir)
    if std_ref < 1e-9 or std_x < 1e-9:
        return 0.0, 0.0

    if max_gecikme_sn is not None:
        m = int(round(max_gecikme_sn * sr))
    else:
        m = max(n_ref, n_x)
    m = max(1, min(m, max(n_ref, n_x)))

    # Döngüsel sarılmayı (circular wrap-around) önlemek için sıfır dolgulama
    n_min = n_ref + n_x
    n_fft = 1 << int(n_min - 1).bit_length()
    if n_fft < 2 * m + 1:
        n_fft = 1 << int(2 * m).bit_length()

    R = np.fft.rfft(ref_sifir, n=n_fft)
    X = np.fft.rfft(x_sifir, n=n_fft)

    # Çapraz güç spektrumu ve PHAT ağırlığı
    C = X * np.conj(R)
    C_phat = C / (np.abs(C) + 1e-12)
    cc = np.fft.irfft(C_phat, n=n_fft)

    # [-m, +m] gecikme aralığındaki ilinti değerlerini çıkar
    corr = np.concatenate([cc[n_fft - m : ], cc[0 : m + 1]])
    lags = np.arange(-m, m + 1)

    idx_max = int(np.argmax(corr))
    val_max = float(corr[idx_max])
    k_max = int(lags[idx_max])

    # İkinci tepe için ±5 ms dışlama penceresi
    komsuluk = max(1, int(round(0.005 * sr)))
    maske = np.ones(len(corr), dtype=bool)
    sol = max(0, idx_max - komsuluk)
    sag = min(len(corr), idx_max + komsuluk + 1)
    maske[sol:sag] = False

    val_ikinci = float(np.max(corr[maske])) if np.any(maske) else 0.0

    # Alt-örnek tepe interpolasyonu (parabolik)
    delta = 0.0
    if 0 < idx_max < len(corr) - 1:
        y_m1 = float(corr[idx_max - 1])
        y_0 = val_max
        y_p1 = float(corr[idx_max + 1])
        payda = 2.0 * (2.0 * y_0 - y_m1 - y_p1)
        if payda > 1e-12:
            delta = float(np.clip((y_p1 - y_m1) / payda, -0.5, 0.5))

    k_alt = k_max + delta
    ofset_sn = float(k_alt / sr)

    # Güven skoru: birinci tepe / ikinci tepe oranından türetilmiş ve [0, 1] aralığına sıkıştırılmış
    if val_max <= 1e-12:
        guven = 0.0
    elif val_ikinci <= 1e-12:
        guven = 1.0
    else:
        oran = val_max / val_ikinci
        oran_fark = max(0.0, oran - 1.0)
        guven = float(np.clip(oran_fark / (oran_fark + 0.28), 0.0, 1.0))

    return ofset_sn, guven


def hazirla(
    ses: np.ndarray,
    sr: int,
    hedef_sr: int = 8000,
) -> np.ndarray:
    """300-3400 Hz bant geçiren FFT filtresi ve 8000 Hz'e yeniden örnekleme."""
    ses_dizi = np.asarray(ses, dtype=np.float64)
    n = len(ses_dizi)
    if n == 0:
        return np.zeros(0, dtype=np.float64)

    # 1. FFT ve frekans maskesi (300 - 3400 Hz)
    X = np.fft.rfft(ses_dizi)
    freqs = np.fft.rfftfreq(n, d=1.0 / sr)
    mask = (freqs >= 300.0) & (freqs <= 3400.0)
    X_filt = X * mask

    if sr == hedef_sr:
        return np.fft.irfft(X_filt, n=n)

    # 2. Frekans domeninde yeniden örnekleme (Fourier resampling)
    sure_sn = n / sr
    m = int(round(sure_sn * hedef_sr))
    if m <= 0:
        return np.zeros(0, dtype=np.float64)

    m_kutup = m // 2 + 1
    X_yeni = np.zeros(m_kutup, dtype=complex)
    ortak = min(len(X_filt), m_kutup)
    X_yeni[:ortak] = X_filt[:ortak]
    if m % 2 == 0:
        X_yeni[-1] = X_yeni[-1].real

    cikti = np.fft.irfft(X_yeni, n=m) * (float(m) / float(n))
    return cikti


def konusma_pencereleri(
    ses: np.ndarray,
    sr: int,
    pencere_sn: float = 30.0,
    adet: int = 5,
) -> list[float]:
    """Enerji eşiği üstünde, eşit aralıklı pencere başlangıç zamanları (saniye)."""
    ses_dizi = np.asarray(ses, dtype=np.float64)
    n = len(ses_dizi)
    p_ornek = int(round(pencere_sn * sr))
    if p_ornek <= 0 or n == 0:
        return [0.0] if n > 0 else []

    if n < p_ornek:
        # Sinyal pencere süresinden kısa
        return [0.0] if float(np.mean(ses_dizi ** 2)) > 1e-7 else []

    # 1 saniyelik adımlarla aday pencereleri tara
    adim = max(1, int(sr * 1.0))
    adaylar = np.arange(0, n - p_ornek + 1, adim)
    if len(adaylar) == 0:
        return [0.0]

    # Kümülatif kare toplamı ile her adayın pencere enerjisini hesapla
    ses_kare = ses_dizi ** 2
    kum = np.pad(np.cumsum(ses_kare), (1, 0), mode="constant")
    enerjiler = (kum[adaylar + p_ornek] - kum[adaylar]) / p_ornek

    maks_e = float(np.max(enerjiler)) if len(enerjiler) > 0 else 0.0
    if maks_e <= 1e-9:
        return []

    # Enerji eşiği ve sessizlikten kaçınma
    # Pencerenin tamamı ve başlangıç kısmı (ilk 2 saniye) konuşma enerjisi içermeli
    esik = max(1e-7, 0.20 * maks_e)
    p_bas = min(p_ornek, max(1, int(round(sr * 2.0))))
    enerjiler_bas = (kum[adaylar + p_bas] - kum[adaylar]) / p_bas
    esik_bas = max(1e-7, 0.05 * maks_e)

    filtre = (enerjiler >= esik) & (enerjiler_bas >= esik_bas)
    gecerli = adaylar[filtre]
    if len(gecerli) == 0:
        gecerli = adaylar[enerjiler >= esik]
    if len(gecerli) == 0:
        gecerli = adaylar[enerjiler >= 0.05 * maks_e]
    if len(gecerli) == 0:
        return []

    if len(gecerli) <= adet:
        secilen = gecerli
    else:
        secim_indeksleri = np.unique(np.round(np.linspace(0, len(gecerli) - 1, adet)).astype(int))
        secilen = gecerli[secim_indeksleri]

    return [float(i / sr) for i in secilen]


def olc(
    ref: np.ndarray,
    x: np.ndarray,
    sr: int,
    pencere_sn: float = 30.0,
    adet: int = 5,
    max_gecikme_sn: float = 60.0,
    min_guven: float = 0.25,
    on_isleme: bool = True,
) -> SenkronSonucu:
    """Çoklu pencerede GCC-PHAT ile zaman ofseti ve saat sürüklenmesini (drift) ölçer."""
    ref_dizi = np.asarray(ref, dtype=np.float64)
    x_dizi = np.asarray(x, dtype=np.float64)

    if len(ref_dizi) == 0 or len(x_dizi) == 0:
        return SenkronSonucu(
            ofset_sn=0.0,
            drift_ppm=0.0,
            guven=0.0,
            pencere_olcumleri=[],
            guvenilir=False,
            neden="bos ses sinyali",
        )

    # 1. Ön işleme (300-3400 Hz filtreleme ve 8000 Hz'e yeniden örnekleme)
    if on_isleme and sr >= 8000:
        ref_islenmis = hazirla(ref_dizi, sr)
        x_islenmis = hazirla(x_dizi, sr)
        is_sr = 8000
    else:
        ref_islenmis = ref_dizi
        x_islenmis = x_dizi
        is_sr = sr

    n_ref = len(ref_islenmis)
    n_x = len(x_islenmis)
    sure_ref = n_ref / is_sr
    sure_x = n_x / is_sr

    eff_pencere_sn = pencere_sn
    if sure_ref < pencere_sn:
        eff_pencere_sn = max(1.0, sure_ref * 0.8)

    # 2. Konuşma içeren pencereleri belirle
    pencereler = konusma_pencereleri(
        ref_islenmis,
        is_sr,
        pencere_sn=eff_pencere_sn,
        adet=max(adet, 5),
    )

    if len(pencereler) == 0:
        return SenkronSonucu(
            ofset_sn=0.0,
            drift_ppm=0.0,
            guven=0.0,
            pencere_olcumleri=[],
            guvenilir=False,
            neden="ortak ses yok",
        )

    p_ornek = int(round(eff_pencere_sn * is_sr))
    pencere_olcumleri: list[tuple[float, float, float]] = []

    # 3. Her pencere için GCC-PHAT çalıştır
    for t_sn in pencereler:
        i_ref_bas = int(round(t_sn * is_sr))
        i_ref_bit = min(n_ref, i_ref_bas + p_ornek)
        if i_ref_bit - i_ref_bas < max(10, int(is_sr * 0.5)):
            continue

        ref_parca = ref_islenmis[i_ref_bas:i_ref_bit]

        # x içinde arama aralığı: [-max_gecikme_sn, +max_gecikme_sn]
        t_x_bas = max(0.0, t_sn - max_gecikme_sn)
        t_x_bit = min(sure_x, t_sn + eff_pencere_sn + max_gecikme_sn)
        i_x_bas = int(round(t_x_bas * is_sr))
        i_x_bit = int(round(t_x_bit * is_sr))
        if i_x_bit <= i_x_bas:
            continue

        x_parca = x_islenmis[i_x_bas:i_x_bit]

        ofset_bagil, p_guven = gcc_phat(ref_parca, x_parca, is_sr, max_gecikme_sn=None)
        # Mutlak ofset dönüşümü: ofset_bagil = ofset_mutlak + (t_sn - t_x_bas)
        ofset_mutlak = ofset_bagil - (t_sn - t_x_bas)
        if abs(ofset_mutlak) > max_gecikme_sn:
            p_guven = 0.0  # izinli aralık dışındaki tepe geçersiz sayılır
        pencere_olcumleri.append((float(t_sn), float(ofset_mutlak), float(p_guven)))

    if len(pencere_olcumleri) == 0:
        return SenkronSonucu(
            ofset_sn=0.0,
            drift_ppm=0.0,
            guven=0.0,
            pencere_olcumleri=[],
            guvenilir=False,
            neden="ortak ses yok",
        )

    # 4. Düşük güvenli pencereleri ele
    gecerli = [olcum for olcum in pencere_olcumleri if olcum[2] >= min_guven]
    tum_guvenler = [olcum[2] for olcum in pencere_olcumleri]
    ortalama_guven = float(np.mean(tum_guvenler)) if tum_guvenler else 0.0

    if len(gecerli) < 3:
        neden = "ortak ses yok" if len(gecerli) == 0 else "yetersiz pencere"
        dusuk_guven = min(ortalama_guven * 0.3, 0.25)
        ilk_ofset = float(gecerli[0][1]) if len(gecerli) > 0 else 0.0
        return SenkronSonucu(
            ofset_sn=ilk_ofset,
            drift_ppm=0.0,
            guven=float(dusuk_guven),
            pencere_olcumleri=pencere_olcumleri,
            guvenilir=False,
            neden=neden,
        )

    # 5. Kalan >=3 pencereden t -> ofset doğrusu (RANSAC / medyan)
    T = np.array([olcum[0] for olcum in gecerli], dtype=np.float64)
    Y = np.array([olcum[1] for olcum in gecerli], dtype=np.float64)
    G = np.array([olcum[2] for olcum in gecerli], dtype=np.float64)
    M = len(gecerli)

    en_iyi_ic_noktalar: list[int] = []
    en_iyi_egim = 0.0
    en_iyi_kesim = float(np.median(Y))
    en_iyi_hata = float("inf")

    esik_sn = 0.020  # 20 ms artık hata eşiği

    for i in range(M):
        for j in range(i + 1, M):
            dt = T[j] - T[i]
            if abs(dt) < 0.5:
                m_aday = 0.0
                c_aday = (Y[i] + Y[j]) / 2.0
            else:
                m_aday = (Y[j] - Y[i]) / dt
                c_aday = Y[i] - m_aday * T[i]

            artiklar = np.abs(Y - (m_aday * T + c_aday))
            ic_noktalar = np.where(artiklar <= esik_sn)[0].tolist()

            if len(ic_noktalar) > len(en_iyi_ic_noktalar):
                en_iyi_ic_noktalar = ic_noktalar
                en_iyi_egim = m_aday
                en_iyi_kesim = c_aday
                en_iyi_hata = float(np.mean(artiklar[ic_noktalar]))
            elif len(ic_noktalar) == len(en_iyi_ic_noktalar) and len(ic_noktalar) >= 3:
                hata = float(np.mean(artiklar[ic_noktalar]))
                if hata < en_iyi_hata:
                    en_iyi_ic_noktalar = ic_noktalar
                    en_iyi_egim = m_aday
                    en_iyi_kesim = c_aday
                    en_iyi_hata = hata

    if len(en_iyi_ic_noktalar) >= 3:
        T_ic = T[en_iyi_ic_noktalar]
        Y_ic = Y[en_iyi_ic_noktalar]
        if np.ptp(T_ic) > 1.0:
            son_egim, son_kesim = np.polyfit(T_ic, Y_ic, 1)
        else:
            son_egim = 0.0
            son_kesim = float(np.mean(Y_ic))

        son_artiklar = np.abs(Y_ic - (son_egim * T_ic + son_kesim))
        artik_hata = float(np.sqrt(np.mean(son_artiklar ** 2)))
        drift_ppm = float(son_egim * 1e6)
        ofset_sn = float(son_kesim)
        ic_guvenler = G[en_iyi_ic_noktalar]
        son_guven = float(np.mean(ic_guvenler))
    else:
        if np.ptp(T) > 1.0:
            son_egim, son_kesim = np.polyfit(T, Y, 1)
        else:
            son_egim = 0.0
            son_kesim = float(np.median(Y))
        son_artiklar = np.abs(Y - (son_egim * T + son_kesim))
        artik_hata = float(np.sqrt(np.mean(son_artiklar ** 2)))
        drift_ppm = float(son_egim * 1e6)
        ofset_sn = float(son_kesim)
        son_guven = float(np.mean(G) * 0.3)

    # 6. Güvenilirlik ölçütü: en az 3 pencere güvenli VE artık hata <= 20 ms
    guvenilir = (len(en_iyi_ic_noktalar) >= 3) and (artik_hata <= 0.020)
    if guvenilir:
        neden = "basarili"
        nihai_guven = float(np.clip(son_guven * (1.0 - artik_hata / 0.040), 0.1, 1.0))
    else:
        neden = "yuksek artik hata" if len(en_iyi_ic_noktalar) >= 3 else "yetersiz pencere"
        nihai_guven = float(np.clip(son_guven * 0.3, 0.0, 0.25))

    return SenkronSonucu(
        ofset_sn=ofset_sn,
        drift_ppm=drift_ppm,
        guven=nihai_guven,
        pencere_olcumleri=pencere_olcumleri,
        guvenilir=guvenilir,
        neden=neden,
    )
