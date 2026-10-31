"""Sarnıç Atölye senkron motoru kabul testleri."""
import math
import sys
import unittest
from pathlib import Path

kok = Path(__file__).resolve().parent.parent / "app"
if str(kok) not in sys.path:
    sys.path.insert(0, str(kok))

import numpy as np
from sarnicatolye.senkron import (
    SenkronSonucu,
    gcc_phat,
    hazirla,
    konusma_pencereleri,
    olc,
)


def _sentetik_konusma(
    sure_sn: float,
    sr: int = 8000,
    tohum: int = 42,
) -> np.ndarray:
    """Konuşma benzeri sinyal: genlik zarfı değişen bantlı gürültü (sessizlik+patlama dizisi)."""
    rng = np.random.default_rng(tohum)
    n = int(round(sure_sn * sr))
    if n <= 0:
        return np.zeros(0, dtype=np.float64)

    # 1. Beyaz gürültü ve bant geçiren filtre
    gurultu = rng.standard_normal(n)
    G = np.fft.rfft(gurultu)
    freqs = np.fft.rfftfreq(n, d=1.0 / sr)
    ust_kesim = min(3400.0, sr * 0.45)
    mask = (freqs >= 300.0) & (freqs <= ust_kesim)
    bantli = np.fft.irfft(G * mask, n=n)

    # 2. Genlik zarfı: hece/sözcük patlamaları ve duraklamalar
    t = np.arange(n) / sr
    ana_zarf = np.maximum(0.0, np.sin(2.0 * np.pi * 1.5 * t) + 0.6 * np.sin(2.0 * np.pi * 3.5 * t) - 0.2)

    patlama_maskesi = np.zeros(n)
    pos = int(sr * 0.2)
    while pos < n - int(sr * 0.4):
        p_boy = rng.integers(int(sr * 0.15), int(sr * 0.50))
        patlama_maskesi[pos : min(n, pos + p_boy)] = 1.0
        pos += p_boy + rng.integers(int(sr * 0.10), int(sr * 0.35))

    sinyal = bantli * ana_zarf * patlama_maskesi
    rms = np.sqrt(np.mean(sinyal ** 2))
    if rms > 1e-9:
        sinyal = sinyal / rms * 0.2
    return sinyal


