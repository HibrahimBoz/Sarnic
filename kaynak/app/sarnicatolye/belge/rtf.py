"""RTF metni: gruplar, atlanan hedefler, cp1254 ve Unicode kaçışları."""
import re

_TOKEN = re.compile(r"\\([a-zA-Z]+)(-?\d+)? ?|\\'([0-9a-fA-F]{2})|\\([^a-zA-Z])|([{}])|([^\\{}]+)")
_ATLA = {"fonttbl", "colortbl", "stylesheet", "info", "pict", "object", "header", "footer",
         "filetbl", "listtable", "listoverridetable", "datastore", "themedata", "fldinst"}


def soy(ham: bytes) -> str:
    yigin, cikti = [], []
    atla, uc, yedek = False, 1, 0
    kodlama = "cp1254"
    for m in _TOKEN.finditer(ham.decode("latin1")):
        kelime, sayi, hex_, simge, grup, duz = m.groups()
        if grup == "{":
            yigin.append((atla, uc, kodlama))
        elif grup == "}":
            if yigin:
                atla, uc, kodlama = yigin.pop()
        elif simge == "*" or kelime in _ATLA:
            atla = True
        elif kelime == "ansicpg" and sayi:
            kodlama = "cp" + sayi
        elif kelime == "uc" and sayi:
            uc = int(sayi)
        elif kelime == "u" and sayi:
            if not atla:
                cikti.append(chr(int(sayi) & 0xFFFF))
                yedek = uc
        elif not atla:
            if kelime in ("par", "line", "page"):
                cikti.append("\n")
            elif kelime == "tab":
                cikti.append("\t")
            else:
                if hex_:
                    metin = bytes([int(hex_, 16)]).decode(kodlama, errors="replace")
                elif simge:
                    metin = {"~": " ", "_": "-", "-": "", "\n": "", "\r": ""}.get(simge, simge)
                elif duz:
                    metin = duz.encode("latin1").decode(kodlama, errors="replace").replace("\r", "").replace("\n", "")
                else:
                    continue
                n = min(yedek, len(metin))
                yedek -= n
                cikti.append(metin[n:])
    return "".join(cikti).encode("utf-16", errors="surrogatepass").decode("utf-16", errors="replace")
