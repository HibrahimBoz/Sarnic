"""terimler.py birim testi: python test_terimler.py  (çıkış kodu = başarısız sayısı)"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "app"))
from sarnicatolye import terimler as T  # noqa: E402

kok = Path(__file__).resolve().parent.parent
tm = T.yukle(kok / "terimler.txt")
hata = 0


def kontrol(ad, kosul, ayrinti=""):
    global hata
    if not kosul:
        hata += 1
    print(("TAMAM " if kosul else "HATA  ") + ad + (f"  {ayrinti}" if ayrinti else ""))


kontrol("istem <= 480 karakter", 0 < len(tm.istem) <= T.ISTEM_KARAKTER_SINIRI, f"{len(tm.istem)} kr")
kontrol("istem darbe/pulse/deinterleaving/ECM var", all(k in tm.istem for k in ("darbe", "pulse", "deinterleaving", "ECM")))
kontrol("sozluk ve kural yüklü", len(tm.sozluk) > 40 and len(tm.kurallar) > 30, f"sozluk={len(tm.sozluk)} kural={len(tm.kurallar)}")

pozitif = {
    "bu ee si em teknikleri radarı yanıltır": "ECM",
    "dinterliving işlemi darbeleri ayırır": "deinterleaving",
    "deinterliving": "deinterleaving",
    "bir cemmer ve çaf kullanılır": "jammer",
    "pals genişliği ve e s m alıcısı": "pulse",
    "dopler kayması": "Doppler",
    "sigint ve elint": "SIGINT",
    "jammıng yapılır": "jamming",            # bulanık: jammıng ~ jamming
    "emittır tanıma": "emitter",             # bulanık
    "beamformıng yöntemi": "beamforming",    # bulanık
}
for girdi, beklenen in pozitif.items():
    cikti, n = T.duzelt_metin(girdi, tm)
    kontrol(f"düzelt: {girdi!r}", beklenen in cikti and n > 0, f"-> {cikti!r}")

# Yanlış pozitif: Türkçe düz metinde HİÇ değişiklik olmamalı
negatif_dosyalar = [kok / "tests" / "metin_tr.txt", kok / "baslat" / "OKU-BENI.txt",
                    Path.home() / ".claude" / "CLAUDE.md"]
for d in negatif_dosyalar:
    if not d.exists():
        continue
    metin = d.read_text(encoding="utf-8-sig")
    onb = {}
    degisen = []
    for satir in metin.splitlines():
        yeni, n = T.duzelt_metin(satir, tm, onb)
        if n:
            degisen.append((satir[:70], yeni[:70]))
    kontrol(f"yanlış pozitif yok: {d.name}", not degisen, str(degisen[:3]))

# Çekimli biçim bozulmamalı
for girdi in ("jammerlar karıştırır", "deinterleaving'i yapıyoruz", "darbeleri ayırır", "yanılma payı", "yanıltma tekniği"):
    cikti, n = T.duzelt_metin(girdi, tm)
    kontrol(f"korunur: {girdi!r}", cikti == girdi, f"-> {cikti!r}")
sys.exit(hata)
