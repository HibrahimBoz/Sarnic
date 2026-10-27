"""Video Transkriptör - Çok kameralı grup işleme ve orkestrasyon modülü."""
from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime
import json
import logging
import os
from pathlib import Path
import re
import subprocess
import tempfile
from typing import Any, Callable, Dict, List, Optional, Tuple
import wave

import numpy as np

from . import cikti, ders_json, parca_zinciri, senkron, ses, yollar
from .cihaz import CihazSecimi
from .hat import Bayraklar
from .parca_zinciri import dogal_siralama_anahtari
from .stt import Segment

gunluk = logging.getLogger("videohat")
VIDEO_UZANTILARI = {".mp4", ".mkv", ".mov", ".avi", ".m4a", ".wav", ".mp3"}
_UZANTI_DESENI = "|".join(sorted(re.escape(u.lstrip(".")) for u in VIDEO_UZANTILARI))
KALIP_REGEX = re.compile(rf"^(.*?)[_-]?(\d+)\.({_UZANTI_DESENI})$", re.IGNORECASE)
CIKTI_KLASOR_EKLERI = ("_islenmis", "_videoperisi")


def _cikti_klasoru_mu(ad: str) -> bool:
    return ad.lower().endswith(CIKTI_KLASOR_EKLERI)


@dataclass
class GrupSonucu:
    """Çok kameralı ders işleme sonucu."""
    ders_adi: str
    basarili: bool
    cikis_klasoru: Path
    ders_tanimi: Optional[ders_json.DersTanimi] = None
    uyarilar: List[str] = field(default_factory=list)
    cikti_dosyalari: List[Path] = field(default_factory=list)
    hata: Optional[str] = None


def kanallari_bul(klasor: str | Path) -> Dict[str, List[Path]]:
    """
    Ders klasöründeki kanalları ve dosyalarını bulur (doğal sırada).
    Önce alt klasörleri kanal kabul eder; yoksa regex dosya kalıbı ile düz klasörde arar.
    """
    klasor = Path(klasor)
    if not klasor.exists() or not klasor.is_dir():
        return {}

    sonuc: Dict[str, List[Path]] = {}

    # 1. Alt klasör düzeni kontrolü
    alt_klasorler = [
        d for d in klasor.iterdir()
        if d.is_dir() and not d.name.startswith(".") and not _cikti_klasoru_mu(d.name)
    ]
    alt_harita: Dict[str, List[Path]] = {}
    video_bulundu = False
    for alt in alt_klasorler:
        dosyalar = [
            f for f in alt.iterdir()
            if f.is_file() and f.suffix.lower() in VIDEO_UZANTILARI
        ]
        sirali = sorted(dosyalar, key=lambda f: dogal_siralama_anahtari(f.name))
        alt_harita[alt.name] = sirali
        if sirali:
            video_bulundu = True

    if video_bulundu:
        return dict(sorted(alt_harita.items(), key=lambda kv: dogal_siralama_anahtari(kv[0])))

    # 2. Düz klasörde regex ile arama: ^(.*?)[_-]?(\d+)\.(mp4|mkv|mov|avi)$
    duz_dosyalar = [
        f for f in klasor.iterdir()
        if f.is_file() and f.suffix.lower() in VIDEO_UZANTILARI
    ]
    for f in duz_dosyalar:
        m = KALIP_REGEX.match(f.name)
        if m:
            kanal_adi = m.group(1).strip()
            if not kanal_adi:
                kanal_adi = "kanal"
            sonuc.setdefault(kanal_adi, []).append(f)
        else:
            kanal_adi = f.stem
            sonuc.setdefault(kanal_adi, []).append(f)

    if sonuc:
        for k in sonuc:
            sonuc[k] = sorted(sonuc[k], key=lambda f: dogal_siralama_anahtari(f.name))
        return dict(sorted(sonuc.items(), key=lambda kv: dogal_siralama_anahtari(kv[0])))

    if alt_harita:
        return dict(sorted(alt_harita.items(), key=lambda kv: dogal_siralama_anahtari(kv[0])))

    return {}


