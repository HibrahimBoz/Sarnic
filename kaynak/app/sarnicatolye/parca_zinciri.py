"""
Sarnıç Atölye - Parça Zinciri Modülü
Kamera parça dosyalarını sanal zaman çizelgesine dönüştüren saf mantık bileşeni.
"""

from __future__ import annotations
from dataclasses import dataclass
import bisect
import math
import os
import re
from typing import List, Optional, Tuple, Dict, Iterator


@dataclass
class Parca:
    """Tek bir video parçasını temsil eder."""
    dosya: str
    baslangic_zamani: Optional[float]  # epoch sn (None olabilir)
    sure_sn: float


@dataclass
class Dilim:
    """Sanal zaman çizelgesindeki tek bir dilimi temsil eder."""
    dosya: Optional[str]  # dosyasız boşluk için None
    sanal_bas: float
    sanal_son: float
    yerel_bas: float
    tur: str  # "kayit" | "bosluk" | "cakisma_kirpildi"

    @property
    def sure(self) -> float:
        """Dilimin sanal süresi (sn)."""
        return self.sanal_son - self.sanal_bas

    @property
    def yerel_son(self) -> float:
        """Dilimin dosya içi yerel bitiş saniyesi."""
        return self.yerel_bas + self.sure


def dogal_siralama_anahtari(metin: str) -> list:
    """
    Doğal sıralama anahtarı üretir (ör. 'ders_2' < 'ders_10').
    Metindeki sayı öbeklerini tam sayıya dönüştürür.
    """
    parcalar = re.split(r'(\d+)', metin)
    anahtar = []
    for p in parcalar:
        if not p:
            continue
        if p.isdigit():
            anahtar.append((0, int(p)))
        else:
            anahtar.append((1, p.lower()))
    return anahtar


def parca_siralama_anahtari(parca: Parca) -> tuple:
    """
    Parçaları öncelikle başlangıç zamanına, yoksa doğal dosya adı sırasına göre sıralar.
    """
    taban_ad = os.path.basename(parca.dosya)
    ad_anahtari = (dogal_siralama_anahtari(taban_ad), dogal_siralama_anahtari(parca.dosya))
    if parca.baslangic_zamani is not None:
        return (0, parca.baslangic_zamani, ad_anahtari)
    return (1, 0.0, ad_anahtari)


