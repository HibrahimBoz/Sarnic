"""Sarnıç Atölye - Çok kameralı ders tanımı (ders.json) saf mantık modülü."""
from __future__ import annotations

from dataclasses import dataclass, field
import json
import os
from pathlib import Path
from typing import Any, List
import uuid


@dataclass
class ParcaKaydi:
    """Tek bir video parçasının çizelge ve senkron kaydı."""
    dosya: str  # göreli yol
    sanal_bas: float
    sanal_son: float
    yerel_bas: float
    tur: str
    ofset_sn: float = 0.0
    guven: float = 1.0

    def to_dict(self) -> dict[str, Any]:
        return {
            "dosya": self.dosya,
            "sanal_bas": round(self.sanal_bas, 4),
            "sanal_son": round(self.sanal_son, 4),
            "yerel_bas": round(self.yerel_bas, 4),
            "tur": self.tur,
            "ofset_sn": round(self.ofset_sn, 4),
            "guven": round(self.guven, 4),
        }

    @classmethod
    def from_dict(cls, veri: dict[str, Any]) -> ParcaKaydi:
        zorunlu = ["dosya", "sanal_bas", "sanal_son", "yerel_bas", "tur", "ofset_sn", "guven"]
        for alan in zorunlu:
            if alan not in veri:
                raise ValueError(f"ParcaKaydi eksik alan: {alan}")
        return cls(
            dosya=str(veri["dosya"]),
            sanal_bas=float(veri["sanal_bas"]),
            sanal_son=float(veri["sanal_son"]),
            yerel_bas=float(veri["yerel_bas"]),
            tur=str(veri["tur"]),
            ofset_sn=float(veri["ofset_sn"]),
            guven=float(veri["guven"]),
        )


@dataclass
class Kanal:
    """Bir kameraya/kanala ait parçalar ve senkron bilgisi."""
    ad: str
    parcalar: List[ParcaKaydi]
    ofset_sn: float = 0.0
    drift_ppm: float = 0.0
    guven: float = 1.0
    guvenilir: bool = True
    neden: str = "basarili"

    def to_dict(self) -> dict[str, Any]:
        return {
            "ad": self.ad,
            "ofset_sn": round(self.ofset_sn, 4),
            "drift_ppm": round(self.drift_ppm, 4),
            "guven": round(self.guven, 4),
            "guvenilir": bool(self.guvenilir),
            "neden": self.neden,
            "parcalar": [p.to_dict() for p in self.parcalar],
        }

    @classmethod
    def from_dict(cls, veri: dict[str, Any]) -> Kanal:
        for alan in ["ad", "parcalar"]:
            if alan not in veri:
                raise ValueError(f"Kanal eksik alan: {alan}")
        parcalar_ham = veri["parcalar"]
        if not isinstance(parcalar_ham, list):
            raise ValueError("Kanal 'parcalar' bir liste olmalıdır.")
        parcalar = [ParcaKaydi.from_dict(p) for p in parcalar_ham]
        return cls(
            ad=str(veri["ad"]),
            parcalar=parcalar,
            ofset_sn=float(veri.get("ofset_sn", 0.0)),
            drift_ppm=float(veri.get("drift_ppm", 0.0)),
            guven=float(veri.get("guven", 1.0)),
            guvenilir=bool(veri.get("guvenilir", True)),
            neden=str(veri.get("neden", "basarili")),
        )


@dataclass
class SenkronBilgisi:
    """Genel ders senkronizasyon özeti."""
    yontem: str = "gcc_phat"
    drift_ppm: float = 0.0
    guven: float = 1.0
    guvenilir: bool = True
    neden: str = "basarili"

    def to_dict(self) -> dict[str, Any]:
        return {
            "yontem": self.yontem,
            "drift_ppm": round(self.drift_ppm, 4),
            "guven": round(self.guven, 4),
            "guvenilir": bool(self.guvenilir),
            "neden": self.neden,
        }

    @classmethod
    def from_dict(cls, veri: dict[str, Any]) -> SenkronBilgisi:
        zorunlu = ["yontem", "drift_ppm", "guven", "guvenilir", "neden"]
        for alan in zorunlu:
            if alan not in veri:
                raise ValueError(f"SenkronBilgisi eksik alan: {alan}")
        return cls(
            yontem=str(veri["yontem"]),
            drift_ppm=float(veri["drift_ppm"]),
            guven=float(veri["guven"]),
            guvenilir=bool(veri["guvenilir"]),
            neden=str(veri["neden"]),
        )


Senkron = SenkronBilgisi


