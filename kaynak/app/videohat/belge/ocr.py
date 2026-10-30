"""Yerel PP-OCRv6 modelleri, ikinci görüş ve öldürülebilen spawn işçileri."""
from dataclasses import dataclass
import csv
import io
import logging
import multiprocessing
import os
from pathlib import Path
import queue
import tempfile
import time

from .. import yollar
from ..hatalar import IsHatasi, OrtamHatasi
from .model import OcrSatir, Sayfa
from .onisleme import hazirla, metin_murekkebi, yon_cevir
from .sira import sirala, birlestir, ortalama
from .sozluk import Sozluk
from .surec import calistir

log = logging.getLogger("videohat")


def _indirme_yasak(*args, **kwargs):
    raise OrtamHatasi("OCR çevrimdışı: model veya karakter sözlüğü eksik; indirme kapalı")


class OcrMotoru:
    def __init__(self, ayar):
        self.ayar = ayar
        self.motorlar = {}
        sozluk = Path(ayar.ocr.sozluk.replace("\\", "/"))
        if not sozluk.is_absolute():
            sozluk = ayar.kok / sozluk
        self.sozluk = Sozluk(sozluk, ayar.kok / ayar.stt.terimler_dosyasi,
                             unlu_uyumu=getattr(ayar.ocr, "unlu_uyumu", True))
        self.tesseract = yollar.tesseract() if ayar.ocr.tesseract else None

    def _motor(self, det, rec):
        anahtar = det, rec
        if anahtar not in self.motorlar:
            from rapidocr import RapidOCR, OCRVersion, ModelType
            from rapidocr.utils.download_file import DownloadFile
            import onnxruntime
            # Sözlük/model indirme yolu da kapalı; eksik meta veri ağa çıkamaz.
            DownloadFile.run = staticmethod(_indirme_yasak)
            cuda = "CUDAExecutionProvider" in onnxruntime.get_available_providers()
            if self.ayar.ocr.cihaz == "cuda" and not cuda:
                raise OrtamHatasi("ONNX Runtime CUDAExecutionProvider bulunamadı")
            params = {
                "Det.model_path": str(yollar.ocr_model(f"PP-OCRv6_det_{det}.onnx")),
                "Rec.model_path": str(yollar.ocr_model(f"PP-OCRv6_rec_{rec}.onnx")),
                "Cls.model_path": str(yollar.ocr_model("ch_ppocr_mobile_v2.0_cls_mobile.onnx")),
                "Det.ocr_version": OCRVersion.PPOCRV6, "Rec.ocr_version": OCRVersion.PPOCRV6,
                "Det.model_type": ModelType(det), "Rec.model_type": ModelType(rec),
                "Global.text_score": 0.0, "Global.log_level": "error",
                "EngineConfig.onnxruntime.use_cuda": cuda and self.ayar.ocr.cihaz != "cpu",
                # Çok süreçte her motorun tüm çekirdekleri tüketmesini önler.
                "EngineConfig.onnxruntime.intra_op_num_threads": self.ayar.genel.cpu_threads or 1,
                "EngineConfig.onnxruntime.inter_op_num_threads": 1,
            }
            self.motorlar[anahtar] = RapidOCR(params=params)
        motor = self.motorlar[anahtar]
        for (d, r), diger in self.motorlar.items():
            if d == det and diger.text_det is not None:
                motor.text_det = diger.text_det
            if r == rec and diger.text_rec is not None:
                motor.text_rec = diger.text_rec
            if diger.text_cls is not None:
                motor.text_cls = diger.text_cls
        return motor

    def _oku(self, gorsel, det, rec):
        sonuc = self._motor(det, rec)(gorsel, use_det=True, use_rec=True, use_cls=True)
        if sonuc.boxes is None or not sonuc.txts:
            return []
        return [OcrSatir(t, float(g), (float(b[:, 0].min()), float(b[:, 1].min()),
                                      float(b[:, 0].max()), float(b[:, 1].max())))
                for b, t, g in zip(sonuc.boxes, sonuc.txts, sonuc.scores)]

    def _tesseract(self, kirpinti):
        from PIL import Image
        with tempfile.TemporaryDirectory(prefix="bulgu-tess-") as tmp:
            yol = Path(tmp) / "satir.png"
            Image.fromarray(kirpinti).save(yol)
            cikti = calistir([self.tesseract, yol, "stdout", "-l", "tur+eng", "--psm", "7", "tsv"],
                            min(30, self.ayar.ocr.sayfa_zaman_asimi_sn), "Tesseract")
        kelimeler, guvenler = [], []
        for satir in csv.DictReader(io.StringIO(cikti.decode("utf-8")), delimiter="\t"):
            metin = satir.get("text", "").strip()
            guven = float(satir.get("conf", -1))
            if metin and guven >= 0:
                kelimeler.append(metin)
                guvenler.append((len(metin), guven / 100))
        return (" ".join(kelimeler), sum(n * g for n, g in guvenler) / sum(n for n, _ in guvenler)
                if guvenler else 0.0)

    def oku(self, gorsel):
        import cv2
        gri, donusum = hazirla(gorsel)
        profil = self.ayar.ocr.profil
        det = rec = "medium" if profil == "kalite" else "small"
        satirlar = self._oku(gri, det, rec)
        if ortalama(satirlar) < .5:
            oran = min(1.0, 1000 / max(gri.shape))
            kucuk = cv2.resize(gri, None, fx=oran, fy=oran, interpolation=cv2.INTER_AREA)
            en_yon, en_guven = 0, ortalama(satirlar)
            for yon in (90, 180, 270):
                deneme, _ = yon_cevir(kucuk, yon)
                guven = ortalama(self._oku(deneme, det, rec))
                if guven > en_guven:
                    en_yon, en_guven = yon, guven
            if en_yon:
                gri, matris = yon_cevir(gri, en_yon)
                donusum.yon = en_yon
                donusum.ekle(matris)
                satirlar = self._oku(gri, det, rec)
        if profil == "uyarlamali":
            kutu_alani = sum((s.kutu[2] - s.kutu[0]) * (s.kutu[3] - s.kutu[1]) for s in satirlar)
            if kutu_alani < .5 * metin_murekkebi(gri):
                satirlar = self._oku(gri, "medium", "small")
        for s in satirlar:
            if s.guven < self.ayar.ocr.ikinci_esik:
                x0, y0, x1, y1 = s.kutu
                kirpinti = gri[max(0, int(y0)):min(gri.shape[0], int(y1 + 1)),
                               max(0, int(x0)):min(gri.shape[1], int(x1 + 1))]
                if kirpinti.size:
                    adaylar = [(s.metin, s.guven)]
                    if profil == "uyarlamali":
                        sonuc = self._motor("small", "medium")(kirpinti, use_det=False,
                                                               use_cls=True, use_rec=True)
                        if sonuc.txts:
                            adaylar.append((sonuc.txts[0], float(sonuc.scores[0])))
                    s.metin, s.guven = max(adaylar, key=lambda a: self.sozluk.puan(*a))
                    if self.tesseract and s.guven < self.ayar.ocr.ikinci_esik:
                        try:
                            adaylar.append(self._tesseract(kirpinti))
                        except (OSError, IsHatasi, ValueError) as e:
                            log.warning("Tesseract atlandı: %s", e)
                        s.metin, s.guven = max(adaylar, key=lambda a: self.sozluk.puan(*a))
            s.metin = self.sozluk.duzelt(s.metin, s.guven)
        satirlar = sirala(satirlar, (gri.shape[1], gri.shape[0]))
        for s in satirlar:
            x0, y0, x1, y1 = s.kutu
            s.kutu = donusum.geri_kutu([(x0, y0), (x1, y0), (x1, y1), (x0, y1)])
        return satirlar, ortalama(satirlar)


