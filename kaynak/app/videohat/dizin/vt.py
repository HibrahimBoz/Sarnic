"""Tek kaynaktan SQLite şeması ve sayfa FTS yazımı."""
from pathlib import Path
from contextlib import contextmanager
import os
import sqlite3
import unicodedata

from .. import yollar
from ..hatalar import OrtamHatasi
from .normal import icerik_normalle


def _kanon(p):
    return os.path.normcase(os.path.abspath(str(p))) if p else ""


@contextmanager
def yazar_kilidi(yol):
    yol = Path(yol)
    yol.parent.mkdir(parents=True, exist_ok=True)
    with yol.with_name(yol.name + ".kilit").open("a+b") as f:
        if not f.tell():
            f.write(b"0")
            f.flush()
        f.seek(0)
        try:
            if os.name == "nt":
                import msvcrt
                msvcrt.locking(f.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.flock(f, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError as e:
            raise OrtamHatasi("Dizin başka bir yazar tarafından kullanılıyor") from e
        try:
            yield
        finally:
            f.seek(0)
            if os.name == "nt":
                msvcrt.locking(f.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                fcntl.flock(f, fcntl.LOCK_UN)


def ac(yol) -> sqlite3.Connection:
    yol = Path(yol)
    yol.parent.mkdir(parents=True, exist_ok=True)
    db = sqlite3.connect(yol, timeout=30)
    db.row_factory = sqlite3.Row
    db.create_function("kanon", 1, _kanon)
    db.execute("PRAGMA journal_mode=WAL")
    db.execute("PRAGMA foreign_keys=ON")
    db.execute("PRAGMA temp_store=FILE")
    try:
        db.executescript(yollar.ortak("dizin_sema.sql").read_text(encoding="utf-8"))
        if db.execute("SELECT deger FROM meta WHERE anahtar=?", ("surum",)).fetchone()[0] != "1":
            raise OrtamHatasi("Desteklenmeyen dizin şema sürümü")
        # Yazar ek dizini: büyük/küçük harfi değişmiş yolu tam tablo taraması olmadan bulur (okurlar kullanmaz)
        db.execute("CREATE INDEX IF NOT EXISTS ix_dosya_yol_nocase ON dosya(yol COLLATE NOCASE)")
    except BaseException:
        db.close()
        raise
    return db


def sayfalari_yaz(db, dosya_id, sayfalar):
    db.execute("DELETE FROM sayfa WHERE dosya_id=?", (dosya_id,))
    for s in sayfalar:
        metin = unicodedata.normalize("NFKC", s["metin"]).strip()
        cur = db.execute("INSERT INTO sayfa(dosya_id,no,etiket,kaynak,guven,metin) VALUES (?,?,?,?,?,?)",
                         (dosya_id, s["no"], s["etiket"], s["kaynak"], s.get("guven"), metin))
        db.execute("INSERT INTO sayfa_fts(rowid,norm) VALUES (?,?)", (cur.lastrowid, icerik_normalle(metin)))