def _ffprobe_prob(dosya: Path | str) -> Tuple[Optional[float], float]:
    """ffprobe ile creation_time (epoch sn) ve duration (sn) okur."""
    dosya = Path(dosya)
    ffprobe_exe = str(yollar.ffprobe())
    cmd = [
        ffprobe_exe,
        "-v", "error",
        "-show_entries", "format=duration:format_tags=creation_time",
        "-of", "json",
        str(dosya),
    ]
    try:
        res = subprocess.run(
            cmd,
            check=True,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
        )
        veri = json.loads(res.stdout)
        fmt = veri.get("format", {})
        sure_sn = float(fmt.get("duration", 0.0))
        creation_time = fmt.get("tags", {}).get("creation_time")
        epoch: Optional[float] = None
        if creation_time:
            try:
                iso_str = creation_time.replace("Z", "+00:00")
                dt = datetime.fromisoformat(iso_str)
                epoch = dt.timestamp()
            except Exception:
                epoch = None
        return epoch, sure_sn
    except Exception as e:
        gunluk.warning("ffprobe prob hatası (%s): %s", dosya, e)
        return None, 0.0


def hazirla_kanal(
    parcalar: List[Path | str],
    prob_fn: Optional[Callable[[str], Tuple[Optional[float], float]]] = None,
) -> parca_zinciri.SanalZamanCizelgesi:
    """Parça dosyalarını prob_fn ile inceleyip parca_zinciri çizelgesi oluşturur."""
    if prob_fn is None:
        prob_fn = _ffprobe_prob

    parca_listesi = []
    for d in parcalar:
        d_str = str(d)
        baslangic, sure = prob_fn(d_str)
        parca_listesi.append(
            parca_zinciri.Parca(
                dosya=d_str,
                baslangic_zamani=baslangic,
                sure_sn=float(sure),
            )
        )
    return parca_zinciri.cizelge_olustur(parca_listesi)


def varsayilan_ses_enerjisi(kanal_veya_ses: Any, veri: Any = None) -> float:
    """
    Ses sinyalinin RMS (Root Mean Square) enerjisini hesaplar.
    Sessiz kanalların referans seçilmesini engeller.
    """
    dizi = veri if veri is not None else kanal_veya_ses
    if isinstance(dizi, np.ndarray):
        if dizi.size == 0:
            return 0.0
        return float(np.sqrt(np.mean(np.square(dizi.astype(np.float64, copy=False)))))
    return 0.0


def referans_sec(
    kanallar: Any,
    ses_enerji_fn: Optional[Callable] = None,
    manuel_override: Optional[str] = None,
    **kwargs,
) -> str:
    """
    En yüksek ortalama konuşma enerjisi/SNR'li kanalı seçer.
    Manuel override parametresi verilmişse önceliklidir.
    """
    secilen = kwargs.get("referans", kwargs.get("override", manuel_override))
    kanal_adlari = list(kanallar.keys()) if isinstance(kanallar, dict) else list(kanallar)

    if not kanal_adlari:
        raise ValueError("Kanal listesi boş olamaz.")

    if secilen:
        return secilen

    if ses_enerji_fn is None:
        ses_enerji_fn = varsayilan_ses_enerjisi

    skorlar: Dict[str, float] = {}
    for k in kanal_adlari:
        veri = kanallar[k] if isinstance(kanallar, dict) else None
        if veri is not None:
            try:
                skorlar[k] = float(ses_enerji_fn(veri))
            except TypeError:
                try:
                    skorlar[k] = float(ses_enerji_fn(k, veri))
                except TypeError:
                    skorlar[k] = float(ses_enerji_fn(k))
        else:
            try:
                skorlar[k] = float(ses_enerji_fn(k))
            except TypeError:
                skorlar[k] = float(ses_enerji_fn(k, veri))

    return max(kanal_adlari, key=lambda k: skorlar.get(k, 0.0))


def ses_yukle_komutu(dosya: Path | str, sr: int = 16000) -> list[str]:
    """ffmpeg ile raw ses okuma komutunu üretir."""
    ffmpeg_exe = str(yollar.ffmpeg())
    return [
        ffmpeg_exe,
        "-v", "error",
        "-i", str(dosya),
        "-f", "f32le",
        "-ac", "1",
        "-ar", str(sr),
        "pipe:1",
    ]


