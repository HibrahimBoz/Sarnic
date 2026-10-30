"""Office ve OpenDocument metnini bölüm bölüm çıkarır."""
from pathlib import Path
import os
import tempfile
import zipfile
import xml.etree.ElementTree as ET

from .. import yollar
from ..hatalar import IsHatasi
from .surec import calistir


def bolumler(paragraflar, sinir=3000):
    biriken, uzunluk = [], 0
    for metin in paragraflar:
        if metin is None or (biriken and uzunluk + len(metin) > sinir):
            yield "\n".join(biriken)
            biriken, uzunluk = [], 0
            if metin is None:
                continue
        biriken.append(metin)
        uzunluk += len(metin) + 1
    if biriken or not uzunluk:
        yield "\n".join(biriken)


def docx_bolumleri(yol):
    from docx import Document
    from docx.oxml.ns import qn
    from docx.table import Table
    # python-docx, macro content type'ını reddeder. Yalnız paket türünü geçici kopyada düzelt.
    if yol.suffix.lower() == ".docm":
        with tempfile.TemporaryDirectory(prefix="bulgu-docm-") as tmp:
            yeni = Path(tmp) / "belge.docx"
            with zipfile.ZipFile(yol) as z, zipfile.ZipFile(yeni, "w") as out:
                for ad in z.namelist():
                    b = z.read(ad)
                    if ad == "[Content_Types].xml":
                        b = b.replace(b"application/vnd.ms-word.document.macroEnabled.main+xml",
                                      b"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")
                    out.writestr(ad, b)
            yield from docx_bolumleri(yeni)
        return
    belge = Document(yol)
    acik_sayfa = bool(belge.element.xpath('.//w:br[@w:type="page"] | .//w:lastRenderedPageBreak'))
    etiket = "Sayfa" if acik_sayfa else "Bölüm"
    parcalar, gorseller, uzunluk, no = [], [], 0, 1
    bilgi = []
    gorulen = set()
    for bolum in belge.sections:
        for kisim in (bolum.header, bolum.footer):
            for p in kisim.paragraphs:
                if p.text and p.text not in gorulen:
                    bilgi.append(p.text)
                    gorulen.add(p.text)
            for t in kisim.tables:
                bilgi.extend("\t".join(c.text for c in r.cells) for r in t.rows)
            for e in kisim._element.iter(qn("a:blip")):
                rel = e.get(qn("r:embed"))
                if rel in kisim.part.related_parts:
                    gorseller.append(kisim.part.related_parts[rel].blob)
    parcalar.extend(bilgi)
    uzunluk = sum(len(p) + 1 for p in parcalar)
    for eleman in belge.element.body:
        if eleman.tag == qn("w:tbl"):
            rows = Table(eleman, belge).rows
            metinler = [("\t".join(c.text for c in r.cells), []) for r in rows]
            tbl_resimler = []
            for e in eleman.iter(qn("a:blip")):
                rel = e.get(qn("r:embed"))
                if rel in belge.part.related_parts:
                    blob = belge.part.related_parts[rel].blob
                    if metinler:
                        metinler[-1][1].append(blob)
                    else:
                        tbl_resimler.append(blob)
            if not metinler and tbl_resimler:
                metinler = [("", tbl_resimler)]
        elif eleman.tag == qn("w:p"):
            metinler, buf, resimler = [], [], []
            for e in eleman.iter():
                if e.tag == qn("w:t"):
                    buf.append(e.text or "")
                elif e.tag == qn("w:tab"):
                    buf.append("\t")
                elif e.tag == qn("w:lastRenderedPageBreak") or (e.tag == qn("w:br") and e.get(qn("w:type")) == "page"):
                    metinler.extend((("".join(buf), resimler), (None, [])))
                    buf, resimler = [], []
                elif e.tag == qn("w:br"):
                    buf.append("\n")
                elif e.tag == qn("a:blip"):
                    rel = e.get(qn("r:embed"))
                    if rel in belge.part.related_parts:
                        resimler.append(belge.part.related_parts[rel].blob)
            metinler.append(("".join(buf), resimler))
        else:
            continue
        for metin, resimler in metinler:
            if metin is None or (not acik_sayfa and parcalar and uzunluk + len(metin) > 3000):
                yield f"{etiket} {no}", "\n".join(parcalar), gorseller
                no += 1
                parcalar, gorseller, uzunluk = [], [], 0
                if metin is None:
                    continue
            parcalar.append(metin)
            gorseller.extend(resimler)
            uzunluk += len(metin) + 1
    yield f"{etiket} {no}", "\n".join(parcalar), gorseller


def pptx_slaytlari(yol):
    from pptx import Presentation

    def sekiller(sekiller_):
        for s in sekiller_:
            if hasattr(s, "shapes"):
                yield from sekiller(s.shapes)
            yield s

    belge = Presentation(yol)
    for i, slayt in enumerate(belge.slides, 1):
        metinler, gorseller = [], []
        for s in sekiller(slayt.shapes):
            if s.has_text_frame:
                metinler.append(s.text)
            if s.has_table:
                metinler.extend("\t".join(c.text for c in r.cells) for r in s.table.rows)
            if hasattr(s, "image"):
                gorseller.append(s.image.blob)
        if slayt.has_notes_slide:
            tf = slayt.notes_slide.notes_text_frame
            if tf and tf.text.strip():
                metinler.append("Not: " + tf.text)
        yield f"Slayt {i}", "\n".join(metinler), gorseller