@dataclass
class DersTanimi:
    """Çok kameralı ders tanımı."""
    ad: str
    kanallar: List[Kanal]
    referans_kanal: str
    senkron: SenkronBilgisi
    elle_isaretler: List[Any] = field(default_factory=list)
    sema_surumu: int = 1

    def to_dict(self) -> dict[str, Any]:
        return {
            "sema_surumu": self.sema_surumu,
            "ad": self.ad,
            "referans_kanal": self.referans_kanal,
            "senkron": self.senkron.to_dict() if hasattr(self.senkron, "to_dict") else dict(self.senkron),
            "kanallar": [k.to_dict() for k in self.kanallar],
            "elle_isaretler": list(self.elle_isaretler),
        }

    @classmethod
    def from_dict(cls, veri: dict[str, Any]) -> DersTanimi:
        dogrula(veri)
        kanallar = [Kanal.from_dict(k) for k in veri["kanallar"]]
        senkron_veri = veri["senkron"]
        if isinstance(senkron_veri, dict):
            senkron = SenkronBilgisi.from_dict(senkron_veri)
        elif isinstance(senkron_veri, SenkronBilgisi):
            senkron = senkron_veri
        else:
            raise ValueError("Geçersiz senkron alanı.")

        return cls(
            ad=str(veri["ad"]),
            kanallar=kanallar,
            referans_kanal=str(veri["referans_kanal"]),
            senkron=senkron,
            elle_isaretler=list(veri["elle_isaretler"]),
            sema_surumu=int(veri["sema_surumu"]),
        )


def dogrula(veri: dict[str, Any]) -> None:
    """Verilen sözlüğün ders.json şemasına uygunluğunu doğrular."""
    if not isinstance(veri, dict):
        raise ValueError("Şema hatası: kök veri sözlük olmalıdır.")

    zorunlu_kok = ["ad", "kanallar", "referans_kanal", "senkron", "elle_isaretler", "sema_surumu"]
    for alan in zorunlu_kok:
        if alan not in veri:
            raise ValueError(f"Şema hatası: eksik alan '{alan}'")

    if not isinstance(veri["kanallar"], list):
        raise ValueError("Şema hatası: 'kanallar' liste olmalıdır.")

    for k in veri["kanallar"]:
        if not isinstance(k, dict):
            raise ValueError("Şema hatası: kanal öğesi sözlük olmalıdır.")
        for alan in ["ad", "parcalar"]:
            if alan not in k:
                raise ValueError(f"Şema hatası: kanalda eksik alan '{alan}'")
        if not isinstance(k["parcalar"], list):
            raise ValueError("Şema hatası: 'parcalar' liste olmalıdır.")
        for p in k["parcalar"]:
            if not isinstance(p, dict):
                raise ValueError("Şema hatası: parça öğesi sözlük olmalıdır.")
            for alan in ["dosya", "sanal_bas", "sanal_son", "yerel_bas", "tur", "ofset_sn", "guven"]:
                if alan not in p:
                    raise ValueError(f"Şema hatası: parçada eksik alan '{alan}'")

    senkron = veri["senkron"]
    if not isinstance(senkron, dict):
        raise ValueError("Şema hatası: 'senkron' sözlük olmalıdır.")
    for alan in ["yontem", "drift_ppm", "guven", "guvenilir", "neden"]:
        if alan not in senkron:
            raise ValueError(f"Şema hatası: senkron nesnesinde eksik alan '{alan}'")


def yaz(yol: str | Path, ders: DersTanimi) -> None:
    """DersTanimi nesnesini atomik olarak UTF-8 JSON dosyasına yazar."""
    yol = Path(yol)
    yol.parent.mkdir(parents=True, exist_ok=True)
    veri = ders.to_dict()
    icerik = json.dumps(veri, ensure_ascii=False, indent=2) + "\n"

    gecici_yol = yol.parent / f"{yol.name}.{os.getpid()}.{uuid.uuid4().hex[:8]}.tmp"
    try:
        gecici_yol.write_text(icerik, encoding="utf-8")
        os.replace(gecici_yol, yol)
    finally:
        if gecici_yol.exists():
            try:
                gecici_yol.unlink()
            except OSError:
                pass


def oku(yol: str | Path) -> DersTanimi:
    """JSON dosyasını okur, şemayı doğrular ve DersTanimi nesnesi döndürür."""
    yol = Path(yol)
    if not yol.exists():
        raise FileNotFoundError(f"Dosya bulunamadı: {yol}")
    metin = yol.read_text(encoding="utf-8")
    veri = json.loads(metin)
    dogrula(veri)
    return DersTanimi.from_dict(veri)


def kanal_zamani(ders: DersTanimi, kanal_ad: str, sanal_sn: float) -> float:
    """Sanal zamandan ilgili kanalın yerel zamanına dönüşüm yapar."""
    kanal = next((k for k in ders.kanallar if k.ad == kanal_ad), None)
    if kanal is None:
        raise ValueError(f"Kanal bulunamadı: {kanal_ad}")

    drift_fark = kanal.drift_ppm * 1e-6
    return float(sanal_sn * (1.0 + drift_fark) + kanal.ofset_sn)


def sanal_zaman(ders: DersTanimi, kanal_ad: str, yerel_sn: float) -> float:
    """İlgili kanalın yerel zamanından sanal zamana dönüşüm yapar."""
    kanal = next((k for k in ders.kanallar if k.ad == kanal_ad), None)
    if kanal is None:
        raise ValueError(f"Kanal bulunamadı: {kanal_ad}")

    drift_fark = kanal.drift_ppm * 1e-6
    payda = 1.0 + drift_fark
    if abs(payda) < 1e-12:
        raise ZeroDivisionError("Drift değeri geçersiz.")
    return float((yerel_sn - kanal.ofset_sn) / payda)


kanal_zamani_tersi = sanal_zaman
