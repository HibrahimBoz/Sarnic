-- Bulgu dizin veritabanı şeması, sürüm 1.
-- TEK KAYNAK: Python dizinleyici (sarnicatolye/dizin/vt.py) bu dosyayı okuyup çalıştırır,
-- C# testleri aynı dosyadan veritabanı kurar. Değişiklikte meta.surum artırılır.

PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS meta(
    anahtar TEXT PRIMARY KEY,
    deger   TEXT NOT NULL
);

-- Taranan her dosya ve klasör (Everything benzeri ad araması için).
CREATE TABLE IF NOT EXISTS dosya(
    id           INTEGER PRIMARY KEY,
    yol          TEXT NOT NULL UNIQUE,   -- tam yol, Windows biçimi
    ad           TEXT NOT NULL,          -- uzantılı ad
    klasor       TEXT NOT NULL,          -- üst klasörün tam yolu
    uzanti       TEXT NOT NULL,          -- küçük harf, noktasız; yoksa ''
    boyut        INTEGER NOT NULL,       -- bayt; klasörde 0
    degisme      INTEGER NOT NULL,       -- unix saniye (UTC)
    tur          TEXT NOT NULL,          -- 'klasor' | 'belge' | 'goruntu' | 'video' | 'diger'
    durum        TEXT NOT NULL,          -- 'yok' | 'bekliyor' | 'tamam' | 'hata' | 'desteklenmiyor'
    hata         TEXT,
    sha          TEXT,                   -- içerik sha256 (yalnız içeriği işlenen dosyada)
    sayfa_sayisi INTEGER,
    ocr_sayfa    INTEGER,
    ders         TEXT,                   -- bağlı ders klasörünün tam yolu (..._islenmis) ya da NULL
    norm_ad      TEXT NOT NULL,          -- dosya_normalle(ad): Türkçe harf katlama + küçük harf, noktalama KORUNUR
    norm_yol     TEXT NOT NULL           -- dosya_normalle(yol)
);
CREATE INDEX IF NOT EXISTS ix_dosya_klasor ON dosya(klasor);
CREATE INDEX IF NOT EXISTS ix_dosya_uzanti ON dosya(uzanti);
CREATE INDEX IF NOT EXISTS ix_dosya_ders   ON dosya(ders);
CREATE INDEX IF NOT EXISTS ix_dosya_durum  ON dosya(durum);
CREATE INDEX IF NOT EXISTS ix_dosya_sha    ON dosya(sha);
CREATE INDEX IF NOT EXISTS ix_dosya_tur    ON dosya(tur);
CREATE INDEX IF NOT EXISTS ix_dosya_boyut  ON dosya(boyut);
CREATE INDEX IF NOT EXISTS ix_dosya_degisme ON dosya(degisme);
CREATE INDEX IF NOT EXISTS ix_dosya_norm_ad ON dosya(norm_ad);

-- Ad alt dize araması (normalize sonrası 3+ karakter). 1-2 karakter ve yol: araması norm_ad/norm_yol LIKE ile.
CREATE VIRTUAL TABLE IF NOT EXISTS dosya_fts USING fts5(
    norm_ad,
    content='dosya', content_rowid='id',
    tokenize='trigram'
);
CREATE TRIGGER IF NOT EXISTS dosya_ai AFTER INSERT ON dosya BEGIN
    INSERT INTO dosya_fts(rowid, norm_ad) VALUES (new.id, new.norm_ad);
END;
CREATE TRIGGER IF NOT EXISTS dosya_ad AFTER DELETE ON dosya BEGIN
    INSERT INTO dosya_fts(dosya_fts, rowid, norm_ad) VALUES ('delete', old.id, old.norm_ad);
END;
CREATE TRIGGER IF NOT EXISTS dosya_au AFTER UPDATE OF norm_ad ON dosya BEGIN
    INSERT INTO dosya_fts(dosya_fts, rowid, norm_ad) VALUES ('delete', old.id, old.norm_ad);
    INSERT INTO dosya_fts(rowid, norm_ad) VALUES (new.id, new.norm_ad);
END;

-- Belge içeriği: sayfa (PDF sayfası, slayt, tablo parçası, bölüm) başına bir satır.
CREATE TABLE IF NOT EXISTS sayfa(
    id       INTEGER PRIMARY KEY,
    dosya_id INTEGER NOT NULL REFERENCES dosya(id) ON DELETE CASCADE,
    no       INTEGER NOT NULL,           -- 1 tabanlı sıra
    etiket   TEXT NOT NULL,              -- 'Sayfa 3', 'Slayt 2', 'Tablo1 (satır 1-200)', 'Ek: a.pdf / Sayfa 1'
    kaynak   TEXT NOT NULL,              -- 'metin' | 'ocr'
    guven    REAL,                       -- OCR ortalama güveni 0..1; metinde NULL
    metin    TEXT NOT NULL,              -- gösterilen özgün metin (NFKC uygulanmış)
    dil      TEXT,                       -- 'tr' | 'en' | 'az' | 'ru' | ...
    ceviri   TEXT                        -- Türkçe çeviri metni; Türkçe veya çevrilmemişse NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_sayfa_dosya_no ON sayfa(dosya_id, no);

-- İçeriksiz FTS: normalize metin yalnız dizinde durur (sütun olarak saklanmaz, boyut yarıya iner).
-- Yazar her sayfa için: INSERT INTO sayfa_fts(rowid, norm) VALUES (sayfa.id, icerik_normalle(metin)).
CREATE VIRTUAL TABLE IF NOT EXISTS sayfa_fts USING fts5(
    norm,
    content='', contentless_delete=1,
    tokenize='unicode61 remove_diacritics 0',
    prefix='2 3'
);
CREATE VIRTUAL TABLE IF NOT EXISTS sayfa_ceviri_fts USING fts5(
    norm,
    content='', contentless_delete=1,
    tokenize='unicode61 remove_diacritics 0',
    prefix='2 3'
);

CREATE TRIGGER IF NOT EXISTS sayfa_ad AFTER DELETE ON sayfa BEGIN
    DELETE FROM sayfa_fts WHERE rowid = old.id;
    DELETE FROM sayfa_ceviri_fts WHERE rowid = old.id;
END;

INSERT OR IGNORE INTO meta(anahtar, deger) VALUES ('surum', '1');