class TestSenkronMotoru(unittest.TestCase):
    """Senkronizasyon motoru doğruluk ve dayanıklılık testleri."""

    def test_hazirla_dogruluk(self) -> None:
        """300-3400 Hz bant geçiren FFT maskesi ve 8000 Hz örneklemenin doğruluğu."""
        sr_giris = 16000
        sure_sn = 1.5
        n = int(round(sure_sn * sr_giris))
        t = np.arange(n) / sr_giris

        # 100 Hz (engellenmeli), 1000 Hz (geçmeli), 5000 Hz (engellenmeli)
        f_alt = 100.0
        f_gecen = 1000.0
        f_ust = 5000.0
        giris = (
            np.sin(2.0 * np.pi * f_alt * t)
            + np.sin(2.0 * np.pi * f_gecen * t)
            + np.sin(2.0 * np.pi * f_ust * t)
        )

        cikti = hazirla(giris, sr=sr_giris, hedef_sr=8000)

        # Çıktı örnekleme hızı 8000 Hz olmalı
        beklenen_ornek = int(round(sure_sn * 8000))
        self.assertEqual(len(cikti), beklenen_ornek)

        # Spektrum incelemesi
        C = np.fft.rfft(cikti)
        c_freqs = np.fft.rfftfreq(len(cikti), d=1.0 / 8000)

        idx_alt = np.argmin(np.abs(c_freqs - f_alt))
        idx_gecen = np.argmin(np.abs(c_freqs - f_gecen))

        guc_alt = np.abs(C[idx_alt])
        guc_gecen = np.abs(C[idx_gecen])

        # 100 Hz bileşeni bastırılmış olmalı (> 40 dB)
        self.assertLess(guc_alt / max(guc_gecen, 1e-9), 1e-2)
        # 1000 Hz geçiş bandındaki bileşen korunmuş olmalı
        self.assertGreater(guc_gecen, 10.0)

    def test_gcc_phat_alt_ornek_ve_guven(self) -> None:
        """GCC-PHAT alt-örnek parabolik interpolasyon ve güven skoru."""
        sr = 8000
        ref = _sentetik_konusma(10.0, sr=sr, tohum=101)

        # 12.35 örnek gecikme (alt-örnek kayması)
        kayma_ornek = 12.35
        beklenen_ofset = kayma_ornek / sr

        # Sinc/Fourier tabanlı hassas kaydırma
        R = np.fft.rfft(ref)
        freqs = np.fft.rfftfreq(len(ref), d=1.0 / sr)
        faz_kaymasi = np.exp(-1j * 2.0 * np.pi * freqs * beklenen_ofset)
        x = np.fft.irfft(R * faz_kaymasi, n=len(ref))

        ofset, guven = gcc_phat(ref, x, sr, max_gecikme_sn=1.0)

        # Alt-örnek parabolik interpolasyon ile hata <= 0.2 ms
        hata = abs(ofset - beklenen_ofset)
        self.assertLessEqual(hata, 0.0002)
        self.assertGreater(guven, 0.8)
        self.assertLessEqual(guven, 1.0)

    def test_senaryo_1_bilinen_ofset_temiz(self) -> None:
        """Senaryo 1: Bilinen ofset 0.3, 4.0, 37.0 s, temiz -> hata <= 5 ms."""
        sr = 8000
        sure_sn = 120.0
        ref = _sentetik_konusma(sure_sn, sr=sr, tohum=1001)

        for ofset in [0.3, 4.0, 37.0]:
            with self.subTest(ofset=ofset):
                gecikme_ornek = int(round(ofset * sr))
                x = np.zeros_like(ref)
                x[gecikme_ornek:] = ref[: len(ref) - gecikme_ornek]

                sonuc = olc(
                    ref,
                    x,
                    sr=sr,
                    pencere_sn=30.0,
                    adet=5,
                    max_gecikme_sn=45.0,
                )

                self.assertTrue(sonuc.guvenilir, f"Ofset {ofset} için güvenilir olmalı: {sonuc.neden}")
                hata = abs(sonuc.ofset_sn - ofset)
                self.assertLessEqual(hata, 0.005, f"Ofset {ofset} için hata 5 ms altında olmalı, gercek: {hata:.6f} s")
                self.assertGreater(sonuc.guven, 0.7)

    def test_senaryo_2_ek_gurultu_snr(self) -> None:
        """Senaryo 2: Ek gürültü SNR 20 ve 10 dB -> hata <= 40 ms."""
        sr = 8000
        sure_sn = 80.0
        ref = _sentetik_konusma(sure_sn, sr=sr, tohum=2002)
        ofset = 2.0
        gecikme_ornek = int(round(ofset * sr))
        x_temiz = np.zeros_like(ref)
        x_temiz[gecikme_ornek:] = ref[: len(ref) - gecikme_ornek]

        rng = np.random.default_rng(2023)
        p_sinyal = float(np.mean(x_temiz[gecikme_ornek:] ** 2))

        for snr in [20, 10]:
            with self.subTest(snr=snr):
                p_gurultu = p_sinyal / (10.0 ** (snr / 10.0))
                gurultu = rng.standard_normal(len(x_temiz)) * math.sqrt(p_gurultu)
                x_gurultulu = x_temiz + gurultu

                sonuc = olc(
                    ref,
                    x_gurultulu,
                    sr=sr,
                    pencere_sn=25.0,
                    adet=5,
                    max_gecikme_sn=10.0,
                )

                self.assertTrue(sonuc.guvenilir, f"SNR {snr} dB güvenilir olmalı: {sonuc.neden}")
                hata = abs(sonuc.ofset_sn - ofset)
                self.assertLessEqual(hata, 0.040, f"SNR {snr} dB hata <= 40 ms olmalı, gercek: {hata:.6f} s")

    def test_senaryo_3_snr_0db(self) -> None:
        """Senaryo 3: SNR 0 dB -> ya hata <= 80 ms ya guvenilir=False (asla yüksek güvenle yanlış)."""
        sr = 8000
        sure_sn = 80.0
        ref = _sentetik_konusma(sure_sn, sr=sr, tohum=3003)
        ofset = 1.8
        gecikme_ornek = int(round(ofset * sr))
        x_temiz = np.zeros_like(ref)
        x_temiz[gecikme_ornek:] = ref[: len(ref) - gecikme_ornek]

        rng = np.random.default_rng(3034)
        p_sinyal = float(np.mean(x_temiz[gecikme_ornek:] ** 2))
        p_gurultu = p_sinyal / (10.0 ** (0.0 / 10.0))  # 0 dB: sinyal gücü = gürültü gücü
        gurultu = rng.standard_normal(len(x_temiz)) * math.sqrt(p_gurultu)
        x_gurultulu = x_temiz + gurultu

        sonuc = olc(
            ref,
            x_gurultulu,
            sr=sr,
            pencere_sn=25.0,
            adet=5,
            max_gecikme_sn=10.0,
        )

        hata = abs(sonuc.ofset_sn - ofset)
        # Kural: ya hata <= 80 ms ya da güvenilir=False
        kural_saglandi = (hata <= 0.080) or (not sonuc.guvenilir)
        self.assertTrue(kural_saglandi, f"Hata {hata:.4f} s ve guvenilir={sonuc.guvenilir}")

        # Eğer güvenilir değilse asla yüksek güven verilmemeli
        if not sonuc.guvenilir:
            self.assertLess(sonuc.guven, 0.6, "Güvenilmez sonuç yüksek güvenle verilmemeli")

    def test_ofset_sinir_disi_reddedilir(self) -> None:
        """Gerçek ofset max_gecikme_sn dışındaysa sonuç güvenilir OLMAMALI."""
        sr = 8000
        ref = _sentetik_konusma(80.0, sr=sr, tohum=7001)
        gecikme_ornek = int(round(15.0 * sr))
        x = np.zeros_like(ref)
        x[gecikme_ornek:] = ref[: len(ref) - gecikme_ornek]
        sonuc = olc(ref, x, sr=sr, pencere_sn=25.0, adet=5, max_gecikme_sn=10.0)
        self.assertFalse(sonuc.guvenilir, "Sınır dışı ofset güvenilir sayılmamalı")

    def test_iliskisiz_sinyaller_cok_tohum(self) -> None:
        """İlişkisiz iki sinyal, 10 farklı tohumda hiç güvenilir çıkmamalı."""
        sr = 8000
        for tohum in range(8100, 8110):
            ref = _sentetik_konusma(80.0, sr=sr, tohum=tohum)
            x = _sentetik_konusma(80.0, sr=sr, tohum=tohum + 500)
            sonuc = olc(ref, x, sr=sr, pencere_sn=25.0, adet=5, max_gecikme_sn=10.0)
            self.assertFalse(sonuc.guvenilir, f"tohum {tohum}: ilişkisiz sinyal güvenilir çıktı")

    def test_senaryo_4_yanki_ve_kazanc(self) -> None:
        """Senaryo 4: Yankı (üstel azalan impuls yanıt, RT60~0.5 s) + farklı kazanç -> hata <= 40 ms."""
        sr = 8000
        sure_sn = 80.0
        ref = _sentetik_konusma(sure_sn, sr=sr, tohum=4004)
        ofset = 2.4
        gecikme_ornek = int(round(ofset * sr))
        x_temiz = np.zeros_like(ref)
        x_temiz[gecikme_ornek:] = ref[: len(ref) - gecikme_ornek]

        # RT60 = 0.5 s üstel azalan oda dürtü yanıtı (RIR)
        n_rir = int(round(0.5 * sr))
        t_rir = np.arange(n_rir) / sr
        alfa = 3.0 * math.log(10.0) / 0.5  # ~13.8155
        rng = np.random.default_rng(4045)
        rir = rng.standard_normal(n_rir) * np.exp(-alfa * t_rir)
        # Gerçekçi oda: doğrudan-yankı enerji oranı (DRR) -6 dB (doğrudan yol, kuyruk enerjisinin yarısı kadar genlik)
        rir[0] = 0.0
        rir[0] = 0.5 * float(np.sqrt(np.sum(rir ** 2)))
        rir = rir / np.sqrt(np.sum(rir ** 2))

        # Konvolüsyon ve farklı kazanç (gain = 1.4)
        n_conv = len(x_temiz) + n_rir
        n_fft = 1 << int(n_conv - 1).bit_length()
        X = np.fft.rfft(x_temiz, n=n_fft)
        H = np.fft.rfft(rir, n=n_fft)
        x_yanki = np.fft.irfft(X * H, n=n_fft)[: len(x_temiz)] * 1.4

        sonuc = olc(
            ref,
            x_yanki,
            sr=sr,
            pencere_sn=25.0,
            adet=5,
            max_gecikme_sn=10.0,
        )

        self.assertTrue(sonuc.guvenilir, f"Yankılı sinyal güvenilir olmalı: {sonuc.neden}")
        hata = abs(sonuc.ofset_sn - ofset)
        self.assertLessEqual(hata, 0.040, f"Yankılı sinyal hatası <= 40 ms olmalı, gercek: {hata:.6f} s")

    def test_senaryo_5_drift_100_ve_500_ppm(self) -> None:
        """Senaryo 5: Drift +100 ve +500 ppm (hızı değiştirerek yeniden örnekleme) -> hata <= 30 ppm (1 saat)."""
        sr = 2000  # Düşük örnekleme hızı ile hızlı işlem (< 60 s)
        sure_sn = 3600.0  # 1 saat
        ref = _sentetik_konusma(sure_sn, sr=sr, tohum=5005)
        n = len(ref)
        t_eski = np.arange(n) / sr

        for hedef_drift_ppm in [100.0, 500.0]:
            with self.subTest(drift_ppm=hedef_drift_ppm):
                drift_orani = hedef_drift_ppm * 1e-6
                # x sinyalinin referansa göre zaman eksenini esnet
                t_hedef = (1.0 - drift_orani) * t_eski - 1.0
                x = np.interp(t_hedef, t_eski, ref, left=0.0, right=0.0)

                sonuc = olc(
                    ref,
                    x,
                    sr=sr,
                    pencere_sn=30.0,
                    adet=6,
                    max_gecikme_sn=10.0,
                    on_isleme=False,
                )

                self.assertTrue(sonuc.guvenilir, f"Drift {hedef_drift_ppm} ppm güvenilir olmalı: {sonuc.neden}")
                drift_hatasi = abs(sonuc.drift_ppm - hedef_drift_ppm)
                self.assertLessEqual(
                    drift_hatasi,
                    30.0,
                    f"Drift hatası <= 30 ppm olmalı, gercek drift: {sonuc.drift_ppm:.2f} ppm (hata: {drift_hatasi:.2f})",
                )
                ofset_hatasi = abs(sonuc.ofset_sn - 1.0)
                self.assertLessEqual(ofset_hatasi, 0.040, f"Başlangıç ofseti hatası <= 40 ms olmalı: {ofset_hatasi:.4f} s")

    def test_senaryo_6_iliskisiz_iki_sinyal(self) -> None:
        """Senaryo 6: İlişkisiz iki sinyal -> guvenilir=False."""
        sr = 8000
        ref = _sentetik_konusma(80.0, sr=sr, tohum=6001)
        x = _sentetik_konusma(80.0, sr=sr, tohum=6002)

        sonuc = olc(ref, x, sr=sr, pencere_sn=25.0, adet=5, max_gecikme_sn=10.0)

        self.assertFalse(sonuc.guvenilir, "İlişkisiz iki sinyal güvenilir OLMAMALI")
        self.assertIn(sonuc.neden, ["ortak ses yok", "yetersiz pencere"])
        self.assertLess(sonuc.guven, 0.5, "İlişkisiz sinyalde güven düşük olmalı")

    def test_senaryo_7_uzun_sessizlik_kayan_pencereler(self) -> None:
        """Senaryo 7: Uzun sessizlik içeren sinyal -> pencereler konuşmalı yerlere kayar, sonuç doğru."""
        sr = 8000
        sure_sessizlik = 40.0
        sure_konusma = 60.0
        konusma = _sentetik_konusma(sure_konusma, sr=sr, tohum=7007)
        sessizlik = np.zeros(int(round(sure_sessizlik * sr)))
        ref = np.concatenate([sessizlik, konusma])

        ofset = 2.5
        gecikme_ornek = int(round(ofset * sr))
        x = np.zeros_like(ref)
        x[gecikme_ornek:] = ref[: len(ref) - gecikme_ornek]

        pencereler = konusma_pencereleri(ref, sr=sr, pencere_sn=20.0, adet=4)

        # Sessiz bölge elenmeli, pencereler konuşma bölgesine kaymalı
        self.assertGreater(len(pencereler), 0)
        for t_p in pencereler:
            self.assertGreaterEqual(t_p, 35.0, f"Pencere sessiz bölgede olmamalı: {t_p} s")

        sonuc = olc(ref, x, sr=sr, pencere_sn=20.0, adet=4, max_gecikme_sn=10.0)

        self.assertTrue(sonuc.guvenilir, f"Sessizlik içeren sinyal güvenilir olmalı: {sonuc.neden}")
        hata = abs(sonuc.ofset_sn - ofset)
        self.assertLessEqual(hata, 0.005, f"Hata <= 5 ms olmalı, gercek: {hata:.6f} s")


if __name__ == "__main__":
    unittest.main()