def _ffmpeg_ses_yukle(dosya: Path | str, sr: int = 8000) -> np.ndarray:
    """ffmpeg ile dosyadan mono float32 raw ses yükler."""
    dosya = Path(dosya)
    cmd = ses_yukle_komutu(dosya, sr=sr)
    try:
        res = subprocess.run(
            cmd,
            check=True,
            capture_output=True,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
        )
        return np.frombuffer(res.stdout, dtype=np.float32)
    except Exception as e:
        gunluk.warning("ffmpeg ses yükleme hatası (%s): %s", dosya, e)
        return np.zeros(0, dtype=np.float32)


def ses_birlestir(
    cizelge: parca_zinciri.SanalZamanCizelgesi,
    ses_yukle_fn: Optional[Callable[[str, int], np.ndarray]] = None,
    sr: int = 8000,
) -> np.ndarray:
    """
    Kanalın tüm parçalarını sanal çizelgeye göre birleştirip tek bir dizi döndürür.
    Boşluklar sıfır dolgulanır.
    """
    if ses_yukle_fn is None:
        ses_yukle_fn = _ffmpeg_ses_yukle

    toplam_sure = cizelge.toplam_sure
    n_ornek = int(round(toplam_sure * sr))
    if n_ornek <= 0:
        return np.zeros(0, dtype=np.float32)

    birlesik = np.zeros(n_ornek, dtype=np.float32)
    onbellek: Dict[str, np.ndarray] = {}

    for dilim in cizelge.dilimler:
        if dilim.tur == "bosluk" or not dilim.dosya:
            continue
        if dilim.dosya not in onbellek:
            try:
                onbellek[dilim.dosya] = np.asarray(ses_yukle_fn(dilim.dosya, sr), dtype=np.float32)
            except TypeError:
                onbellek[dilim.dosya] = np.asarray(ses_yukle_fn(dilim.dosya), dtype=np.float32)

        dosya_sesi = onbellek[dilim.dosya]
        i_bas = int(round(dilim.yerel_bas * sr))
        i_uzunluk = int(round(dilim.sure * sr))
        i_son = i_bas + i_uzunluk

        if i_bas < len(dosya_sesi):
            parca_ses = dosya_sesi[i_bas:min(len(dosya_sesi), i_son)]
        else:
            parca_ses = np.zeros(0, dtype=np.float32)

        s_bas = int(round(dilim.sanal_bas * sr))
        s_son = min(n_ornek, s_bas + len(parca_ses))
        if s_bas < n_ornek and s_son > s_bas:
            birlesik[s_bas:s_son] = parca_ses[:s_son - s_bas]

    return birlesik


def senkronla(
    ref_ses: np.ndarray,
    kanal_sesleri: Dict[str, np.ndarray],
    sr: int = 8000,
) -> Dict[str, senkron.SenkronSonucu]:
    """Her kanal için senkron.olc ile ofset, drift ve güven skoru ölçer."""
    sonuclar: Dict[str, senkron.SenkronSonucu] = {}
    for kanal_ad, x_ses in kanal_sesleri.items():
        if np.array_equal(ref_ses, x_ses):
            sonuclar[kanal_ad] = senkron.SenkronSonucu(
                ofset_sn=0.0,
                drift_ppm=0.0,
                guven=1.0,
                pencere_olcumleri=[],
                guvenilir=True,
                neden="referans kanal",
            )
        else:
            sonuclar[kanal_ad] = senkron.olc(ref_ses, x_ses, sr=sr)
    return sonuclar


class TranskriptSonucu(list):
    """Segment listesi ve transkripsiyon üstverisi."""

    def __init__(self, segmentler=(), meta: Optional[Dict[str, Any]] = None):
        super().__init__(segmentler)
        self.meta: Dict[str, Any] = meta or {}


