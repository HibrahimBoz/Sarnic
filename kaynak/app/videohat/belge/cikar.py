"""Uzantıya göre metin çıkarma. OCR görüntüleri sayfa başına işlenir."""
import email
from email import policy
from html.parser import HTMLParser
import io
from pathlib import Path
import xml.etree.ElementTree as ET
import zlib

from ..ayar import Ayar
from ..hatalar import IsHatasi
from . import GORUNTU_UZANTILARI, UZANTILAR
from .model import BelgeSonucu, Sayfa
from .ocr import OcrHavuzu, OcrIsi
from .onbellek import sayfa_oku, sayfa_yaz
from .ofis import bolumler, docx_bolumleri, pptx_slaytlari, xlsx_parcalari, odf_parcalari, eski_cevir

# Görsel kaplı PDF'de yalnız kısa üst/alt bilgi bulunması da OCR gerektirir.
PDF_AZ_METIN_KATI = 2


class _Html(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.atla = 0
        self.parcalar = []

    def handle_starttag(self, tag, attrs):
        if tag in ("script", "style"):
            self.atla += 1
        if tag in ("br", "p", "div", "tr", "li", "h1", "h2", "h3") and not self.atla:
            self.parcalar.append("\n")

    def handle_endtag(self, tag):
        if tag in ("script", "style"):
            self.atla = max(0, self.atla - 1)
        if tag in ("p", "div", "tr", "li") and not self.atla:
            self.parcalar.append("\n")

    def handle_data(self, data):
        if not self.atla:
            self.parcalar.append(data)


def html_metin(s):
    p = _Html()
    p.feed(s)
    return "".join(p.parcalar)


def metin_oku(ham):
    if ham.startswith((b"\xff\xfe", b"\xfe\xff")):
        return ham.decode("utf-16")
    try:
        return ham.decode("utf-8-sig")
    except UnicodeDecodeError:
        return ham.decode("cp1254", errors="replace")


def _sakla(sonuc, sayfa, ara, ayar, uyarilar=()):
    sayfa_yaz(ara, sayfa, ayar.ocr.dpi, uyarilar)
    sonuc.sayfalar.append(sayfa)
    sonuc.uyarilar.extend(uyarilar)


def _onbellekten(sonuc, ara, no, ayar):
    veri = sayfa_oku(ara, no, ayar.ocr.dpi)
    if veri:
        s, u = veri
        sonuc.sayfalar.append(s)
        sonuc.uyarilar.extend(u)
        return True
    return False


def _ocr_isleri(sonuc, isler, havuz, ara, ayar):
    if havuz is None:
        for is_ in isler:
            s = Sayfa(is_.no, is_.etiket or f"Sayfa {is_.no}", "ocr", "", 0)
            _sakla(sonuc, s, ara, ayar, [f"Sayfa {is_.no}: OCR havuzu yok"])
    else:
        for s, u in havuz.haritala(isler):
            _sakla(sonuc, s, ara, ayar, u)
    sonuc.sayfalar.sort(key=lambda s: s.no)


def _pdf(yol, ayar, havuz, ara, sonuc):
    import pymupdf
    with pymupdf.open(yol) as belge:
        if belge.needs_pass:
            raise IsHatasi("Parolalı PDF")

        def isler():
            for no, sayfa in enumerate(belge, 1):
                if havuz and getattr(havuz, "ilerleme", None):
                    havuz.ilerleme()
                if _onbellekten(sonuc, ara, no, ayar):
                    continue
                metin = sayfa.get_text("text", sort=True).strip()
                az = len(metin) < ayar.ocr.min_metin
                if not az and len(metin) < ayar.ocr.min_metin * PDF_AZ_METIN_KATI:
                    alan = sum((pymupdf.Rect(i["bbox"]) & sayfa.rect).get_area()
                               for i in sayfa.get_image_info())
                    az = alan >= sayfa.rect.get_area() * .6
                if az:
                    yield OcrIsi(str(yol), no, "pdf")
                else:
                    _sakla(sonuc, Sayfa(no, f"Sayfa {no}", "metin", metin), ara, ayar)

        _ocr_isleri(sonuc, isler(), havuz, ara, ayar)


def _gomulu(bloblar, ayar, havuz, ara, no):
    from PIL import Image
    metinler, uyarilar = [], []
    if not ayar.ocr.gomulu_gorsel:
        return metinler, uyarilar
    for i, blob in enumerate(bloblar):
        try:
            with Image.open(io.BytesIO(blob)) as im:
                if min(im.size) < 200:
                    continue
                yol = ara / f"gomulu_{no}_{i}.png"
                im.convert("RGB").save(yol)
        except (OSError, ValueError):
            uyarilar.append(f"Bölüm {no}: gömülü görsel okunamadı")
            continue
        try:
            if havuz is None:
                uyarilar.append(f"Bölüm {no}: gömülü görsel için OCR havuzu yok")
            else:
                s, u = havuz.oku(OcrIsi(str(yol)))
                metinler.append(s.metin)
                uyarilar.extend(u)
        finally:
            yol.unlink(missing_ok=True)
    return metinler, uyarilar


def _ofis(parcalar, sonuc, ayar, havuz, ara):
    for no, (etiket, metin, gorseller) in enumerate(parcalar, 1):
        if havuz and getattr(havuz, "ilerleme", None):
            havuz.ilerleme()
        if _onbellekten(sonuc, ara, no, ayar):
            continue
        ek, u = _gomulu(gorseller, ayar, havuz, ara, no)
        _sakla(sonuc, Sayfa(no, etiket, "metin", "\n".join([metin] + ek)), ara, ayar, u)


def _ekleri_isle(ekler, sonuc, ayar, havuz, ara, derinlik):
    if derinlik >= 1:
        if ekler:
            sonuc.uyarilar.append("İç içe ileti ekleri atlandı (derinlik 1)")
        return
    for i, (ad, veri) in enumerate(ekler, 1):
        guvenli_ad = Path(ad.replace("\\", "/")).name or f"ek_{i}.bin"
        ek_ara = ara / f"ek_{i}"
        ek_ara.mkdir(exist_ok=True)
        uzanti = Path(guvenli_ad).suffix.lower()
        yol = ek_ara / (f"ek_{i}" + (uzanti if uzanti in UZANTILAR else ".bin"))
        yol.write_bytes(veri)
        try:
            alt = cikar(yol, ayar, havuz, ek_ara / "_ara", derinlik + 1)
        except IsHatasi as e:
            sonuc.uyarilar.append(f"Ek: {guvenli_ad}: {e}")
            continue
        sonuc.uyarilar.extend(alt.uyarilar)
        if alt.hata:
            sonuc.uyarilar.append(f"Ek: {guvenli_ad}: {alt.hata}")
        for s in alt.sayfalar:
            s.no = len(sonuc.sayfalar) + 1
            s.etiket = f"Ek: {guvenli_ad} / {s.etiket}"
            _sakla(sonuc, s, ara, ayar)


def _ileti(yol, sonuc, ayar, havuz, ara, derinlik):
    if yol.suffix.lower() == ".eml":
        with yol.open("rb") as f:
            ileti = email.message_from_binary_file(f, policy=policy.default)
        govde = ileti.get_body(preferencelist=("plain", "html"))
        metin = govde.get_content() if govde else ""
        if govde and govde.get_content_subtype() == "html":
            metin = html_metin(metin)
        ekler = []
        for i, ek in enumerate(ileti.iter_attachments(), 1):
            veri = ek.get_payload(decode=True)
            if veri is None and ek.get_content_type() == "message/rfc822":
                veri = ek.get_payload(0).as_bytes()
            if veri is not None:
                uzanti = ".eml" if ek.get_content_type() == "message/rfc822" else ".bin"
                ekler.append((ek.get_filename() or f"ek_{i}{uzanti}", veri))
    else:
        try:
            import extract_msg
        except ImportError:
            sonuc.durum, sonuc.hata = "desteklenmiyor", "MSG: extract-msg bulunamadı"
            return
        with extract_msg.openMsg(str(yol)) as ileti:
            metin = ileti.body
            if not metin and ileti.htmlBody:
                metin = html_metin(ileti.htmlBody if isinstance(ileti.htmlBody, str) else metin_oku(ileti.htmlBody))
            metin = metin or ""
            ekler = []
            for i, ek in enumerate(ileti.attachments, 1):
                if isinstance(ek.data, bytes):
                    ekler.append((ek.longFilename or ek.shortFilename or f"ek_{i}.bin", ek.data))
    _sakla(sonuc, Sayfa(1, "İleti", "metin", metin), ara, ayar)
    _ekleri_isle(ekler, sonuc, ayar, havuz, ara, derinlik)


def _cikar(yol, ayar, havuz, ara, derinlik):
    yol, ara = Path(yol), Path(ara)
    ara.mkdir(parents=True, exist_ok=True)
    uz = yol.suffix.lower()
    sonuc = BelgeSonucu(uz.lstrip("."))
    if uz not in UZANTILAR:
        sonuc.durum, sonuc.hata = "desteklenmiyor", f"Desteklenmeyen biçim: {uz}"
    elif uz == ".pdf":
        _pdf(yol, ayar, havuz, ara, sonuc)
    elif uz in GORUNTU_UZANTILARI:
        from PIL import Image
        with Image.open(yol) as im:
            sayi = getattr(im, "n_frames", 1)
        isler = (OcrIsi(str(yol), no) for no in range(1, sayi + 1)
                 if not _onbellekten(sonuc, ara, no, ayar))
        _ocr_isleri(sonuc, isler, havuz, ara, ayar)
    elif uz in (".docx", ".docm", ".pptx", ".pptm", ".xlsx", ".xlsm", ".odt", ".ods", ".odp"):
        fn = (docx_bolumleri if uz in (".docx", ".docm") else
              pptx_slaytlari if uz in (".pptx", ".pptm") else
              xlsx_parcalari if uz in (".xlsx", ".xlsm") else odf_parcalari)
        _ofis(fn(yol), sonuc, ayar, havuz, ara)
    elif uz in (".doc", ".xls", ".ppt"):
        yeni = eski_cevir(yol, ara / "ofis")
        if yeni is None:
            sonuc.durum = "desteklenmiyor"
            sonuc.hata = "Eski Office biçimi: LibreOffice bulunamadı"
        else:
            sonuc = _cikar(yeni, ayar, havuz, ara / "cevrilen", derinlik)
            sonuc.bicim = uz.lstrip(".")
    elif uz in (".eml", ".msg"):
        _ileti(yol, sonuc, ayar, havuz, ara, derinlik)
    else:
        ham = yol.read_bytes()
        if uz == ".rtf":
            from .rtf import soy
            metin = soy(ham)
        else:
            metin = metin_oku(ham)
        if uz in (".html", ".htm"):
            metin = html_metin(metin)
        _ofis(((f"Bölüm {i}", s, []) for i, s in enumerate(bolumler(metin.splitlines()), 1)),
              sonuc, ayar, havuz, ara)
    return sonuc


def cikar(yol: Path, ayar: Ayar, havuz: OcrHavuzu | None, ara: Path, derinlik: int = 0) -> BelgeSonucu:
    try:
        return _cikar(yol, ayar, havuz, ara, derinlik)
    except (IsHatasi, KeyboardInterrupt):
        raise
    except (OSError, ValueError, KeyError, EOFError, RuntimeError, ET.ParseError, LookupError, zlib.error, TypeError) as e:
        raise IsHatasi(f"Belge okunamadı: {e}") from e