@dataclass
class OcrIsi:
    yol: str
    no: int = 1
    tur: str = "goruntu"
    etiket: str = ""


def _is_oku(motor, is_, ayar):
    import numpy as np
    if is_.tur == "pdf":
        import pymupdf
        with pymupdf.open(is_.yol) as belge:
            pix = belge[is_.no - 1].get_pixmap(dpi=ayar.ocr.dpi, colorspace=pymupdf.csRGB, alpha=False)
            gorsel = np.frombuffer(pix.samples, np.uint8).reshape(pix.height, pix.width, 3)[:, :, ::-1].copy()
            del pix
    else:
        from PIL import Image, ImageOps
        with Image.open(is_.yol) as im:
            im.seek(is_.no - 1)
            gorsel = np.asarray(ImageOps.exif_transpose(im).convert("RGB"))[:, :, ::-1].copy()
    satirlar, guven = motor.oku(gorsel)
    return Sayfa(is_.no, is_.etiket or f"Sayfa {is_.no}", "ocr", birlestir(satirlar), guven,
                 satirlar, (gorsel.shape[1], gorsel.shape[0]))


def _isci(ayar, gelen, giden):
    motor = OcrMotoru(ayar)
    while True:
        is_ = gelen.get()
        if is_ is None:
            return
        try:
            giden.put(("ok", _is_oku(motor, is_, ayar)))
        except OrtamHatasi as e:
            giden.put(("ortam", str(e)))
        except Exception as e:
            giden.put(("hata", f"OCR okunamadı: {e}"))