def _varsayilan_transkript(
    ref_ses: np.ndarray,
    cikis_dir: Path,
    ayar: Any,
    bayraklar: Any,
    sr: int = 16000,
    meta_cikti: Optional[Dict[str, Any]] = None,
) -> List[Segment]:
    """Gerçek çalışma için referans sesi geçici klasörde işler ve ara çıktıları temizler."""
    from . import hat
    if ayar is None:
        from . import ayar as ayar_mod
        ayar = ayar_mod.yukle(yollar.kok())
    if bayraklar is None:
        from .hat import Bayraklar
        bayraklar = Bayraklar()

    with tempfile.TemporaryDirectory() as tmp_dir_str:
        tmp_dir = Path(tmp_dir_str)
        tmp_wav = tmp_dir / "ref_birlestirilmis.wav"
        ses_int16 = np.clip(ref_ses * 32767.0, -32768, 32767).astype(np.int16)
        with wave.open(str(tmp_wav), "wb") as wf:
            wf.setnchannels(1)
            wf.setsampwidth(2)
            wf.setframerate(sr)
            wf.writeframes(ses_int16.tobytes())

        # hat.isle_tek geçici klasörde çalıştırılır; son çıktı klasörü kirletilmez
        s = hat.isle_tek(tmp_wav, tmp_dir, ayar, bayraklar)
        if not s.basarili:
            hata_mesaji = s.hata or "Transkripsiyon başarısız oldu."
            gunluk.error("hat.isle_tek hatası: %s", hata_mesaji)
            raise RuntimeError(hata_mesaji)
        json_yolu = tmp_dir / f"{tmp_wav.stem}.json"
        if json_yolu.exists():
            veri = json.loads(json_yolu.read_text(encoding="utf-8"))
            if meta_cikti is not None:
                meta_cikti.update(veri)
            segmentler = [
                Segment(s_item["bas"], s_item["son"], s_item["metin"])
                for s_item in veri.get("segmentler", [])
            ]
            return TranskriptSonucu(segmentler, meta=veri)
        return TranskriptSonucu([])