def xlsx_parcalari(yol):
    import openpyxl
    belge = openpyxl.load_workbook(yol, read_only=True, data_only=True)
    try:
        for s in belge.worksheets:
            satirlar, bas, son = [], 1, 0
            for son, hucreler in enumerate(s.iter_rows(values_only=True), 1):
                if any(h is not None and str(h) != "" for h in hucreler):
                    satirlar.append("\t".join("" if h is None else str(h) for h in hucreler))
                if son % 200 == 0:
                    yield f"{s.title} (satır {bas}-{son})", "\n".join(satirlar), []
                    satirlar, bas = [], son + 1
            if son < bas or son == 0:
                if son == 0:
                    yield f"{s.title} (satır 1-1)", "", []
            else:
                yield f"{s.title} (satır {bas}-{son})", "\n".join(satirlar), []
    finally:
        belge.close()


_NS = {k: f"urn:oasis:names:tc:opendocument:xmlns:{v}:1.0" for k, v in
       (("office", "office"), ("text", "text"), ("table", "table"), ("draw", "drawing"))}


def _od_metin(e):
    parcalar = [e.text or ""]
    for alt in e:
        if alt.tag in (f"{{{_NS['text']}}}p", f"{{{_NS['text']}}}h"):
            parcalar.append(alt.tail or "")  # iç paragraf dış döngüde okunur; ardından gelen metin kaybolmasın
            continue
        if alt.tag == f"{{{_NS['text']}}}s":
            parcalar.append(" " * int(alt.get(f"{{{_NS['text']}}}c", "1")))
        elif alt.tag == f"{{{_NS['text']}}}tab":
            parcalar.append("\t")
        elif alt.tag == f"{{{_NS['text']}}}line-break":
            parcalar.append("\n")
        else:
            parcalar.append(_od_metin(alt))
        parcalar.append(alt.tail or "")
    return "".join(parcalar)


def odf_parcalari(yol):
    with zipfile.ZipFile(yol) as z:
        with z.open("content.xml") as f:
            kok = ET.parse(f).getroot()
    if yol.suffix.lower() == ".ods":
        for tablo in kok.findall(".//table:table", _NS):
            ad = tablo.get(f"{{{_NS['table']}}}name", "Tablo")
            satirlar, bas, no = [], 1, 0
            for r in tablo.findall(".//table:table-row", _NS):
                hucreler = []
                for c in r:
                    # Hücre içi liste/not paragrafları ve başlıklar da (iç içe text:p/text:h)
                    metin = "\n".join(_od_metin(p) for p in c.iter()
                                      if p.tag in (f"{{{_NS['text']}}}p", f"{{{_NS['text']}}}h"))
                    hucreler.extend([metin] * int(c.get(f"{{{_NS['table']}}}number-columns-repeated", "1")))
                metin = "\t".join(hucreler).rstrip("\t")
                tekrar = int(r.get(f"{{{_NS['table']}}}number-rows-repeated", "1"))
                if not metin.strip() and tekrar > 200:
                    if satirlar:
                        yield f"{ad} (satır {bas}-{no})", "\n".join(satirlar), []
                    no += tekrar
                    bas, satirlar = no + 1, []
                    continue
                for _ in range(tekrar):
                    no += 1
                    if metin.strip():
                        satirlar.append(metin)
                    if no - bas + 1 == 200:
                        yield f"{ad} (satır {bas}-{no})", "\n".join(satirlar), []
                        bas, satirlar = no + 1, []
            if satirlar or (no == 0 and bas == 1):
                yield f"{ad} (satır {bas}-{max(bas, no)})", "\n".join(satirlar), []
    elif yol.suffix.lower() == ".odp":
        for i, s in enumerate(kok.findall(".//draw:page", _NS), 1):
            yield f"Slayt {i}", "\n".join(_od_metin(p) for p in s.iter()
                                         if p.tag in (f"{{{_NS['text']}}}p", f"{{{_NS['text']}}}h")), []
    else:
        paragraflar = (_od_metin(e) for e in kok.iter()
                      if e.tag in (f"{{{_NS['text']}}}p", f"{{{_NS['text']}}}h"))
        for i, metin in enumerate(bolumler(paragraflar), 1):
            yield f"Bölüm {i}", metin, []


def eski_cevir(yol, ara):
    exe = yollar.libreoffice()
    if exe is None:
        return None
    uzanti = {".doc": "docx", ".xls": "xlsx", ".ppt": "pptx"}[yol.suffix.lower()]
    ara.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=f"lo_profil_{os.getpid()}_", dir=ara) as tmp:
        calistir([exe, f"-env:UserInstallation={Path(tmp).resolve().as_uri()}", "--headless",
                  "--convert-to", uzanti, "--outdir", ara, yol], 120, "LibreOffice")
    sonuc = ara / (yol.stem + "." + uzanti)
    if not sonuc.is_file():
        raise IsHatasi("LibreOffice çıktı oluşturmadı")
    return sonuc
