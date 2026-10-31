"""Türkçe dışı dillerden (en, az, ru) Türkçeye çeviri çekirdeği.

NLLB-200 distilled 1.3B (CTranslate2 int8) modeli kullanılır.
Tembel yükleme: ctranslate2 ve sentencepiece yalnızca gerçek çeviri
yapılacağı zaman içe aktarılır.
"""
from __future__ import annotations

import hashlib
import logging
from pathlib import Path
import re
from typing import TYPE_CHECKING, Any

from . import yollar
from .hatalar import CeviriHatasi

log = logging.getLogger("videohat")

if TYPE_CHECKING:
    from .ayar import Ayar

# NLLB-200 dil kodları
DIL_KODLARI: dict[str, str] = {
    "en": "eng_Latn",
    "az": "azj_Latn",
    "ru": "rus_Cyrl",
    "tr": "tur_Latn",
}

# Cümle bölücüde nokta sonrası bölmeyi engelleyen kısaltmalar (küçük harf)
KISALTMALAR: set[str] = {
    "dr", "prof", "mr", "mrs", "ms", "no", "vs", "vb", "bkz", "örn",
    "st", "jr", "г", "т", "др", "им"
}

_ROMEN_RAKAMI_RE = re.compile(r"^[ivxlcdm]+$", re.IGNORECASE)

# Durak sözcükleri (küçük harf)
_TR_DURAK: set[str] = {
    "ve", "bir", "bu", "için", "ile", "olarak", "da", "de", "çok", "daha", "gibi", "olan"
}
_EN_DURAK: set[str] = {
    "the", "and", "of", "to", "is", "in", "that", "for", "with", "as", "are", "on"
}
_AZ_DURAK: set[str] = {
    "və", "bir", "bu", "üçün", "ilə", "olan", "edir", "deyil", "kimi", "daha", "çox"
}

_TURKCE_OZEL_HARFLER: set[str] = set("ğışİçöüĞIŞÇÖÜ")


def _parcala_400(metin: str) -> list[str]:
    """400 karakteri aşan parçayı son boşlukta, boşluk yoksa 400'lük sabit dilimle böler."""
    metin = metin.strip()
    if not metin:
        return []
    if len(metin) <= 400:
        return [metin]

    sonuclar: list[str] = []
    kalan = metin
    while len(kalan) > 400:
        dilim = kalan[:401]
        son_bosluk = dilim.rfind(" ")
        if son_bosluk > 0:
            parca = kalan[:son_bosluk].strip()
            kalan = kalan[son_bosluk:].strip()
        else:
            parca = kalan[:400]
            kalan = kalan[400:].strip()
        if parca:
            sonuclar.append(parca)
    if kalan:
        sonuclar.append(kalan)
    return sonuclar


def cumlelere_bol(metin: str) -> list[str]:
    """Metni cümlelere böler.

    [.!?…]+\\s+ ve boş satır (\\n\\s*\\n) böler; tek \\n bölmez, boşluğa çevrilir.
    Noktadan önceki sözcük Romen rakamı, sayı ya da kapalı kısaltma listesindeyse bölmez.
    400 karakteri aşan parça son boşlukta, boşluk yoksa 400 karakterlik sabit dilimle bölünür.
    """
    if not metin or not metin.strip():
        return []

    # Boş satırlarla (\n\s*\n+) paragraflara ayır
    paragraflar = re.split(r"\n\s*\n+", metin.strip())
    ham_cumleler: list[str] = []

    for paragraf in paragraflar:
        # Tek satır sonlarını (\n) boşluğa çevir
        p = re.sub(r"(?<!\n)\n(?!\n)", " ", paragraf).strip()
        if not p:
            continue

        baslangic = 0
        for eslesme in re.finditer(r"([.!?…]+)(\s+)", p):
            noktalama = eslesme.group(1)
            isaret_sonu = eslesme.end(1)
            bosluk_sonu = eslesme.end(2)

            # Nokta içeriyorsa solundaki sözcüğü kontrol et
            if "." in noktalama:
                sol_metin = p[baslangic:eslesme.start(1)]
                kelimeler = sol_metin.split()
                if kelimeler:
                    son_kelime = kelimeler[-1]
                    temiz_kelime = re.sub(r"^[^\w]+|[^\w]+$", "", son_kelime)
                    kucuk = temiz_kelime.lower()
                    if (kucuk in KISALTMALAR or
                        temiz_kelime.isdigit() or
                        bool(_ROMEN_RAKAMI_RE.fullmatch(kucuk))):
                        continue

            cumle = p[baslangic:isaret_sonu].strip()
            if cumle:
                ham_cumleler.append(cumle)
            baslangic = bosluk_sonu

        kalan = p[baslangic:].strip()
        if kalan:
            ham_cumleler.append(kalan)

    # 400 karakter sınırını uygula
    sonuc: list[str] = []
    for c in ham_cumleler:
        sonuc.extend(_parcala_400(c))
    return sonuc


