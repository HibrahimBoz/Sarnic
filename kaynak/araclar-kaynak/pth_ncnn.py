"""SRVGGNetCompact (.pth) -> ncnn (.param/.bin) dönüştürücü. torch GEREKMEZ (numpy + zipfile + pickle).

Kullanım: python pth_ncnn.py girdi.pth cikti_ad [olcek=4] [ikinci.pth alfa]
Mimari: Conv(3->nf) PReLU {Conv(nf->nf) PReLU}*N Conv(nf->3*s*s) PixelShuffle(s) + nearest-yukarı-örneklenmiş girdi (Real-ESRGAN
SRVGGNetCompact; realesr-animevideov3 ve realesr-general-x4v3 aynı yapıdadır).
Ağırlıklar float32 yazılır (ncnn etiketi 0). Üretilen .param/.bin realesrgan-ncnn-vulkan'ın models klasörüne konur.
"""
import pickle
import struct
import sys
import zipfile
from collections import OrderedDict

import numpy as np


class _Depo:
    def __init__(self, anahtar, numel):
        self.anahtar, self.numel = anahtar, numel


def _tensor_kur(depo, ofset, boyut, adim, *_):
    return ("tensor", depo, ofset, tuple(boyut), tuple(adim))


class _Acici(pickle.Unpickler):
    def __init__(self, f, z, kok):
        super().__init__(f)
        self.z, self.kok = z, kok

    def find_class(self, modul, ad):
        if ad == "_rebuild_tensor_v2":
            return _tensor_kur
        if ad == "OrderedDict":
            return OrderedDict
        if ad.endswith("Storage"):
            return ad
        # Güvenlik: .pth bir pickle'dır; yalnız yukarıdaki beyaz liste çözülür, başka her sınıf reddedilir.
        raise pickle.UnpicklingError(f"izin verilmeyen sınıf: {modul}.{ad}")

    def persistent_load(self, pid):
        _, tip, anahtar, _, numel = pid
        return _Depo(anahtar, numel)


def agirliklari_oku(yol):
    z = zipfile.ZipFile(yol)
    pkl = next(n for n in z.namelist() if n.endswith("data.pkl"))
    kok = pkl[: -len("data.pkl")]
    veri = _Acici(z.open(pkl), z, kok).load()
    if "params" in veri:
        veri = veri["params"]
    cikis = OrderedDict()
    for ad, t in veri.items():
        _, depo, ofset, boyut, adim = t
        ham = np.frombuffer(z.read(f"{kok}data/{depo.anahtar}"), dtype="<f4")
        n = int(np.prod(boyut)) if boyut else 1
        cikis[ad] = ham[ofset: ofset + n].reshape(boyut).copy()
    return cikis


def donustur(pth, ad, olcek=4, pth2=None, alfa=1.0):
    """pth2 verilirse ağırlıklar alfa*pth + (1-alfa)*pth2 olur (Real-ESRGAN -dn gürültü giderme karışımı)."""
    w = agirliklari_oku(pth)
    if pth2:
        w2 = agirliklari_oku(pth2)
        w = OrderedDict((k, alfa * v + (1 - alfa) * w2[k]) for k, v in w.items())
    idx = sorted({int(k.split(".")[1]) for k in w if k.startswith("body.")})
    son = idx[-1]
    satir, bin_ = [], bytearray()
    blob_sayac = [0]
    blobs = set()

    def yeni():
        blob_sayac[0] += 1
        return f"b{blob_sayac[0]}"

    satir.append("Input                    input.1                  0 1 data")
    satir.append("Split                    splitncnn_input0         1 2 data input.1_splitncnn_0 input.1_splitncnn_1")
    onceki = "input.1_splitncnn_1"
    n = 0
    for i in idx:
        wk, bk = f"body.{i}.weight", f"body.{i}.bias"
        if bk in w:  # Conv
            cikis_ch, giris_ch, k, _ = w[wk].shape
            hedef = yeni()
            satir.append(f"Convolution              Conv_{n:<18}1 1 {onceki} {hedef} 0={cikis_ch} 1={k} 4={k//2} 5=1 6={w[wk].size}")
            bin_ += struct.pack("<I", 0) + w[wk].astype("<f4").tobytes() + w[bk].astype("<f4").tobytes()
        else:  # PReLU
            ch = w[wk].size
            hedef = yeni()
            satir.append(f"PReLU                    PRelu_{n:<18}1 1 {onceki} {hedef} 0={ch}")
            bin_ += w[wk].astype("<f4").tobytes()
        onceki = hedef
        n += 1
    ps = yeni()
    satir.append(f"PixelShuffle             DepthToSpace             1 1 {onceki} {ps} 0={olcek}")
    rs = yeni()
    satir.append(f"Interp                   Resize                   1 1 input.1_splitncnn_0 {rs} 0=1 1={float(olcek):.6e} 2={float(olcek):.6e}")
    satir.append(f"BinaryOp                 Add                      2 1 {ps} {rs} output")
    for s in satir:
        for tok in s.split()[3:]:
            if "=" not in tok and not tok.isdigit():
                blobs.add(tok)
    # katman satırı: ad, giriş/çıkış sayıları sonrasındaki blob adları
    blobs = set()
    for s in satir:
        p = s.split()
        ng, nc = int(p[2]), int(p[3])
        blobs.update(p[4: 4 + ng + nc])
    with open(f"{ad}.param", "w", encoding="ascii", newline="\n") as f:
        f.write("7767517\n")
        f.write(f"{len(satir)} {len(blobs)}\n")
        f.write("\n".join(satir) + "\n")
    with open(f"{ad}.bin", "wb") as f:
        f.write(bin_)
    return len(satir), len(blobs), len(bin_)


if __name__ == "__main__":
    pth, ad = sys.argv[1], sys.argv[2]
    olcek = int(sys.argv[3]) if len(sys.argv) > 3 else 4
    pth2 = sys.argv[4] if len(sys.argv) > 4 else None
    alfa = float(sys.argv[5]) if len(sys.argv) > 5 else 1.0
    print(donustur(pth, ad, olcek, pth2, alfa))
