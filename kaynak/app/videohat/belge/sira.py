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


def _oluk_ayir(kutular, genislik, yukseklik=None):
    if len(kutular) < 4:
        return None
    h = yukseklik if yukseklik is not None else (max(s.kutu[3] for s in kutular) - min(s.kutu[1] for s in kutular))
    if h <= 0:
        return None
    # Kesintisiz dikey oluk; iki yanda en az iki satır bulunmalı.
    araliklar = sorted((s.kutu[0], s.kutu[2]) for s in kutular)
    sag = araliklar[0][1]
    for bas, son in araliklar[1:]:
        if bas - sag >= genislik * .02:
            sol = [s for s in kutular if s.kutu[2] <= sag]
            diger = [s for s in kutular if s.kutu[0] >= bas]
            if all(len(_satirlar(g)) >= 2 and
                   max(s.kutu[3] for s in g) - min(s.kutu[1] for s in g) >= .6 * h
                   for g in (sol, diger)):
                return sol, diger
        sag = max(sag, son)
    return None


def _bantlar(kutular):
    """Dikey izdüşümde boşlukla ayrılan yatay bantlar (yukarıdan aşağı)."""
    bantlar, alt = [], None
    for s in sorted(kutular, key=lambda k: k.kutu[1]):
        if bantlar and s.kutu[1] < alt:
            bantlar[-1].append(s)
            alt = max(alt, s.kutu[3])
        else:
            bantlar.append([s])
            alt = s.kutu[3]
    return bantlar


def _ic_bosluk(bant, genislik):
    """Bant içindeki ilk yatay boşluk (sütun aralığı adayı) (sol_son, sag_bas); yoksa None."""
    araliklar = sorted((s.kutu[0], s.kutu[2]) for s in bant)
    sag = araliklar[0][1]
    for bas, son in araliklar[1:]:
        if bas - sag >= genislik * .02:
            return sag, bas
        sag = max(sag, son)
    return None


def _bloklar(kutular, genislik):
    """XY-cut: önce bölge boyu dikey oluk; yoksa yatay bantlara böl, boşluklu ardışık bantları (sütun satırları)
    tek grupta topla, oluğu kesen başlık/altbilgi bantları ayrı blok olur; her grup yeniden kesilir."""
    if not kutular:
        return []
    # Üstte/altta tek kutulu, geniş ya da ortalanmış bant (başlık, altbilgi) sütun bölmesinden önce ayrılır
    bantlar = _bantlar(kutular)
    x0, x1 = min(s.kutu[0] for s in kutular), max(s.kutu[2] for s in kutular)
    orta, en = (x0 + x1) / 2, max(x1 - x0, 1)

    def kapsayan(bant):
        if len(bant) != 1 or len(bantlar) < 2:
            return False
        k = bant[0].kutu
        return k[2] - k[0] >= .5 * en or abs((k[0] + k[2]) / 2 - orta) <= .1 * en

    bas, son = 0, len(bantlar)
    while bas < son and kapsayan(bantlar[bas]):
        bas += 1
    while son > bas and kapsayan(bantlar[son - 1]):
        son -= 1
    if bas or son < len(bantlar):
        orta_kutular = [s for b in bantlar[bas:son] for s in b]
        return ([b for b in bantlar[:bas]] + _bloklar(orta_kutular, genislik) + [b for b in bantlar[son:]])
    bol = _oluk_ayir(kutular, genislik)
    if bol:
        return _bloklar(bol[0], genislik) + _bloklar(bol[1], genislik)
    # grup: [oluk (sol_son, sag_bas) ya da None, kutular]. Oluğu kesmeyen tek sütunluk bant (bir sütunda
    # paragraf boşluğu) sütun grubunda kalır; oluğu kesen bant (ara başlık) ayrı blok olur.
    gruplar = []
    for bant in _bantlar(kutular):
        oluk = _ic_bosluk(bant, genislik)
        son_oluk = gruplar[-1][0] if gruplar else None
        if son_oluk and (oluk or all(k.kutu[2] <= son_oluk[1] or k.kutu[0] >= son_oluk[0] for k in
                                     (s for s in bant))):
            if all(k.kutu[2] <= son_oluk[1] or k.kutu[0] >= son_oluk[0] for k in bant):
                gruplar[-1][1].extend(bant)
                if oluk:  # ortak oluk: iki bandın kesişimi
                    gruplar[-1][0] = (max(son_oluk[0], oluk[0]), min(son_oluk[1], oluk[1]))
                continue
        gruplar.append([oluk, list(bant)])
    if len(gruplar) <= 1:
        return [kutular]
    return [blok for _, grup in gruplar for blok in _bloklar(grup, genislik)]


def sirala(satirlar, boyut):
    genislik, yukseklik = boyut
    return [s for blok in _bloklar(satirlar, genislik) for grup in _satirlar(blok) for s in grup] if satirlar else []


def birlestir(satirlar):
    if not satirlar:
        return ""
    if hasattr(satirlar, "metin") and satirlar.metin:
        return satirlar.metin
    dikey = sum(1 for s in satirlar if (s.kutu[3] - s.kutu[1]) > 1.2 * (s.kutu[2] - s.kutu[0])) > len(satirlar) * 0.5
    if dikey:
        med = median(max(s.kutu[2] - s.kutu[0], 1) for s in satirlar)
        parcalar = [satirlar[0].metin]
        for once, simdi in zip(satirlar, satirlar[1:]):
            a, b = once.kutu, simdi.kutu
            ortusme = min(a[2], b[2]) - max(a[0], b[0])
            if ortusme >= .5 * min(a[2] - a[0], b[2] - b[0]) and (b[1] >= a[3] or (b[1] >= a[1] and a[3] - b[1] < .5 * (a[3] - a[1]))):
                ayrac = " "
            else:
                ayrac = "\n\n" if abs(b[0] - a[2]) > 1.5 * med else "\n"
            parcalar.extend((ayrac, simdi.metin))
        return "".join(parcalar)
    med = median(max(s.kutu[3] - s.kutu[1], 1) for s in satirlar)
    parcalar = [satirlar[0].metin]
    for once, simdi in zip(satirlar, satirlar[1:]):
        a, b = once.kutu, simdi.kutu
        ortusme = min(a[3], b[3]) - max(a[1], b[1])
        if ortusme >= .5 * min(a[3] - a[1], b[3] - b[1]) and (b[0] >= a[2] or (b[0] >= a[0] and a[2] - b[0] < .5 * (a[2] - a[0]))):
            ayrac = " "
        else:
            ayrac = "\n\n" if b[1] - a[3] > 1.5 * med else "\n"
        parcalar.extend((ayrac, simdi.metin))
    return "".join(parcalar)


def ortalama(satirlar):
    toplam = sum(len(s.metin) for s in satirlar)
    return sum(len(s.metin) * s.guven for s in satirlar) / toplam if toplam else 0.0