def dil_tahmin(metin: str) -> str:
    """Metnin dilini tahmin eder ('tr', 'ru', 'az', 'en').

    Sırayla:
    1. harf < 5 -> 'tr'
    2. Kiril harf oranı >= 0.5 -> 'ru'
    3. ə/Ə var ya da (harf >= 20 ve (q+x) oranı >= 0.01) -> 'az'
    4. durak sözcüğü sayımı: tr / en / az — en yüksek sayı kazanır
    5. eşitlikte ya da hepsi 0 ise Türkçe harf oranı >= 0.02 -> 'tr', değilse 'en'
    """
    harfler = [c for c in metin if c.isalpha()]
    toplam_harf = len(harfler)
    if toplam_harf < 5:
        return "tr"

    # Kiril oranı
    kiril_harf_sayisi = sum(1 for c in harfler if "\u0400" <= c <= "\u04ff")
    if kiril_harf_sayisi / toplam_harf >= 0.5:
        return "ru"

    # Azerice kuralı
    qx_sayisi = sum(1 for c in metin.lower() if c in ("q", "x"))
    if ("ə" in metin or "Ə" in metin) or (toplam_harf >= 20 and (qx_sayisi / toplam_harf >= 0.01)):
        return "az"

    # Durak sözcüğü sayımı
    metin_kucuk = metin.lower()
    sozcukler = re.findall(r"\b\w+\b", metin_kucuk, flags=re.UNICODE)
    tr_skor = sum(1 for s in sozcukler if s in _TR_DURAK)
    en_skor = sum(1 for s in sozcukler if s in _EN_DURAK)
    az_skor = sum(1 for s in sozcukler if s in _AZ_DURAK)

    en_yuksek = max(tr_skor, en_skor, az_skor)
    if en_yuksek > 0:
        kazananlar = []
        if tr_skor == en_yuksek:
            kazananlar.append("tr")
        if en_skor == en_yuksek:
            kazananlar.append("en")
        if az_skor == en_yuksek:
            kazananlar.append("az")
        if len(kazananlar) == 1:
            return kazananlar[0]

    # Eşitlik ya da hepsi 0
    turkce_harf_sayisi = sum(1 for c in metin if c in _TURKCE_OZEL_HARFLER)
    if turkce_harf_sayisi / toplam_harf >= 0.02:
        return "tr"
    return "en"


