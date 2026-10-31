"""Ağ kilidi (test amaçlı): dış bağlantı denemesini engeller ve günlüğe yazar.

Etkinleştirme: SARNICATOLYE_AG_KILIDI=1 ortam değişkeni (python -m sarnicatolye başlarken kur() çağrılır).
Loopback (127.0.0.0/8, ::1) ve Unix soketleri serbesttir.
"""
import ipaddress
import sys
import time
from pathlib import Path

_kuruldu = False


def _loopback_mu(adres) -> bool:
    try:
        host = adres[0] if isinstance(adres, tuple) else adres
    except Exception:
        return False
    if not isinstance(host, str):
        return False
    if host in ("localhost", ""):
        return True
    try:
        return ipaddress.ip_address(host).is_loopback
    except ValueError:
        return False


def _yaz(olay: str, ayrinti) -> None:
    from . import yollar
    try:
        d = yollar.kok() / "is" / "gunluk"
        d.mkdir(parents=True, exist_ok=True)
        with open(d / "ag_ihlal.log", "a", encoding="utf-8") as f:
            f.write(f"{time.strftime('%Y-%m-%d %H:%M:%S')} {olay} {ayrinti!r}\n")
    except OSError:
        pass


def _kanca(olay: str, args) -> None:
    if olay == "socket.connect":
        adres = args[1] if len(args) > 1 else None
        if not _loopback_mu(adres):
            _yaz(olay, adres)
            raise PermissionError(f"Ağ kilidi: dış bağlantı engellendi {adres!r}")
    elif olay in ("socket.getaddrinfo", "socket.gethostbyname", "socket.gethostbyaddr"):
        host = args[0] if args else None
        if not _loopback_mu((host,)):
            _yaz(olay, host)
            raise PermissionError(f"Ağ kilidi: ad çözümleme engellendi {host!r}")


def kur() -> None:
    global _kuruldu
    if _kuruldu:
        return
    sys.addaudithook(_kanca)
    _kuruldu = True


def ihlal_sayisi() -> int:
    from . import yollar
    p: Path = yollar.kok() / "is" / "gunluk" / "ag_ihlal.log"
    return len(p.read_text(encoding="utf-8").splitlines()) if p.exists() else 0
