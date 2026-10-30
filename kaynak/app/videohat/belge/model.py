"""Belge hattının taşınabilir kayıtları (görüntü verisi içermez)."""
from dataclasses import dataclass, field
from pathlib import Path


@dataclass
class OcrSatir:
    metin: str
    guven: float
    kutu: tuple[float, float, float, float]


class SatirListesi(list):
    metin: str = ""
    yon: int = 0


@dataclass
class Sayfa:
    no: int
    etiket: str
    kaynak: str
    metin: str
    guven: float | None = None
    satirlar: list[OcrSatir] = field(default_factory=list)
    gorsel_boyut: tuple[int, int] | None = None
    yon: int = 0  # OCR'da bulunan sayfa yönü; önbellekten devamda aranabilir PDF için gerekli


@dataclass
class BelgeSonucu:
    bicim: str
    sayfalar: list[Sayfa] = field(default_factory=list)
    uyarilar: list[str] = field(default_factory=list)
    aranabilir_pdf: Path | None = None
    durum: str = "tamam"
    hata: str | None = None


@dataclass
class BelgeKaydi:
    sha: str
    hedef: Path
    veri: dict

    @property
    def basarili(self):
        return self.veri.get("durum", "tamam") == "tamam"

    @property
    def hata(self):
        return self.veri.get("hata")

    @property
    def sureler(self):
        return {"belge": self.veri.get("sure_sn", 0)}
