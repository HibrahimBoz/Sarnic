"""Sabit maliyetli, yalnız Türkçe harf karışmalarını düzelten sözlük."""
import re
from pathlib import Path

from ..dizin.normal import icerik_normalle
from ..terimler import kucuk

_SOZCUK = re.compile(r"[^\W_]+", re.UNICODE)
_KALIN, _INCE = set("aıou"), set("eiöü")
# Ünlü uyumuna uymayan ekler: bunlarla biten sözcüğe uyum kuralı uygulanmaz.
_UYUMSUZ_EKLER = ("yor", "yordu", "yorlar", "ken", "ki", "leyin", "gil")
# OCR Türkçe işareti düşürür, nadiren yanlış ekler: yalnız işaret EKLEYEN değişime izin verilir (ı->i yasak).
_ISARET_EKLE = {("i", "ı"), ("s", "ş"), ("g", "ğ"), ("c", "ç"), ("o", "ö"), ("u", "ü")}


def _izinli(aday: str, ham: str) -> bool:
    """aday (küçük harf) ham'ın (özgün yazım) yalnız işaret eklenmiş biçimi mi."""
    for a, h in zip(aday, ham):
        k = "i" if h == "İ" else ("ı" if h == "I" else h.lower())
        if a == k or (h == "I" and a == "i"):  # büyük I, İ'nin noktasız okunuşu olabilir
            continue
        if (k, a) not in _ISARET_EKLE:
            return False
    return True


def _buyuk(s):
    return s.replace("i", "İ").replace("ı", "I").upper()


class Sozluk:
    def __init__(self, yol: Path | None = None, terimler: Path | None = None, unlu_uyumu: bool = True):
        self.unlu_uyumu = unlu_uyumu
        self.indeks = {}
        self.birebir = set()
        self._siklik = {}
        if yol and yol.is_file():
            with yol.open(encoding="utf-8-sig") as f:
                for satir in f:
                    parcalar = satir.strip().rsplit(None, 1)
                    if not parcalar or parcalar[0].startswith("#"):
                        continue
                    try:
                        siklik = int(parcalar[1]) if len(parcalar) == 2 else 1
                    except ValueError:
                        siklik = 1
                    self.ekle(parcalar[0], siklik)
        if terimler and terimler.is_file():
            for satir in terimler.read_text(encoding="utf-8-sig").splitlines():
                s = satir.strip()
                if not s or s.startswith(("#", "[")):
                    continue
                s = s.split("=>", 1)[-1]
                for kelime in _SOZCUK.findall(s):
                    self.ekle(kelime)

    def ekle(self, kelime: str, siklik: int = 1):
        kelime = kucuk(kelime)
        anahtar = icerik_normalle(kelime)
        self.birebir.add(kelime)
        if siklik > self._siklik.get(anahtar, -1):
            self.indeks[anahtar] = kelime
            self._siklik[anahtar] = siklik

    def oran(self, metin: str) -> float:
        kelimeler = _SOZCUK.findall(metin)
        return (sum(kucuk(k) in self.birebir for k in kelimeler) / len(kelimeler)
                if kelimeler else 0.0)

    def puan(self, metin: str, guven: float) -> float:
        return guven + 0.15 * self.oran(metin)

    def _uyumla(self, kelime: str) -> str | None:
        """Kök + ek: en uzun sözlük kökünü bulur, yalnız ekteki ı/i'yi ünlü uyumuyla belirler.

        Ölçüldü (tr_50k, 27.015 sözcük, ı->i bozulması benzetildi): %79,9 düzeltir, doğru sözcüğün %3,6'sını bozar.
        """
        anahtar = icerik_normalle(kelime)
        if anahtar.endswith(_UYUMSUZ_EKLER) or len(anahtar) != len(kelime):
            return None
        for n in range(len(anahtar) - 2, 3, -1):
            kok = self.indeks.get(anahtar[:n])
            if not kok or len(kok) != n:
                continue
            unluler = [c for c in kok if c in _KALIN or c in _INCE]
            if not unluler:
                return None
            onceki, sonuc = unluler[-1], list(kok)
            for c in kelime[n:]:
                if c in "iı":
                    c = "ı" if onceki in _KALIN else "i"
                if c in _KALIN or c in _INCE:
                    onceki = c
                sonuc.append(c)
            return "".join(sonuc)
        return None

    def duzelt(self, metin: str, guven: float) -> str:
        # Ölçüldü: 150 dpi taramada ı/i karışan satırların güveni 0,96 çıktı; eşik yalnız kusursuz satırı atlar.
        if guven >= 0.99:
            return metin
        metin = metin.replace("ș", "ş").replace("Ș", "Ş")  # Rumence virgüllü s, Türkçe ş ile karışır

        def yerine(m):
            ham = m.group()
            # Büyük harfli sözcükte küçük i/ı OCR karışmasıdır: "GiZLiLiK" -> "GİZLİLİK"
            harfler = [c for c in ham if c not in "iı"]
            if len(ham) > 2 and harfler and all(c.isupper() for c in harfler) and ham != "".join(harfler):
                ham = ham.replace("i", "İ").replace("ı", "I")
            kelime = kucuk(ham)
            if (len(kelime) < 2 or any(c.isdigit() for c in ham)
                    or (ham.isupper() and len(ham) <= 4) or kelime in self.birebir):
                return ham
            if ham.isupper():
                aday = self.indeks.get(icerik_normalle(kelime))
                if aday and len(aday) == len(kelime) and _izinli(aday, ham):
                    return _buyuk(aday)
                return ham
            aday = self.indeks.get(icerik_normalle(kelime))
            if not aday and self.unlu_uyumu:
                aday = self._uyumla(kelime)
            if not aday or len(aday) != len(kelime):
                return ham
            if not _izinli(aday, ham):
                return ham
            return "".join(_buyuk(a) if b.isupper() else a for a, b in zip(aday, ham))

        return _SOZCUK.sub(yerine, metin)
