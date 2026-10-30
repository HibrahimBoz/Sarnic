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
from .model import OcrSatir, Sayfa, SatirListesi
from .onisleme import hazirla, metin_murekkebi, yon_cevir, dondur, egiklik
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
            threads = self.ayar.genel.cpu_threads
            if not threads:
                threads = max(1, (os.cpu_count() or 1) // 2) if self.ayar.dizin.isci == 1 else 1
            params = {
                "Det.model_path": str(yollar.ocr_model(f"PP-OCRv6_det_{det}.onnx")),
                "Rec.model_path": str(yollar.ocr_model(f"PP-OCRv6_rec_{rec}.onnx")),
                "Cls.model_path": str(yollar.ocr_model("ch_ppocr_mobile_v2.0_cls_mobile.onnx")),
                "Det.ocr_version": OCRVersion.PPOCRV6, "Rec.ocr_version": OCRVersion.PPOCRV6,
                "Det.model_type": ModelType(det), "Rec.model_type": ModelType(rec),
                "Global.text_score": 0.0, "Global.log_level": "error",
                "EngineConfig.onnxruntime.use_cuda": cuda and self.ayar.ocr.cihaz != "cpu",
                # Tek süreçte çekirdekleri kullanır; çok süreçte 1'e sabitler
                "EngineConfig.onnxruntime.intra_op_num_threads": threads,
                "EngineConfig.onnxruntime.inter_op_num_threads": 1,
            }
            # Ortak modelleri RapidOCR başlatmadan önce paylaştır (mükerrer yüklemeyi önler)
            paylasilan_cls = next((m.text_cls for m in self.motorlar.values() if getattr(m, "text_cls", None) is not None), None)
            paylasilan_det = next((m.text_det for (d, _), m in self.motorlar.items() if d == det and getattr(m, "text_det", None) is not None), None)
            paylasilan_rec = next((m.text_rec for (_, r), m in self.motorlar.items() if r == rec and getattr(m, "text_rec", None) is not None), None)

            cls_init, det_init, rec_init = None, None, None
            try:
                if paylasilan_cls is not None:
                    cls_init = paylasilan_cls.__class__.__init__
                    paylasilan_cls.__class__.__init__ = lambda *a, **k: None
                if paylasilan_det is not None:
                    det_init = paylasilan_det.__class__.__init__
                    paylasilan_det.__class__.__init__ = lambda *a, **k: None
                if paylasilan_rec is not None:
                    rec_init = paylasilan_rec.__class__.__init__
                    paylasilan_rec.__class__.__init__ = lambda *a, **k: None

                self.motorlar[anahtar] = RapidOCR(params=params)
            finally:
                if cls_init is not None:
                    paylasilan_cls.__class__.__init__ = cls_init
                if det_init is not None:
                    paylasilan_det.__class__.__init__ = det_init
                if rec_init is not None:
                    paylasilan_rec.__class__.__init__ = rec_init

        motor = self.motorlar[anahtar]
        for (d, r), diger in self.motorlar.items():
            if d == det and diger.text_det is not None and motor.text_det is not diger.text_det:
                motor.text_det = diger.text_det
            if r == rec and diger.text_rec is not None and motor.text_rec is not diger.text_rec:
                motor.text_rec = diger.text_rec
            if diger.text_cls is not None and motor.text_cls is not diger.text_cls:
                motor.text_cls = diger.text_cls
        return motor

    def _oku(self, gorsel, det, rec, cls=True):
        sonuc = self._motor(det, rec)(gorsel, use_det=True, use_rec=True, use_cls=cls)
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
        dikey_sayisi = sum(1 for s in satirlar if (s.kutu[3] - s.kutu[1]) > 1.2 * (s.kutu[2] - s.kutu[0]))
        dikey_mi = bool(satirlar) and (dikey_sayisi > len(satirlar) * 0.5)
        oran = min(1.0, 1000 / max(gri.shape))
        kucuk = cv2.resize(gri, None, fx=oran, fy=oran, interpolation=cv2.INTER_AREA)
        en_yon = 0
        if ortalama(satirlar) < .5 or dikey_mi:
            def _yon_puani(kutular):
                if not kutular:
                    return 0.0
                guv = ortalama(kutular)
                yatay = sum(1 for s in kutular if (s.kutu[2] - s.kutu[0]) >= (s.kutu[3] - s.kutu[1]))
                return guv * (1.0 + yatay / len(kutular))
            en_yon, en_puan = 0, _yon_puani(satirlar)
            for yon in (90, 180, 270):
                deneme, _ = yon_cevir(kucuk, yon)
                puan = _yon_puani(self._oku(deneme, det, rec))
                if puan > en_puan:
                    en_yon, en_puan = yon, puan
        # Satır yönü sınıflandırıcısı (cls) ters satırı da düzeltip okur: yön ile 180° karşıtı (baş aşağı tarama
        # dahil) ayırt edilemez, satır sırası tersine döner (ölçüldü). Karar cls kapalı güvenle verilir.
        karsit = (en_yon + 180) % 360
        if satirlar and (ortalama(self._oku(yon_cevir(kucuk, karsit)[0], det, rec, cls=False))
                         > ortalama(self._oku(yon_cevir(kucuk, en_yon)[0], det, rec, cls=False))):
            en_yon = karsit
        if en_yon:
            gri, matris = yon_cevir(gri, en_yon)
            donusum.yon = en_yon
            donusum.ekle(matris)
            aci2 = egiklik(gri)
            if abs(aci2) >= 0.3:
                gri, mat = dondur(gri, aci2)
                donusum.aci += aci2
                donusum.ekle(mat)
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
        metin = birlestir(satirlar)
        for s in satirlar:
            x0, y0, x1, y1 = s.kutu
            s.kutu = donusum.geri_kutu([(x0, y0), (x1, y0), (x1, y1), (x0, y1)])
        sonuc_satirlar = SatirListesi(satirlar)
        sonuc_satirlar.metin = metin
        sonuc_satirlar.yon = donusum.yon
        return sonuc_satirlar, ortalama(satirlar)


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
    metin = getattr(satirlar, "metin", "") or birlestir(satirlar)
    sayfa = Sayfa(is_.no, is_.etiket or f"Sayfa {is_.no}", "ocr", metin, guven,
                  satirlar, (gorsel.shape[1], gorsel.shape[0]))
    sayfa.yon = getattr(satirlar, "yon", 0)
    return sayfa


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
            if os.name == "nt":
                import subprocess
                subprocess.run(["taskkill", "/PID", str(p.pid), "/T", "/F"],
                               stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                               creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0), check=False)
            p.terminate()
        p.join(timeout=5)
        if p.is_alive():
            p.kill()
            p.join(timeout=5)
        for ad in ("gelen", "giden"):
            try:
                yuva[ad].cancel_join_thread()
                yuva[ad].close()
            except Exception:
                pass
        try:
            p.close()
        except Exception:
            pass

    def _bosalt(self):
        for i, yuva in enumerate(self.yuvalar):
            if yuva["is"] is not None:
                self._kapat(yuva)
                self.yuvalar[i] = self._baslat()
            else:
                while True:
                    try:
                        durum, _ = yuva["giden"].get_nowait()
                        if durum == "hazir":
                            yuva["hazir"] = True
                    except queue.Empty:
                        break

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

        self._bosalt()
        self._cagri_id = getattr(self, "_cagri_id", 0) + 1
        cagri_id = self._cagri_id
        it = iter(isler)
        bitti = False
        try:
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
                    yuva["cagri_id"] = cagri_id
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
                    if yuva.get("cagri_id") != cagri_id:
                        continue
                    if durum == "ortam":
                        raise OrtamHatasi(veri)
                    if durum == "ok":
                        yield veri, []
                    else:
                        yield Sayfa(is_.no, is_.etiket or f"Sayfa {is_.no}", "ocr", "", 0), [veri]
                if bitti and not bekleyen:
                    break
                time.sleep(.01)
        finally:
            self._bosalt()

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
