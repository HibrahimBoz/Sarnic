"""OCR kutularına görünmez metin ekler; özgün sayfa görünümü korunur."""
import io
import os
from pathlib import Path

from . import GORUNTU_UZANTILARI


def _metin_ekle(sayfa, kayit, dpi, font, font_ad, font_yol):
    if font_ad == "bulgu" and font_yol.is_file():
        try:
            sayfa.insert_font(fontname=font_ad, fontfile=str(font_yol))
        except Exception:
            pass
    yon = getattr(kayit, "yon", 0)
    # yon_cevir saat yönünün tersine döndürür: metin katmanı ters açıyla yazılır (ölçüldü, 90°/270° tarama)
    rot = (sayfa.rotation - yon) % 360
    for s in kayit.satirlar:
        if not s.metin.strip():
            continue
        import pymupdf
        r = pymupdf.Rect(*(v * 72 / dpi for v in s.kutu))
        if sayfa.rotation:
            r = r * sayfa.derotation_matrix
        boy, en = (r.height, r.width) if rot % 180 == 0 else (r.width, r.height)
        punto = min(boy / (font.ascender - font.descender),
                    en / max(font.text_length(s.metin, fontsize=1), .01))
        if punto <= 0:
            continue
        ust = font.ascender * punto
        konum = {0: (r.x0, r.y0 + ust), 90: (r.x0 + ust, r.y1),
                 180: (r.x1, r.y1 - ust), 270: (r.x1 - ust, r.y0)}[rot]
        sayfa.insert_text(konum, s.metin, fontname=font_ad, fontsize=punto,
                          render_mode=3, rotate=rot, overlay=True)


def olustur(yol: Path, sayfalar, hedef: Path, dpi: int):
    import pymupdf
    uz = yol.suffix.lower()
    if uz != ".pdf" and uz not in GORUNTU_UZANTILARI:
        return None
    if uz == ".pdf" and not any(s.kaynak == "ocr" for s in sayfalar):
        return None
    font_yol = Path("C:/Windows/Fonts/arial.ttf")
    if font_yol.is_file():
        font = pymupdf.Font(fontfile=str(font_yol))
        font_ad = "bulgu"
    else:
        font_ad, font = "helv", pymupdf.Font("helv")
    hedef.parent.mkdir(parents=True, exist_ok=True)
    gecici = hedef.with_name(hedef.name + ".tmp")
    with (pymupdf.open(yol) if uz == ".pdf" else pymupdf.open()) as belge:
        if uz == ".pdf":
            for s in sayfalar:
                if s.kaynak == "ocr":
                    _metin_ekle(belge[s.no - 1], s, dpi, font, font_ad, font_yol)
        else:
            from PIL import Image, ImageOps
            with Image.open(yol) as im:
                for s in sayfalar:
                    im.seek(s.no - 1)
                    kare = ImageOps.exif_transpose(im).convert("RGB")
                    sayfa = belge.new_page(width=kare.width * 72 / dpi, height=kare.height * 72 / dpi)
                    with io.BytesIO() as buf:
                        kare.save(buf, format="PNG")
                        sayfa.insert_image(sayfa.rect, stream=buf.getvalue())
                    _metin_ekle(sayfa, s, dpi, font, font_ad, font_yol)
        belge.save(gecici, garbage=3, deflate=True)
    os.replace(gecici, hedef)
    return hedef