class Cevirmen:
    """NLLB-200 çeviri modeli ile metinleri Türkçeye çevirir."""

    def __init__(
        self,
        model_yolu: Path,
        cihaz: str = "cpu",
        compute_type: str = "int8",
        cevirici: Any = None,
        batch: int = 16,
    ) -> None:
        self.model_yolu = Path(model_yolu)
        self.model_ad = self.model_yolu.name
        self.batch = batch
        self.cihaz = cihaz
        self.compute_type = compute_type
        self._onbellek: dict[tuple[str, str], str] = {}

        if cevirici is not None:
            self.cevirici = cevirici
            self.sp = None
        else:
            sp_yolu = self.model_yolu / "sentencepiece.bpe.model"
            if not self.model_yolu.is_dir() or not (self.model_yolu / "model.bin").is_file() or not sp_yolu.is_file():
                raise CeviriHatasi(f"Çeviri modeli bulunamadı veya eksik: {self.model_yolu}")

            try:
                import ctranslate2
                import sentencepiece as spm
            except ImportError as e:
                raise CeviriHatasi(f"Çeviri kütüphaneleri yüklenemedi: {e}") from e

            try:
                self.cevirici = ctranslate2.Translator(
                    str(self.model_yolu),
                    device=self.cihaz,
                    compute_type=self.compute_type,
                )
                self.sp = spm.SentencePieceProcessor()
                self.sp.load(str(sp_yolu))
            except Exception as e:
                raise CeviriHatasi(f"Çeviri modeli başlatılamadı: {e}") from e

    def cevir(self, metinler: list[str], kaynak_dil: str) -> list[str]:
        """Metinleri Türkçeye çevirir; sıra ve uzunluk korunur.

        - kaynak_dil == 'tr' ise girdi aynen döner.
        - Boş ya da yalnız boşluk metin çevrilmez, aynen döner.
        - Aynı metin süreç içi önbellekten döner.
        - Bütün metinlerin cümleleri tek toplu çağrıda çevrilir (max_batch_size ile GPU belleği sınırlı).
        """
        if kaynak_dil == "tr":
            return list(metinler)
        kaynak_kod = DIL_KODLARI.get(kaynak_dil)
        if not kaynak_kod:
            raise CeviriHatasi(f"Desteklenmeyen çeviri dili: {kaynak_dil}")

        sonuclar: list[str | None] = [None] * len(metinler)
        bekleyen: list[tuple[int, tuple[str, str], int, int]] = []  # (sıra, anahtar, ilk parça, parça sayısı)
        parcalar: list[str] = []
        for i, metin in enumerate(metinler):
            if not metin or not metin.strip():
                sonuclar[i] = metin
                continue
            anahtar = (kaynak_dil, hashlib.sha256(metin.encode("utf-8")).hexdigest())
            if anahtar in self._onbellek:
                sonuclar[i] = self._onbellek[anahtar]
                continue
            metin_parcalari = cumlelere_bol(metin)
            bekleyen.append((i, anahtar, len(parcalar), len(metin_parcalari)))
            parcalar.extend(metin_parcalari)

        cevrilen = self._parcalari_cevir(parcalar, kaynak_kod)
        for i, anahtar, bas, adet in bekleyen:
            tam = " ".join(p for p in cevrilen[bas:bas + adet] if p)
            self._onbellek[anahtar] = tam
            sonuclar[i] = tam
        return [s if s is not None else "" for s in sonuclar]

    def _parcalari_cevir(self, parcalar: list[str], kaynak_kod: str) -> list[str]:
        """Parçaları NLLB ile çevirir; her parça için bir çıktı döner."""
        if not parcalar:
            return []
        belirtecler = [
            [kaynak_kod] + (self.sp.encode(p, out_type=str) if self.sp is not None else p.split()) + ["</s>"]
            for p in parcalar
        ]
        # Uzunluk sınırı parça başına değil çağrı başınadır; en uzun parçaya göre (Genel kısıtlar).
        max_dec = min(512, int(2.0 * max(len(b) for b in belirtecler) + 16))
        try:
            ceviriler = self.cevirici.translate_batch(
                belirtecler,
                target_prefix=[["tur_Latn"]] * len(belirtecler),
                max_batch_size=self.batch,
                beam_size=4,
                repetition_penalty=1.0,
                no_repeat_ngram_size=0,
                max_decoding_length=max_dec,
            )
        except Exception as e:
            raise CeviriHatasi(f"Çeviri işlemi sırasında hata: {e}") from e

        cikti: list[str] = []
        for sonuc in ceviriler:
            belirtec = list(sonuc.hypotheses[0])
            if belirtec and belirtec[0] == "tur_Latn":
                belirtec = belirtec[1:]
            metin = self.sp.decode(belirtec) if self.sp is not None else " ".join(belirtec)
            cikti.append(metin.strip())
        return cikti


def _cihaz() -> tuple[str, str]:
    """CUDA varsa (ve kapatılmadıysa) GPU, yoksa CPU. Whisper modeline bağlı değildir."""
    from . import cihaz
    if not cihaz._cuda_kapali:
        try:
            import ctranslate2
            if ctranslate2.get_cuda_device_count() > 0:
                return "cuda", "int8_float16"
        except Exception:
            pass
    return "cpu", "int8"


_HAZIR: dict[tuple[str, int], Cevirmen] = {}


def cevirmen_olustur(ayar: Ayar) -> Cevirmen | None:
    """Ayar etkinse çevirmeni bir kez yükler ve süreç boyunca aynı nesneyi döndürür.

    Kapalı ayarda ya da model yüklenemezse None döner ve günlüğe uyarı yazar; işleme durmaz.
    """
    ceviri_ayar = getattr(ayar, "ceviri", None)
    if ceviri_ayar is None or not ceviri_ayar.etkin:
        return None
    model_yolu = Path(ceviri_ayar.model) if ceviri_ayar.model.strip() else yollar.ceviri_modeli()
    anahtar = (str(model_yolu), ceviri_ayar.batch)
    if anahtar not in _HAZIR:
        cihaz_ad, compute_type = _cihaz()
        try:
            _HAZIR[anahtar] = Cevirmen(model_yolu, cihaz_ad, compute_type, batch=ceviri_ayar.batch)
        except CeviriHatasi as e:
            log.warning("Çeviri yapılamadı: %s", e)
            return None
    return _HAZIR[anahtar]
