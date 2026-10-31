"""Ortak sözleşmenin karakter tabanlı Türkçe normalizasyonu."""
import unicodedata

_KATLAMA = dict.fromkeys("ıİIiîÎ", "i")
for _harfler, _hedef in (("şŞ", "s"), ("ğĞ", "g"), ("üÜûÛ", "u"),
                          ("öÖ", "o"), ("çÇ", "c"), ("âÂ", "a")):
    _KATLAMA.update(dict.fromkeys(_harfler, _hedef))


def _kucult(c: str) -> str:
    if c in _KATLAMA:
        return _KATLAMA[c]
    k = c.lower()
    return k if len(k) == 1 else c


def icerik_normalle(s: str) -> str:
    sonuc = []
    for c in s:
        kat = unicodedata.category(c)
        if ord(c) > 0xFFFF:
            sonuc.append(" ")
        elif kat == "Mn":
            continue
        elif kat[0] in "PSZ" or kat == "Cc" or c in "'’‘`´\t\n\v\f\r\x85\xa0":
            sonuc.append(" ")
        else:
            sonuc.append(_kucult(c))
    # split() daha geniş bir Unicode boşluk kümesine dokunurdu.
    return " ".join(p for p in "".join(sonuc).split(" ") if p)


def dosya_normalle(s: str) -> str:
    return "".join(_kucult(c) for c in s if unicodedata.category(c) != "Mn")
