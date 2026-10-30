"""Harici araçlarda süre sınırı ve süreç ağacını kapatma."""
import os
import subprocess

from ..hatalar import IsHatasi


def calistir(komut, zaman_asimi, ad):
    p = subprocess.Popen([str(a) for a in komut], stdin=subprocess.DEVNULL,
                         stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                         creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    basarili = False
    try:
        try:
            cikti, hata = p.communicate(timeout=zaman_asimi)
        except subprocess.TimeoutExpired as e:
            raise IsHatasi(f"{ad} zaman aşımı") from e
        basarili = True
    finally:
        if not basarili:
            try:
                if os.name == "nt":
                    subprocess.run(["taskkill", "/PID", str(p.pid), "/T", "/F"],
                                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                                   creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0), check=False)
                p.kill()
            except Exception:
                pass
            try:
                p.communicate(timeout=10)
            except Exception:
                pass
            for b in (p.stdin, p.stdout, p.stderr):
                if b:
                    try:
                        b.close()
                    except Exception:
                        pass
    if p.returncode:
        raise IsHatasi(f"{ad} başarısız: {hata.decode('utf-8', errors='replace').strip()[:240]}")
    return cikti
