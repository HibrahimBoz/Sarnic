"""Sütunları ayır, her sütunda satırları soldan sağa oku."""
from statistics import median


def _satirlar(kutular):
    gruplar = []
    for s in sorted(kutular, key=lambda k: (k.kutu[1], k.kutu[0])):
        x0, y0, x1, y1 = s.kutu
        es = None
        for grup in reversed(gruplar):
            a, b = min(k.kutu[1] for k in grup), max(k.kutu[3] for k in grup)
            if min(y1, b) - max(y0, a) >= 0.5 * min(y1 - y0, b - a):
                es = grup
                break
        if es is None:
            gruplar.append([s])
        else:
            es.append(s)
    return [sorted(g, key=lambda s: s.kutu[0]) for g in gruplar]


def _oluk_ayir(kutular, genislik):
    if len(kutular) < 4:
        return None
    # Kesintisiz dikey oluk; iki yanda en az iki satır bulunmalı.
    araliklar = sorted((s.kutu[0], s.kutu[2]) for s in kutular)
    sag = araliklar[0][1]
    yukseklik = max(s.kutu[3] for s in kutular) - min(s.kutu[1] for s in kutular)
    for bas, son in araliklar[1:]:
        if bas - sag >= genislik * .02:
            sol = [s for s in kutular if s.kutu[2] <= sag]
            diger = [s for s in kutular if s.kutu[0] >= bas]
            if all(len(_satirlar(g)) >= 2 and
                   max(s.kutu[3] for s in g) - min(s.kutu[1] for s in g) >= .6 * yukseklik
                   for g in (sol, diger)):
                return sol, diger
        sag = max(sag, son)
    return None


def _bloklar(kutular, genislik):
    satirlar = _satirlar(kutular)
    # Oluk içine taşan kısa başlığı sağ/sol sütuna yapıştırma.
    for i in range(1, min(4, len(satirlar))):
        ust = [s for g in satirlar[:i] for s in g]
        alt = [s for g in satirlar[i:] for s in g]
        alt_bol = _oluk_ayir(alt, genislik)
        if alt_bol and min(s.kutu[1] for s in alt) > max(s.kutu[3] for s in ust):
            sol_son = max(s.kutu[2] for s in alt_bol[0])
            sag_bas = min(s.kutu[0] for s in alt_bol[1])
            if any(s.kutu[0] < sag_bas and s.kutu[2] > sol_son for s in ust):
                return _bloklar(ust, genislik) + _bloklar(alt, genislik)
    bol = _oluk_ayir(kutular, genislik)
    if bol:
        return _bloklar(bol[0], genislik) + _bloklar(bol[1], genislik)
    # Yatay boşlukla ayrılmış başlık, sütun oluğunu kapatabilir.
    med = median(max(s.kutu[3] - s.kutu[1], 1) for s in kutular)
    for i in range(1, len(satirlar)):
        ust = [s for g in satirlar[:i] for s in g]
        alt = [s for g in satirlar[i:] for s in g]
        bosluk = min(s.kutu[1] for s in alt) - max(s.kutu[3] for s in ust)
        if bosluk <= 0:
            continue
        # Önce yatay kesimi dene; yalnız gerçek sütun veya büyük boşluk varsa uygula.
        if bosluk >= 1.5 * med or _oluk_ayir(alt, genislik) or _oluk_ayir(ust, genislik):
            return _bloklar(ust, genislik) + _bloklar(alt, genislik)
    return [kutular]


def sirala(satirlar, boyut):
    return [s for blok in _bloklar(satirlar, boyut[0]) for grup in _satirlar(blok) for s in grup] if satirlar else []


def birlestir(satirlar):
    if not satirlar:
        return ""
    med = median(max(s.kutu[3] - s.kutu[1], 1) for s in satirlar)
    parcalar = [satirlar[0].metin]
    for once, simdi in zip(satirlar, satirlar[1:]):
        a, b = once.kutu, simdi.kutu
        ortusme = min(a[3], b[3]) - max(a[1], b[1])
        if ortusme >= .5 * min(a[3] - a[1], b[3] - b[1]) and b[0] >= a[2]:
            ayrac = " "
        else:
            ayrac = "\n\n" if b[1] - a[3] > 1.5 * med else "\n"
        parcalar.extend((ayrac, simdi.metin))
    return "".join(parcalar)


def ortalama(satirlar):
    toplam = sum(len(s.metin) for s in satirlar)
    return sum(len(s.metin) * s.guven for s in satirlar) / toplam if toplam else 0.0
