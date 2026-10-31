"""OCR ön işlemesi ve özgün piksele ters afin dönüşüm."""
from dataclasses import dataclass, field
import math

import cv2
import numpy as np

GURULTU_LAPLACIAN_ESIK = 1500.0


@dataclass
class Donusum:
    boyut: tuple[int, int]
    olcek: float = 1.0
    aci: float = 0.0
    yon: int = 0
    matris: np.ndarray = field(default_factory=lambda: np.eye(3))

    def ekle(self, m):
        self.matris = np.asarray(m) @ self.matris

    def geri_kutu(self, kutu):
        p = np.asarray(kutu, dtype=float).reshape(-1, 2)
        p = np.c_[p, np.ones(len(p))] @ np.linalg.inv(self.matris).T
        w, h = self.boyut
        return (float(np.clip(p[:, 0].min(), 0, w)), float(np.clip(p[:, 1].min(), 0, h)),
                float(np.clip(p[:, 0].max(), 0, w)), float(np.clip(p[:, 1].max(), 0, h)))


def dondur(gri, aci):
    h, w = gri.shape[:2]
    m = cv2.getRotationMatrix2D((w / 2, h / 2), aci, 1.0)
    nw = math.ceil(h * abs(m[0, 1]) + w * abs(m[0, 0]))
    nh = math.ceil(h * abs(m[0, 0]) + w * abs(m[0, 1]))
    m[0, 2] += (nw - w) / 2
    m[1, 2] += (nh - h) / 2
    return (cv2.warpAffine(gri, m, (nw, nh), borderValue=255),
            np.vstack((m, [0, 0, 1])))


def egiklik(gri):
    h, w = gri.shape
    oran = min(1.0, 700 / max(h, w))
    kucuk = cv2.resize(gri, None, fx=oran, fy=oran, interpolation=cv2.INTER_AREA)
    _, bw = cv2.threshold(kucuk, 0, 255, cv2.THRESH_BINARY_INV + cv2.THRESH_OTSU)
    if np.count_nonzero(bw) < 20:
        return 0.0
    h, w = bw.shape
    var_y = float(np.var(np.sum(bw, axis=1)))
    var_x = float(np.var(np.sum(bw, axis=0)))
    if var_x > 2.0 * var_y and var_x > 50:
        return 0.0
    en_aci, en_puan = 0.0, float(np.sum(np.sum(bw / 255, axis=1) ** 2))
    ilk = en_puan
    for aci in np.arange(-15.0, 15.01, 0.5):
        m = cv2.getRotationMatrix2D((w / 2, h / 2), float(aci), 1)
        donuk = cv2.warpAffine(bw, m, (w, h), flags=cv2.INTER_NEAREST)
        puan = float(np.sum(np.sum(donuk / 255, axis=1) ** 2))
        if puan > en_puan:
            en_aci, en_puan = float(aci), puan
    aday_araliklar = [np.arange(en_aci - 0.5, en_aci + 0.51, 0.1)]
    if abs(en_aci) > 0.6:
        aday_araliklar.append(np.arange(-0.6, 0.61, 0.1))
    for aralik in aday_araliklar:
        for aci in aralik:
            if abs(aci) > 15.0:
                continue
            m = cv2.getRotationMatrix2D((w / 2, h / 2), float(aci), 1)
            donuk = cv2.warpAffine(bw, m, (w, h), flags=cv2.INTER_NEAREST)
            puan = float(np.sum(np.sum(donuk / 255, axis=1) ** 2))
            if puan > en_puan:
                en_aci, en_puan = float(aci), puan
    return round(en_aci, 2) if en_puan > ilk * 1.03 else 0.0


def hazirla(gorsel):
    gri = (cv2.cvtColor(gorsel, cv2.COLOR_BGR2GRAY) if gorsel.ndim == 3 else gorsel.copy())
    h, w = gri.shape
    donusum = Donusum((w, h))
    if min(h, w) < 1000:
        gri = cv2.resize(gri, None, fx=2, fy=2, interpolation=cv2.INTER_CUBIC)
        donusum.olcek = 2.0
        donusum.ekle(np.diag([2., 2., 1.]))
    aci = egiklik(gri)
    if abs(aci) >= 0.3:
        gri, m = dondur(gri, aci)
        donusum.aci = aci
        donusum.ekle(m)
    if gri.std() < 40:
        gri = cv2.createCLAHE(clipLimit=2.0, tileGridSize=(8, 8)).apply(gri)
    if cv2.Laplacian(gri, cv2.CV_64F).var() > GURULTU_LAPLACIAN_ESIK:
        gri = cv2.fastNlMeansDenoising(gri, h=3, templateWindowSize=7, searchWindowSize=15)
    return gri, donusum


def yon_cevir(gri, yon):
    h, w = gri.shape[:2]
    matrisler = {0: np.eye(3), 90: np.array([[0, 1, 0], [-1, 0, w - 1], [0, 0, 1]]),
                180: np.array([[-1, 0, w - 1], [0, -1, h - 1], [0, 0, 1]]),
                270: np.array([[0, -1, h - 1], [1, 0, 0], [0, 0, 1]])}
    return np.ascontiguousarray(np.rot90(gri, yon // 90)), matrisler[yon]


def metin_murekkebi(gri):
    _, bw = cv2.threshold(gri, 0, 255, cv2.THRESH_BINARY_INV + cv2.THRESH_OTSU)
    _, _, istatistik, _ = cv2.connectedComponentsWithStats(bw, 8)
    alan = 0
    for x, y, w, h, piksel in istatistik[1:]:
        if 6 <= h <= 120 and 0.1 <= w / h <= 15:
            alan += int(piksel)
    return alan
