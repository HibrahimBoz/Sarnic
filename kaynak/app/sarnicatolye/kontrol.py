"""Kendi kendine test: `kontrol.bat`. Çıkış kodu 0 = geçti."""
import logging
import re
import shutil
import subprocess
import tempfile
from pathlib import Path

from . import yollar
from .hatalar import SarnicAtolyeHatasi

log = logging.getLogger("sarnicatolye")
ORNEK = Path(__file__).parent / "ornek"


def _norm(metin: str) -> list[str]:
    """Türkçe küçük harf (İ→i, I→ı), noktalama sil, kelimelere böl."""
    metin = metin.replace("İ", "i").replace("I", "ı").lower()
    metin = re.sub(r"[^\w\s]", " ", metin, flags=re.UNICODE)
    return metin.split()


def kelime_orani(beklenen: str, bulunan: str) -> float:
    """Beklenen kelimelerin bulunan metinde yer alma oranı (0..1)."""
    b = _norm(beklenen)
    if not b:
        return 0.0
    havuz = _norm(bulunan)
    say = 0
    for k in b:
        if k in havuz:
            havuz.remove(k)
            say += 1
    return say / len(b)


def calistir() -> int:
    satirlar: list[tuple[str, bool, str]] = []

    def ekle(ad: str, tamam: bool, ayrinti: str) -> None:
        satirlar.append((ad, tamam, ayrinti))

    # 1. ffmpeg
    try:
        c = subprocess.run([str(yollar.ffmpeg()), "-version"], capture_output=True, text=True, timeout=30)
        ekle("ffmpeg", c.returncode == 0, c.stdout.splitlines()[0] if c.stdout else "?")
    except (SarnicAtolyeHatasi, OSError, subprocess.SubprocessError) as e:
        ekle("ffmpeg", False, str(e))

    # 2. modeller
    mevcut = [m for m in ("large-v3", "medium", "turbo") if yollar.model_var_mi(m)]
    ekle("modeller", bool(mevcut), ", ".join(mevcut) or "hiç model yok")

    # 3. cihaz
    secim = None
    try:
        from . import cihaz
        from . import ayar as ayar_mod
        ayar = ayar_mod.yukle(yollar.kok())
        bilgi = cihaz.gpu_bilgi()
        ekle("gpu", True, str(bilgi) if bilgi else "GPU yok / nvidia-smi yok (CPU yolu)")
        secim = cihaz.sec(ayar)
        ekle("cihaz seçimi", True, f"{secim.device}/{secim.compute_type}/{secim.model_ad}: {secim.neden}")
    except (SarnicAtolyeHatasi, Exception) as e:  # noqa: BLE001 — kontrol raporu her hatada düşmemeli
        ekle("cihaz seçimi", False, f"{type(e).__name__}: {e}")

    # 4. uçtan uca (örnek ses)
    wav = ORNEK / "ornek_tr.wav"
    beklenen = ORNEK / "ornek_tr.txt"
    if not wav.exists() or not beklenen.exists():
        ekle("uçtan uca", False, f"örnek yok: {wav}")
    elif secim is not None:
        try:
            from .hat import Bayraklar, isle_tek
            gecici_kok = Path(tempfile.mkdtemp(prefix="sarnicatolye_kontrol_"))
            gecici = gecici_kok / f"{wav.stem}_islenmis"
            try:
                gecici.mkdir()
                s = isle_tek(wav, gecici, ayar, Bayraklar())
                if not s.basarili:
                    ekle("uçtan uca", False, s.hata or "?")
                else:
                    txt = next(gecici.glob("*.txt"), None)
                    oran = kelime_orani(beklenen.read_text(encoding="utf-8"),
                                        txt.read_text(encoding="utf-8") if txt else "")
                    ekle("uçtan uca", oran >= 0.6,
                         f"kelime eşleşmesi %{oran*100:.0f} (eşik %60), cihaz={s.cihaz}, "
                         f"süreler={ {k: round(v, 1) for k, v in s.sureler.items()} }")
            finally:
                shutil.rmtree(gecici_kok, ignore_errors=True)
        except Exception as e:  # noqa: BLE001
            ekle("uçtan uca", False, f"{type(e).__name__}: {e}")

    # 5. ağ kilidi ihlali
    try:
        from . import ag_kilidi
        n = ag_kilidi.ihlal_sayisi()
        ekle("ağ ihlali", n == 0, f"{n} deneme (is\\gunluk\\ag_ihlal.log)")
    except Exception as e:  # noqa: BLE001
        ekle("ağ ihlali", False, str(e))

    print("\n=== Sarnıç Atölye kontrol ===")
    for ad, tamam, ayrinti in satirlar:
        print(f"[{'TAMAM' if tamam else 'HATA '}] {ad}: {ayrinti}")
    gecti = all(t for _, t, _ in satirlar)
    print("SONUÇ:", "GEÇTİ" if gecti else "KALDI")
    return 0 if gecti else 2
