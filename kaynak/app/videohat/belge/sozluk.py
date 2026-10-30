"""Sabit maliyetli, yalnız Türkçe harf karışmalarını düzelten sözlük."""
import re
from pathlib import Path

from ..dizin.normal import icerik_normalle
from ..terimler import kucuk

_SOZCUK = re.compile(r"[^\W_]+", re.UNICODE)
_CIFTLER = [set(p) for p in ("iı", "sş", "gğ", "cç", "oö", "uü")]


def _buyuk(s):
    return s.replace("i", "İ").replace("ı", "I").upper()


class Sozluk:
    def __init__(self, yol: Path | None = None, terimler: Path | None = None):
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

    def duzelt(self, metin: str, guven: float) -> str:
        if guven >= 0.85:
            return metin

        def yerine(m):
            ham = m.group()
            kelime = kucuk(ham)
            if (len(kelime) < 2 or any(c.isdigit() for c in ham)
                    or ham.isupper() or kelime in self.birebir):
                return ham
            aday = self.indeks.get(icerik_normalle(kelime))
            if not aday or len(aday) != len(kelime):
                return ham
            if any(a != b and not any(a in c and b in c for c in _CIFTLER)
                   for a, b in zip(aday, kelime)):
                return ham
            return "".join(_buyuk(a) if b.isupper() else a for a, b in zip(aday, ham))

        return _SOZCUK.sub(yerine, metin)
