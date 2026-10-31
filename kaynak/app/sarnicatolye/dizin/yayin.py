"""Yerel VACUUM anlık görüntüsünü ağ deposuna atomik yayımlar."""
from datetime import datetime
from contextlib import closing
import hashlib
import json
import logging
import os
from pathlib import Path
import shutil
import sqlite3
import tempfile

from ..hatalar import IsHatasi
from ..belge.onbellek import json_yaz

log = logging.getLogger("sarnicatolye")


class Yayinci:
    def __init__(self, db, depo, sakla_yayin=3):
        self.db, self.depo, self.sakla_yayin = db, Path(depo), sakla_yayin
        self.yol = Path(db.execute("PRAGMA database_list").fetchone()[2])
        # Ayrı bağlantı: yalnız ana VT commit'leri data_version'ı değiştirir.
        self.gozlem = sqlite3.connect(self.yol)
        self.son_surum = None
        self.sira = 0
        self.durum_yolu = self.yol.with_name(self.yol.name + ".yayin.json")
        try:
            self.onceki = json.loads(self.durum_yolu.read_text(encoding="utf-8"))
            if not isinstance(self.onceki, dict):
                self.onceki = {}
        except (OSError, ValueError):
            self.onceki = {}

    @staticmethod
    def _ozet(yol):
        h = hashlib.sha256()
        with yol.open("rb") as f:
            for parca in iter(lambda: f.read(1024 * 1024), b""):
                h.update(parca)
        return h.hexdigest()

    def yayinla(self):
        self.db.commit()
        surum = self.gozlem.execute("PRAGMA data_version").fetchone()[0]
        if surum == self.son_surum:
            return None
        yayin = self.depo / "yayin"
        yayin.mkdir(parents=True, exist_ok=True)
        # Eski yarıda kalmış .tmp dosyalarını temizle
        for p in yayin.glob("*.tmp"):
            try:
                p.unlink(missing_ok=True)
            except OSError:
                pass
        with tempfile.TemporaryDirectory(prefix="yayin-", dir=self.yol.parent) as tmp:
            yerel = Path(tmp) / "dizin.db"
            self.db.execute("VACUUM INTO ?", (str(yerel),))
            with closing(sqlite3.connect(yerel)) as denetim:
                sonuc = denetim.execute("PRAGMA quick_check").fetchall()
                if sonuc != [("ok",)]:
                    raise IsHatasi("Dizin bütünlük denetimi başarısız")
            ozet = None
            if self.son_surum is None:
                ozet = self._ozet(yerel)
                # Yeniden başlatmada data_version sıfırlanır. Yerel, sıkıştırılmış VT
                # özeti değişmediyse aynı yayını ağa yeniden kopyalama.
                onceki_ad = self.onceki.get("ad", "")
                if (self.onceki.get("ozet") == ozet and self.onceki.get("depo") == str(self.depo.resolve())
                        and onceki_ad and Path(onceki_ad).name == onceki_ad):
                    try:
                        ayni = ((yayin / "guncel.txt").read_text(encoding="utf-8").strip() == onceki_ad
                                and (yayin / onceki_ad).is_file())
                    except OSError:
                        ayni = False
                    if ayni:
                        self.son_surum = surum
                        return None
            # Aynı saniyede yeniden başlatma veya çok yayın ad çakıştırmaz.
            while True:
                self.sira += 1
                ad = f"dizin-{datetime.now():%Y%m%d-%H%M%S}-{self.sira:06d}.db"
                hedef = yayin / ad
                if not hedef.exists():
                    break
            gecici = hedef.with_name(ad + ".tmp")
            try:
                shutil.copyfile(yerel, gecici)
                os.replace(gecici, hedef)
            except BaseException:
                gecici.unlink(missing_ok=True)
                raise
            isaret = yayin / "guncel.txt.tmp"
            with isaret.open("w", encoding="utf-8", newline="\n") as f:
                f.write(ad + "\n")
                f.flush()
                os.fsync(f.fileno())
            os.replace(isaret, yayin / "guncel.txt")
            self.onceki = {"ozet": ozet or self.onceki.get("ozet"), "ad": ad, "depo": str(self.depo.resolve())}
            json_yaz(self.durum_yolu, self.onceki)
        self.son_surum = surum
        eskiler = sorted(yayin.glob("dizin-*.db"), reverse=True)
        for p in eskiler[self.sakla_yayin:]:
            try:
                p.unlink()
            except OSError:
                pass
        log.info("Dizin yayımlandı: %s", hedef)
        return hedef

    def kapat(self):
        self.gozlem.close()

    def __enter__(self):
        return self

    def __exit__(self, *args):
        self.kapat()