def _isci_baslangic(hedef, ayar, gelen, giden):
    # Windows modül yüklemesi sayfanın çalışma süresinden ayrı tutulur.
    giden.put(("hazir", None))
    hedef(ayar, gelen, giden)


class OcrHavuzu:
    def __init__(self, ayar, isci_hedefi=None):
        self.ayar = ayar
        self.sayi = ayar.dizin.isci or max(1, (os.cpu_count() or 1) // 2)
        self.baglam = multiprocessing.get_context("spawn")
        self.hedef = isci_hedefi or _isci
        self.yuvalar = []
        self.motor = None
        self.ilerleme = None

    def _baslat(self):
        gelen, giden = self.baglam.Queue(1), self.baglam.Queue(1)
        p = self.baglam.Process(target=_isci_baslangic, args=(self.hedef, self.ayar, gelen, giden), daemon=True)
        p.start()
        return {"p": p, "gelen": gelen, "giden": giden, "is": None, "bas": 0, "hazir": False}

    @staticmethod
    def _kapat(yuva):
        p = yuva["p"]
        if p.is_alive():
            p.terminate()
        p.join(timeout=5)
        if p.is_alive():
            p.kill()
            p.join(timeout=5)
        for ad in ("gelen", "giden"):
            yuva[ad].cancel_join_thread()
            yuva[ad].close()
        p.close()

    def haritala(self, isler):
        """Sınırlı sayıda bekleyen iş; sonuç sırası serbest, sayfa no ile birleştirilir."""
        if self.sayi == 1:
            for is_ in isler:
                if self.motor is None:
                    self.motor = OcrMotoru(self.ayar)
                yield _is_oku(self.motor, is_, self.ayar), []
                if self.ilerleme:
                    self.ilerleme()
            return
        it = iter(isler)
        bitti = False
        while True:
            if self.ilerleme:
                self.ilerleme()
            for i in range(self.sayi):
                if i < len(self.yuvalar) and self.yuvalar[i]["is"] is not None:
                    continue
                try:
                    is_ = next(it)
                except StopIteration:
                    bitti = True
                    break
                if i == len(self.yuvalar):
                    self.yuvalar.append(self._baslat())
                yuva = self.yuvalar[i]
                yuva["is"], yuva["bas"] = is_, time.monotonic()
                yuva["gelen"].put(is_)
            bekleyen = False
            for i, yuva in enumerate(self.yuvalar):
                is_ = yuva["is"]
                if is_ is None:
                    continue
                bekleyen = True
                try:
                    durum, veri = yuva["giden"].get_nowait()
                except queue.Empty:
                    sinir = self.ayar.ocr.sayfa_zaman_asimi_sn if yuva["hazir"] else max(30, self.ayar.ocr.sayfa_zaman_asimi_sn)
                    if (time.monotonic() - yuva["bas"] < sinir
                            and yuva["p"].is_alive()):
                        continue
                    self._kapat(yuva)
                    self.yuvalar[i] = self._baslat()
                    durum, veri = "hata", f"Sayfa {is_.no}: OCR zaman aşımı veya işçi kapandı"
                if durum == "hazir":
                    yuva["hazir"], yuva["bas"] = True, time.monotonic()
                    continue
                yuva["is"] = None
                if durum == "ortam":
                    raise OrtamHatasi(veri)
                if durum == "ok":
                    yield veri, []
                else:
                    yield Sayfa(is_.no, is_.etiket or f"Sayfa {is_.no}", "ocr", "", 0), [veri]
            if bitti and not bekleyen:
                break
            time.sleep(.01)

    def oku(self, is_):
        return next(self.haritala([is_]))

    def kapat(self):
        for yuva in self.yuvalar:
            self._kapat(yuva)
        self.yuvalar.clear()
        self.motor = None

    def __enter__(self):
        return self

    def __exit__(self, *args):
        self.kapat()
