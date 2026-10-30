"""Belge testleri için çalışma sırasında üretilen küçük veriler."""
from pathlib import Path
import tempfile
import unittest

import pymupdf

from videohat import yollar
from videohat.ayar import Ayar, DizinAyar, OcrAyar
from videohat.belge.model import OcrSatir, Sayfa

TURKCE = "Radar frekansı ölçümü ve güvenlik kontrolü"
FONT = Path("C:/Windows/Fonts/arial.ttf")


def gorsel():
    import numpy as np
    with pymupdf.open() as d:
        p = d.new_page(width=500, height=170)
        if FONT.exists():
            p.insert_font(fontname="arial", fontfile=str(FONT))
        p.insert_text((20, 60), TURKCE, fontname="arial" if FONT.exists() else "helv", fontsize=20)
        p.insert_text((20, 105), "Sistem testi ve rapor", fontname="arial" if FONT.exists() else "helv", fontsize=18)
        pix = p.get_pixmap(matrix=pymupdf.Matrix(1.5, 1.5), alpha=False)
        return np.frombuffer(pix.samples, dtype=np.uint8).reshape(pix.height, pix.width, 3).copy()


def pdf_yaz(yol, sayi=1, taranmis=False, donus=0):
    with pymupdf.open() as d:
        for i in range(sayi):
            p = d.new_page(width=500, height=170)
            if taranmis:
                im = gorsel()
                pix = pymupdf.Pixmap(pymupdf.csRGB, im.shape[1], im.shape[0], im.tobytes(), False)
                p.insert_image(p.rect, pixmap=pix)
            else:
                p.insert_text((15, 40), f"Belge sayfa {i + 1}. Radar kontrol raporu ve sistem guvenlik olcumu.")
            p.set_rotation(donus)
        d.save(yol)
    return yol


class GeciciTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="bulgu-test-")
        self.addCleanup(self.tmp.cleanup)
        self.kok = Path(self.tmp.name)
        self.ayar = Ayar(yollar.kok(), dizin=DizinAyar(isci=1),
                         ocr=OcrAyar(profil="hizli", dpi=150, tesseract=False))


class SahteHavuz:
    def __init__(self, kes=None):
        self.isler = []
        self.kes = kes

    def haritala(self, isler):
        for is_ in isler:
            self.isler.append(is_.no)
            if is_.no == self.kes:
                raise KeyboardInterrupt()
            satir = OcrSatir("frekansı ölçümü güvenlik", .95, (20, 30, 650, 70))
            yield Sayfa(is_.no, f"Sayfa {is_.no}", "ocr", satir.metin, .95, [satir], (1042, 355)), []

    def oku(self, is_):
        return next(self.haritala([is_]))