class SanalZamanCizelgesi:
    """
    Kamera parçalarından oluşturulan sanal zaman çizelgesi.
    Dilimleri sıralı olarak barındırır ve iki yönlü zaman eşlemesi sağlar.
    """

    def __init__(self, parcalar: Optional[List[Parca]] = None):
        self.dilimler: List[Dilim] = []
        self.uyarilar: List[str] = []
        self._sanal_baslangiclar: List[float] = []
        self._dosya_dilim_haritasi: Dict[Optional[str], List[Dilim]] = {}

        if parcalar is not None:
            self._cizelge_olustur(parcalar)

    def _cizelge_olustur(self, parcalar: List[Parca]) -> None:
        """Parça listesini işleyerek dilimleri ve uyarıları oluşturur."""
        self.dilimler.clear()
        self.uyarilar.clear()

        # 1. Süresi 0 veya negatif olan parçaları filtrele
        gecerli_parcalar: List[Parca] = []
        for p in parcalar:
            if p.sure_sn <= 0:
                self.uyarilar.append(
                    f"Geçersiz süre ({p.sure_sn} sn) nedeniyle parça atlandı: {p.dosya}"
                )
                continue
            gecerli_parcalar.append(p)

        if not gecerli_parcalar:
            self._indeksleri_hazirla()
            return

        # 2. Kontrol: Tüm parçaların başlangıç zamanları eşit mi?
        tum_zamanlar_esit = (
            len(gecerli_parcalar) > 1
            and all(
                p.baslangic_zamani is not None
                and p.baslangic_zamani == gecerli_parcalar[0].baslangic_zamani
                for p in gecerli_parcalar
            )
        )
        if tum_zamanlar_esit:
            self.uyarilar.append(
                "Tüm parçaların başlangıç zamanları eşit olduğu için doğal ad sırasına göre zincirlendi."
            )
            sirali = sorted(
                gecerli_parcalar,
                key=lambda p: (
                    dogal_siralama_anahtari(os.path.basename(p.dosya)),
                    dogal_siralama_anahtari(p.dosya),
                ),
            )
            sanal_imlec = 0.0
            for p in sirali:
                dilim = Dilim(
                    dosya=p.dosya,
                    sanal_bas=sanal_imlec,
                    sanal_son=sanal_imlec + p.sure_sn,
                    yerel_bas=0.0,
                    tur="kayit",
                )
                self.dilimler.append(dilim)
                sanal_imlec += p.sure_sn
            self._indeksleri_hazirla()
            return

        # 3. Sıralama: başlangıç zamanı, yoksa doğal dosya adı
        sirali = sorted(gecerli_parcalar, key=parca_siralama_anahtari)

        # 4. Dilimleri oluştur
        sanal_imlec = 0.0
        son_gercek_son: Optional[float] = None

        for p in sirali:
            if p.baslangic_zamani is None or son_gercek_son is None:
                # Zaman bilgisi yoksa veya ilk parçaysa
                if p.baslangic_zamani is not None:
                    son_gercek_son = p.baslangic_zamani + p.sure_sn

                dilim = Dilim(
                    dosya=p.dosya,
                    sanal_bas=sanal_imlec,
                    sanal_son=sanal_imlec + p.sure_sn,
                    yerel_bas=0.0,
                    tur="kayit",
                )
                self.dilimler.append(dilim)
                sanal_imlec += p.sure_sn
                continue

            fark = p.baslangic_zamani - son_gercek_son

            if fark > 0.5:
                # Boşluk durumu (> 0.5 sn) -> dosyasız boşluk dilimi
                bosluk_dilimi = Dilim(
                    dosya=None,
                    sanal_bas=sanal_imlec,
                    sanal_son=sanal_imlec + fark,
                    yerel_bas=0.0,
                    tur="bosluk",
                )
                self.dilimler.append(bosluk_dilimi)
                sanal_imlec += fark

                # Ardından kayıt dilimi
                kayit_dilimi = Dilim(
                    dosya=p.dosya,
                    sanal_bas=sanal_imlec,
                    sanal_son=sanal_imlec + p.sure_sn,
                    yerel_bas=0.0,
                    tur="kayit",
                )
                self.dilimler.append(kayit_dilimi)
                sanal_imlec += p.sure_sn
                son_gercek_son = p.baslangic_zamani + p.sure_sn

            elif fark >= 0.0:
                # Bitişik veya önemsiz küçük fark (<= 0.5 sn)
                kayit_dilimi = Dilim(
                    dosya=p.dosya,
                    sanal_bas=sanal_imlec,
                    sanal_son=sanal_imlec + p.sure_sn,
                    yerel_bas=0.0,
                    tur="kayit",
                )
                self.dilimler.append(kayit_dilimi)
                sanal_imlec += p.sure_sn
                son_gercek_son = p.baslangic_zamani + p.sure_sn

            else:
                # Çakışma durumu (fark < 0.0)
                cakisma = -fark

                # Çakışma parça süresine eşit veya büyükse parça tamamen atlanır
                if cakisma >= p.sure_sn - 1e-9:
                    self.uyarilar.append(
                        f"Çakışma ({cakisma:.3f} sn >= {p.sure_sn:.3f} sn) nedeniyle parça atlandı: {p.dosya}"
                    )
                    continue

                # Kısmi çakışma: sonraki parça başı kırpılır (yerel_bas artar)
                yerel_bas = cakisma
                kalan_sure = p.sure_sn - cakisma
                kirpildi_dilimi = Dilim(
                    dosya=p.dosya,
                    sanal_bas=sanal_imlec,
                    sanal_son=sanal_imlec + kalan_sure,
                    yerel_bas=yerel_bas,
                    tur="cakisma_kirpildi",
                )
                self.dilimler.append(kirpildi_dilimi)
                sanal_imlec += kalan_sure
                son_gercek_son = p.baslangic_zamani + p.sure_sn

        self._indeksleri_hazirla()

    def _indeksleri_hazirla(self) -> None:
        """Hızlı ikili arama için başlangıç noktalarını ve dosya eşlemelerini hazırlar."""
        self._sanal_baslangiclar = [d.sanal_bas for d in self.dilimler]
        self._dosya_dilim_haritasi = {}
        for d in self.dilimler:
            if d.dosya not in self._dosya_dilim_haritasi:
                self._dosya_dilim_haritasi[d.dosya] = []
            self._dosya_dilim_haritasi[d.dosya].append(d)

    @property
    def toplam_sure(self) -> float:
        """Çizelgenin toplam sanal süresi (sn)."""
        if not self.dilimler:
            return 0.0
        return self.dilimler[-1].sanal_son

    @property
    def toplam_sure_sn(self) -> float:
        """Çizelgenin toplam sanal süresi (sn) alternatif erişim."""
        return self.toplam_sure

    def sanal_to_yerel(self, sanal_sn: float) -> Tuple[Optional[str], float]:
        """
        Sanal zaman noktasını (dosya, yerel_sn) ikilisine dönüştürür.
        İkili arama kullanır. Sınır noktalarında ileri parçayı seçer.
        """
        if not self.dilimler:
            raise ValueError("Çizelgede dilim bulunmuyor.")

        eps = 1e-9
        if sanal_sn < -eps or sanal_sn > self.toplam_sure + eps:
            raise ValueError(
                f"Sanal zaman sınırların dışında: {sanal_sn} (geçerli aralık: 0.0 - {self.toplam_sure})"
            )

        if sanal_sn < 0.0:
            sanal_sn = 0.0
        elif sanal_sn > self.toplam_sure:
            sanal_sn = self.toplam_sure

        # Tam bitiş noktasıysa son dilim
        if math.isclose(sanal_sn, self.toplam_sure, abs_tol=eps) and sanal_sn >= self.toplam_sure - eps:
            son_dilim = self.dilimler[-1]
            yerel_sn = son_dilim.yerel_bas + (sanal_sn - son_dilim.sanal_bas)
            return (son_dilim.dosya, yerel_sn)

        # İkili arama
        idx = bisect.bisect_right(self._sanal_baslangiclar, sanal_sn) - 1
        idx = max(0, min(idx, len(self.dilimler) - 1))

        # Sınırlarda ileri parça kuralı: dilim sonu ile sonraki dilim başı kesişiminde sonraki dilim seçilir
        if idx + 1 < len(self.dilimler) and (sanal_sn >= self.dilimler[idx].sanal_son - eps):
            idx += 1

        secilen = self.dilimler[idx]
        ofset = sanal_sn - secilen.sanal_bas
        yerel_sn = secilen.yerel_bas + ofset
        return (secilen.dosya, yerel_sn)

    def yerel_to_sanal(self, dosya: Optional[str], yerel_sn: float) -> float:
        """
        (dosya, yerel_sn) ikilisini sanal zamana dönüştürür.
        İkili arama kullanır. Sınır noktalarında ileri parçayı seçer.
        """
        if not self.dilimler:
            raise ValueError("Çizelgede dilim bulunmuyor.")

        if dosya not in self._dosya_dilim_haritasi:
            raise ValueError(f"Dosya çizelgede bulunamadı: {dosya}")

        dosya_dilimleri = self._dosya_dilim_haritasi[dosya]
        eps = 1e-9

        yerel_baslangiclar = [d.yerel_bas for d in dosya_dilimleri]

        idx = bisect.bisect_right(yerel_baslangiclar, yerel_sn) - 1
        idx = max(0, min(idx, len(dosya_dilimleri) - 1))

        # Sınırlarda ileri parça kuralı
        if idx + 1 < len(dosya_dilimleri):
            mevcut_yerel_son = dosya_dilimleri[idx].yerel_son
            if yerel_sn >= mevcut_yerel_son - eps:
                idx += 1

        secilen = dosya_dilimleri[idx]

        if yerel_sn < secilen.yerel_bas - eps or yerel_sn > secilen.yerel_son + eps:
            raise ValueError(
                f"Yerel zaman sınırların dışında: {yerel_sn} "
                f"(geçerli yerel aralık: {secilen.yerel_bas} - {secilen.yerel_son})"
            )

        ofset = yerel_sn - secilen.yerel_bas
        return secilen.sanal_bas + ofset

    # Fonksiyon takma adları
    sanal_esle = sanal_to_yerel
    sanal_dan_yerel = sanal_to_yerel
    sanal_yerel_esle = sanal_to_yerel

    yerel_esle = yerel_to_sanal
    yerelden_sanal = yerel_to_sanal
    yerel_sanal_esle = yerel_to_sanal

    def __len__(self) -> int:
        return len(self.dilimler)

    def __getitem__(self, index: int) -> Dilim:
        return self.dilimler[index]

    def __iter__(self) -> Iterator[Dilim]:
        return iter(self.dilimler)


# Kolaylık ve uyumluluk tanımlayıcıları
SanalZaman = SanalZamanCizelgesi


def cizelge_olustur(parcalar: List[Parca]) -> SanalZamanCizelgesi:
    """Parça listesinden sanal zaman çizelgesi oluşturur."""
    return SanalZamanCizelgesi(parcalar)


def parca_zinciri_olustur(parcalar: List[Parca]) -> SanalZamanCizelgesi:
    """Parça listesinden sanal zaman çizelgesi oluşturur."""
    return SanalZamanCizelgesi(parcalar)


def zincir_olustur(parcalar: List[Parca]) -> SanalZamanCizelgesi:
    """Parça listesinden sanal zaman çizelgesi oluşturur."""
    return SanalZamanCizelgesi(parcalar)