def isle_grup(
    klasor: str | Path,
    ayar: Any = None,
    bayraklar: Optional[Bayraklar] = None,
    *,
    referans_kanal_adi: Optional[str] = None,
    prob_fn: Optional[Callable] = None,
    ses_yukle_fn: Optional[Callable] = None,
    ses_enerji_fn: Optional[Callable] = None,
    transkript_fn: Optional[Callable] = None,
) -> GrupSonucu:
    """
    3-4 kameralı çok parçalı ders klasörünü orkestre ederek tek ders olarak işler.
    Çıktıları <klasor>/<ders_adi>_islenmis/ altına ve ders.json'u kaydeder.
    Eksik/boş kanal durumunda çökmez, uyarı listesi döndürür.
    """
    klasor = Path(klasor)
    ders_adi = klasor.name
    cikis_dir = klasor / f"{ders_adi}_islenmis"
    uyarilar: List[str] = []

    if not klasor.exists() or not klasor.is_dir():
        return GrupSonucu(
            ders_adi=ders_adi,
            basarili=False,
            cikis_klasoru=cikis_dir,
            uyarilar=["Klasör mevcut değil veya dizin değil."],
            hata="Klasör bulunamadı",
        )

    kanallar = kanallari_bul(klasor)
    if not kanallar:
        uyarilar.append(f"Klasörde işlenebilecek kanal veya video bulunamadı: {klasor}")
        return GrupSonucu(
            ders_adi=ders_adi,
            basarili=False,
            cikis_klasoru=cikis_dir,
            uyarilar=uyarilar,
            hata="Hiç kanal bulunamadı",
        )

    gecerli_kanallar: Dict[str, Tuple[List[Path], parca_zinciri.SanalZamanCizelgesi]] = {}
    for k_ad, dosyalar in kanallar.items():
        if not dosyalar:
            uyarilar.append(f"Kanal boş: {k_ad}")
            continue
        cizelge = hazirla_kanal(dosyalar, prob_fn)
        if not cizelge.dilimler or cizelge.toplam_sure <= 0.0:
            uyarilar.append(f"Kanalda geçerli süreye sahip parça bulunamadı: {k_ad}")
            continue
        for u in cizelge.uyarilar:
            uyarilar.append(f"Kanal {k_ad} uyarısı: {u}")
        gecerli_kanallar[k_ad] = (dosyalar, cizelge)

    if not gecerli_kanallar:
        uyarilar.append("Hiç geçerli kanal bulunamadı.")
        return GrupSonucu(
            ders_adi=ders_adi,
            basarili=False,
            cikis_klasoru=cikis_dir,
            uyarilar=uyarilar,
            hata="Geçerli kanal kalmadı",
        )

    # Seslerin birleştirilmesi (8 kHz senkron ve enerji ölçümü için)
    sr_senkron = 8000
    kanal_sesleri: Dict[str, np.ndarray] = {}
    for k_ad, (_, cizelge) in gecerli_kanallar.items():
        kanal_sesleri[k_ad] = ses_birlestir(cizelge, ses_yukle_fn, sr=sr_senkron)

    # Referans kanal seçimi (en yüksek RMS enerjili kanal)
    ref_ad = referans_sec(
        kanal_sesleri,
        ses_enerji_fn=ses_enerji_fn,
        manuel_override=referans_kanal_adi,
    )
    if ref_ad not in gecerli_kanallar:
        uyarilar.append(f"Belirtilen referans kanal '{ref_ad}' bulunamadı; varsayılan seçildi.")
        ref_ad = list(gecerli_kanallar.keys())[0]

    ref_ses_senkron = kanal_sesleri[ref_ad]

    # Transkript için referans kanalın sesi tam bantta (16 kHz) çıkarılır
    sr_transkript = 16000
    ref_cizelge = gecerli_kanallar[ref_ad][1]
    ref_ses_transkript = ses_birlestir(ref_cizelge, ses_yukle_fn, sr=sr_transkript)

    # Senkronizasyon (Tek kanalda atlanır)
    senkron_sonuclari: Dict[str, senkron.SenkronSonucu] = {}
    if len(gecerli_kanallar) <= 1:
        senkron_sonuclari[ref_ad] = senkron.SenkronSonucu(
            ofset_sn=0.0,
            drift_ppm=0.0,
            guven=1.0,
            pencere_olcumleri=[],
            guvenilir=True,
            neden="tek kanal (senkron atlandi)",
        )
        genel_senkron = ders_json.SenkronBilgisi(
            yontem="tek_kanal",
            drift_ppm=0.0,
            guven=1.0,
            guvenilir=True,
            neden="tek kanal (senkron atlandi)",
        )
    else:
        senkron_sonuclari = senkronla(ref_ses_senkron, kanal_sesleri, sr=sr_senkron)
        tum_guvenilir = all(s.guvenilir for s in senkron_sonuclari.values())
        min_guven = min((s.guven for s in senkron_sonuclari.values()), default=1.0)
        ortalama_drift = float(np.mean([s.drift_ppm for s in senkron_sonuclari.values()]))
        genel_senkron = ders_json.SenkronBilgisi(
            yontem="gcc_phat",
            drift_ppm=ortalama_drift,
            guven=min_guven,
            guvenilir=tum_guvenilir,
            neden="basarili" if tum_guvenilir else "bazi kanallar guvenilir degil",
        )

    # Kanal ve parça kayıtlarını hazırla
    kanal_listesi: List[ders_json.Kanal] = []
    for k_ad, (_, cizelge) in gecerli_kanallar.items():
        s_sonuc = senkron_sonuclari.get(k_ad)
        k_ofset = s_sonuc.ofset_sn if s_sonuc else 0.0
        k_drift = s_sonuc.drift_ppm if s_sonuc else 0.0
        k_guven = s_sonuc.guven if s_sonuc else 1.0
        k_guvenilir = s_sonuc.guvenilir if s_sonuc else True
        k_neden = s_sonuc.neden if s_sonuc else "basarili"

        parca_kayitlari: List[ders_json.ParcaKaydi] = []
        for dilim in cizelge.dilimler:
            goreli_yol = ""
            if dilim.dosya:
                try:
                    goreli_yol = os.path.relpath(dilim.dosya, cikis_dir).replace("\\", "/")
                except Exception:
                    goreli_yol = str(dilim.dosya).replace("\\", "/")

            parca_kayitlari.append(
                ders_json.ParcaKaydi(
                    dosya=goreli_yol,
                    sanal_bas=dilim.sanal_bas,
                    sanal_son=dilim.sanal_son,
                    yerel_bas=dilim.yerel_bas,
                    tur=dilim.tur,
                    ofset_sn=k_ofset,
                    guven=k_guven,
                )
            )

        kanal_listesi.append(
            ders_json.Kanal(
                ad=k_ad,
                parcalar=parca_kayitlari,
                ofset_sn=k_ofset,
                drift_ppm=k_drift,
                guven=k_guven,
                guvenilir=k_guvenilir,
                neden=k_neden,
            )
        )

    ders_tanimi = ders_json.DersTanimi(
        ad=ders_adi,
        kanallar=kanal_listesi,
        referans_kanal=ref_ad,
        senkron=genel_senkron,
    )

    # Transkripsiyon ve çıktılar
    cikis_dir.mkdir(parents=True, exist_ok=True)
    cikti_dosyalari: List[Path] = []

    transkript_meta: Dict[str, Any] = {}
    try:
        if transkript_fn is not None:
            try:
                bolumler = transkript_fn(ref_ses_transkript)
            except TypeError:
                bolumler = transkript_fn(ref_ses_transkript, cikis_dir)
            if hasattr(bolumler, "meta") and isinstance(bolumler.meta, dict):
                transkript_meta = bolumler.meta
        else:
            bolumler = _varsayilan_transkript(
                ref_ses_transkript,
                cikis_dir,
                ayar,
                bayraklar,
                sr=sr_transkript,
                meta_cikti=transkript_meta,
            )
            if hasattr(bolumler, "meta") and isinstance(bolumler.meta, dict):
                transkript_meta.update(bolumler.meta)
    except Exception as e:
        hata_mesaji = str(e)
        gunluk.error("Transkripsiyon hatası: %s", hata_mesaji)
        uyarilar.append(f"Transkripsiyon hatası: {hata_mesaji}")
        return GrupSonucu(
            ders_adi=ders_adi,
            basarili=False,
            cikis_klasoru=cikis_dir,
            ders_tanimi=ders_tanimi,
            uyarilar=uyarilar,
            hata=hata_mesaji,
        )

    if not isinstance(bolumler, list):
        bolumler = []

    # Altyazı ve metin dosyalarını üret
    srt_yolu = cikis_dir / f"{ders_adi}.srt"
    vtt_yolu = cikis_dir / f"{ders_adi}.vtt"
    txt_yolu = cikis_dir / f"{ders_adi}.txt"
    json_yolu = cikis_dir / f"{ders_adi}.json"
    ders_json_yolu = cikis_dir / "ders.json"

    cikti.srt(bolumler, srt_yolu)
    cikti.vtt(bolumler, vtt_yolu)
    cikti.txt(bolumler, txt_yolu)

    cihaz_adi = transkript_meta.get(
        "cihaz",
        bayraklar.cihaz if (bayraklar and bayraklar.cihaz) else "cpu",
    )
    compute_type = transkript_meta.get("compute_type", "float32")
    model_adi = transkript_meta.get(
        "model",
        bayraklar.model if (bayraklar and bayraklar.model) else "turbo",
    )
    neden = transkript_meta.get("neden", "grup_transkript")
    sureler = transkript_meta.get("sureler", {"grup_isleme": 0.0})

    secim = CihazSecimi(
        device=cihaz_adi,
        compute_type=compute_type,
        model_ad=model_adi,
        neden=neden,
    )
    cikti.json_meta(bolumler, secim, sureler, json_yolu)
    ders_json.yaz(ders_json_yolu, ders_tanimi)

    cikti_dosyalari.extend([srt_yolu, vtt_yolu, txt_yolu, json_yolu, ders_json_yolu])

    return GrupSonucu(
        ders_adi=ders_adi,
        basarili=True,
        cikis_klasoru=cikis_dir,
        ders_tanimi=ders_tanimi,
        uyarilar=uyarilar,
        cikti_dosyalari=cikti_dosyalari,
    )
