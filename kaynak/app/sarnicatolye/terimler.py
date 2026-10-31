"""Terim sözlüğü: Whisper istemi + çıktı düzeltme (tamamen çevrimdışı, yalnız stdlib).

terimler.txt biçimi (bölümsüz satırlar [istem] sayılır):
    [istem]      Whisper'a ipucu olarak verilen KISA öncelikli liste (en önemli terimler başta)
    [bulanik]    Yabancı/özel terimler (İngilizce, kısaltma): çıktıda yakın yazımlar bu terime düzeltilir.
                 Türkçe sözcükler BURAYA konmaz (yanılma/yanıltma gibi benzer gerçek sözcükleri bozar)
    [duzeltme]   yanlış => doğru   (kesin kurallar; boşluklar esnek, büyük/küçük harf duyarsız)
"""
import difflib
import re
from dataclasses import dataclass, field
from pathlib import Path

ISTEM_KARAKTER_SINIRI = 480   # Whisper istemi ~223 token alır; terim sınırında kesilir
BULANIK_ESIK = 0.85
BULANIK_MIN_UZUNLUK = 7

_KELIME = re.compile(r"[0-9A-Za-zÇĞİÖŞÜçğıöşü]+(?:[-'][0-9A-Za-zÇĞİÖŞÜçğıöşü]+)*")


def kucuk(s: str) -> str:
    """Türkçe küçük harf (İ→i, I→ı)."""
    return s.replace("İ", "i").replace("I", "ı").lower()


@dataclass
class Terimler:
    istem: str = ""
    sozluk: list[str] = field(default_factory=list)
    kurallar: list[tuple[re.Pattern, str]] = field(default_factory=list)


def _kural_deseni(yanlis: str) -> re.Pattern:
    """Tek harflik parçalar (harf harf okunan kısaltma) arasında boşluk/tire/nokta/hiçbiri serbest;
    diğer çok sözcüklü kurallarda boşluk veya tire."""
    parcalar = [re.escape(p) for p in re.split(r"[\s-]+", yanlis) if p]
    harf_harf = len(parcalar) > 1 and all(len(p) == 1 for p in parcalar)
    ayirac = r"[\s.\-]*" if harf_harf else r"[\s\-]+"
    return re.compile(r"(?<![0-9A-Za-zÇĞİÖŞÜçğıöşü])" + ayirac.join(parcalar)
                      + r"(?![0-9A-Za-zÇĞİÖŞÜçğıöşü])", re.IGNORECASE)


def ayristir(metin: str) -> Terimler:
    bolum = "istem"
    istem: list[str] = []
    sozluk: list[str] = []
    kurallar: list[tuple[re.Pattern, str]] = []
    for ham in metin.splitlines():
        satir = ham.strip()
        if not satir or satir.startswith("#"):
            continue
        if satir.startswith("[") and satir.endswith("]"):
            bolum = satir[1:-1].strip().lower()
            continue
        if bolum == "istem":
            istem.append(satir)
        elif bolum in ("bulanik", "sozluk"):
            sozluk.append(satir)
        elif bolum == "duzeltme" and "=>" in satir:
            y, d = (x.strip() for x in satir.split("=>", 1))
            if y and d:
                kurallar.append((_kural_deseni(y), d))
    prompt, uzunluk = [], 0
    for t in istem:
        ek = len(t) + (2 if prompt else 0)
        if uzunluk + ek > ISTEM_KARAKTER_SINIRI:
            break
        prompt.append(t)
        uzunluk += ek
    return Terimler(istem=", ".join(prompt), sozluk=sozluk, kurallar=kurallar)


def yukle(yol: Path) -> Terimler:
    if not Path(yol).is_file():
        return Terimler()
    return ayristir(Path(yol).read_text(encoding="utf-8-sig"))


def _bulanik_hazirla(sozluk: list[str]):
    """Tek sözcüklü, yeterince uzun terimler: kucuk_hali -> kanonik yazım."""
    d: dict[str, str] = {}
    for t in sozluk:
        if len(t) >= BULANIK_MIN_UZUNLUK and _KELIME.fullmatch(t) is not None:
            d.setdefault(kucuk(t), t)
    return d


def duzelt_metin(metin: str, tm: Terimler, _onbellek: dict | None = None) -> tuple[str, int]:
    """Kesin kurallar, sonra temkinli bulanık eşleştirme. (yeni_metin, düzeltme_sayısı)"""
    sayi = 0
    for desen, dogru in tm.kurallar:
        metin, n = desen.subn(lambda _m, d=dogru: d, metin)
        sayi += n
    if _onbellek is None:
        _onbellek = {}
    if "terimler" not in _onbellek:
        _onbellek["terimler"] = _bulanik_hazirla(tm.sozluk)
        _onbellek["sonuc"] = {}
    terimler, onb = _onbellek["terimler"], _onbellek["sonuc"]

    def degistir(m: re.Match) -> str:
        nonlocal sayi
        kelime = m.group(0)
        k = kucuk(kelime)
        if len(k) < BULANIK_MIN_UZUNLUK or k in terimler:
            return kelime
        if k not in onb:
            en = None
            for t in terimler:
                if t[0] != k[0] or abs(len(t) - len(k)) > 2 or k.startswith(t):
                    continue
                r = difflib.SequenceMatcher(None, k, t).ratio()
                if r >= BULANIK_ESIK and (en is None or r > en[0]):
                    en = (r, t)
            onb[k] = terimler[en[1]] if en else None
        if onb[k] is None:
            return kelime
        sayi += 1
        return onb[k]

    return _KELIME.sub(degistir, metin), sayi


def uygula(segmentler, tm: Terimler):
    """Segment listesinin metinlerini yerinde düzeltir. Düzeltme sayısını döndürür."""
    if not tm.kurallar and not tm.sozluk:
        return 0
    onb: dict = {}
    toplam = 0
    for s in segmentler:
        yeni, n = duzelt_metin(s.metin, tm, onb)
        s.metin = yeni
        toplam += n
    return toplam
