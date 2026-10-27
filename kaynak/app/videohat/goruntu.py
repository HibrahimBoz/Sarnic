"""VideoHat görüntü iyileştirme ve Real-ESRGAN ile büyütme (upscale) modülü."""
import json
import logging
import shutil
import subprocess
from pathlib import Path

from . import yollar
from .ayar import Ayar
from .hatalar import IsHatasi, UpscaleHatasi


def _penceresiz() -> int:
    return getattr(subprocess, "CREATE_NO_WINDOW", 0)


def _ffmpeg_calistir(komut: list[str]) -> subprocess.CompletedProcess:
    try:
        return subprocess.run(
            komut,
            check=True,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            creationflags=_penceresiz(),
        )
    except subprocess.CalledProcessError as hata:
        satirlar = (hata.stderr or "").strip().splitlines()[-5:]
        ayrinti = "\n".join(satirlar)
        mesaj = f"FFmpeg çalıştırma hatası:\n{ayrinti}" if ayrinti else "FFmpeg çalıştırma hatası."
        raise IsHatasi(mesaj) from hata


def _nvidia_smi_calisiyor() -> bool:
    try:
        sonuc = subprocess.run(
            ["nvidia-smi"],
            check=True,
            capture_output=True,
            creationflags=_penceresiz(),
        )
        return sonuc.returncode == 0
    except Exception:
        return False


def _nvenc_destegi_var() -> bool:
    try:
        sonuc = subprocess.run(
            [str(yollar.ffmpeg()), "-encoders"],
            check=True,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            creationflags=_penceresiz(),
        )
        return "h264_nvenc" in (sonuc.stdout or "")
    except Exception:
        return False


def _fps_al(video: Path) -> str:
    try:
        sonuc = subprocess.run(
            [
                str(yollar.ffprobe()),
                "-v", "error",
                "-select_streams", "v:0",
                "-show_entries", "stream=r_frame_rate",
                "-of", "default=noprint_wrappers=1:nokey=1",
                str(video),
            ],
            check=True,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            creationflags=_penceresiz(),
        )
        fps = (sonuc.stdout or "").strip()
        if fps and fps != "0/0":
            return fps
    except Exception:
        pass
    return "30"


def _fps_oran(fps: str) -> float:
    """FPS metnini kayan noktalı sayıya çevirir ('30000/1001' -> 29.97)."""
    try:
        if not fps or not isinstance(fps, str):
            return 30.0
        fps = fps.strip()
        if "/" in fps:
            pay_s, payda_s = fps.split("/", 1)
            pay = float(pay_s)
            payda = float(payda_s)
            if payda == 0.0 or pay <= 0.0:
                return 30.0
            return pay / payda
        oran = float(fps)
        if oran <= 0.0:
            return 30.0
        return oran
    except Exception:
        return 30.0


def _olcek_sec(kaynak_yukseklik: int, hedef: int, model: str) -> int:
    """Hedef çözünürlüğe ve modele göre ölçek faktörünü belirler."""
    if model not in ("hizli", "agir", "genel"):
        raise UpscaleHatasi(f"Bilinmeyen model: {model}")
    if kaynak_yukseklik >= hedef:
        return 0
    if model == "hizli":
        return 2 if kaynak_yukseklik * 2 >= hedef else 4
    return 4


def _parca_plani(toplam_kare: int, fps: float, parca_sn: int) -> list[tuple[int, int, float]]:
    """Toplam kareleri parça süresine göre dilimler."""
    if toplam_kare <= 0:
        return []
    if fps <= 0.0:
        fps = 30.0
    parca_kare = max(1, round(parca_sn * fps))
    plan: list[tuple[int, int, float]] = []
    kalan = toplam_kare
    indeks = 0
    while kalan > 0:
        kare_sayisi = min(kalan, parca_kare)
        ilk_kare_no = indeks * parca_kare
        baslangic_sn = ilk_kare_no / fps
        plan.append((indeks, kare_sayisi, baslangic_sn))
        kalan -= kare_sayisi
        indeks += 1
    return plan


def _re_komutu(
    re_exe: Path,
    giris: Path,
    cikis: Path,
    model: str,
    olcek: int,
    model_dizini: Path | None,
    ara_format: str,
) -> list[str]:
    """Real-ESRGAN çalıştırma komutunu üretir.

    ÖNEMLİ: exe model adında 'realesr-animevideov3' geçmesini ve -m yolunun
    'models' ile bitmesini şart koşar (genel modeli bu dizinde
    realesr-animevideov3-x4.param/.bin adıyla barındırılır).
    """
    eslesme = {
        "hizli": "realesr-animevideov3",
        "agir": "realesrgan-x4plus",
        "genel": "realesr-animevideov3",
    }
    if model not in eslesme:
        raise UpscaleHatasi(f"Bilinmeyen model: {model}")
    if model == "genel" and model_dizini is None:
        raise UpscaleHatasi("'genel' modeli için model_dizini belirtilmelidir.")

    komut = [
        str(re_exe),
        "-i", str(giris),
        "-o", str(cikis),
        "-n", eslesme[model],
        "-s", str(olcek),
        "-f", ara_format,
    ]
    if model_dizini is not None:
        komut.extend(["-m", str(model_dizini)])
    return komut


def _boyut_al(video: Path) -> tuple[int, int, int]:
    """Videonun genişlik, yükseklik ve toplam kare sayısını ffprobe ile alır."""
    video = Path(video)
    komut = [
        str(yollar.ffprobe()),
        "-v", "error",
        "-select_streams", "v:0",
        "-show_entries", "stream=width,height,nb_frames,duration,r_frame_rate:format=duration",
        "-of", "json",
        str(video),
    ]
    try:
        sonuc = subprocess.run(
            komut,
            check=True,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            creationflags=_penceresiz(),
        )
        veri = json.loads(sonuc.stdout or "{}")
        streams = veri.get("streams", [])
        if not streams:
            raise UpscaleHatasi(f"Videoda video akışı bulunamadı: {video}")
        stream = streams[0]
        genislik = int(stream.get("width", 0))
        yukseklik = int(stream.get("height", 0))
        if genislik <= 0 or yukseklik <= 0:
            raise UpscaleHatasi(f"Geçersiz video çözünürlüğü: {genislik}x{yukseklik}")

        toplam_kare = 0
        nb_frames_str = stream.get("nb_frames")
        if nb_frames_str and nb_frames_str != "N/A":
            try:
                toplam_kare = int(nb_frames_str)
            except ValueError:
                toplam_kare = 0

        if toplam_kare <= 0:
            sure_str = stream.get("duration")
            if not sure_str or sure_str == "N/A":
                sure_str = veri.get("format", {}).get("duration")
            sure = float(sure_str) if sure_str and sure_str != "N/A" else 0.0
            fps = _fps_oran(stream.get("r_frame_rate", ""))
            toplam_kare = round(sure * fps)

        if toplam_kare <= 0:
            raise UpscaleHatasi(f"Toplam kare sayısı tespit edilemedi: {video}")

        return genislik, yukseklik, toplam_kare
    except Exception as e:
        if isinstance(e, UpscaleHatasi):
            raise
        raise UpscaleHatasi(f"Video boyutları alınamadı: {e}") from e


def iyilestir(
    video: Path,
    temiz_wav: Path,
    cikis: Path,
    ayar: Ayar,
    nvenc: bool,
) -> None:
    """Videoya görüntü filtreleri uygular ve temiz ses ile birleştirir."""
    video = Path(video)
    temiz_wav = Path(temiz_wav)
    cikis = Path(cikis)
    cikis.parent.mkdir(parents=True, exist_ok=True)

    if nvenc and _nvenc_destegi_var() and _nvidia_smi_calisiyor():
        vcodec = ["-c:v", "h264_nvenc", "-cq", str(ayar.goruntu.crf + 1), "-preset", "p5"]
    else:
        vcodec = ["-c:v", "libx264", "-crf", str(ayar.goruntu.crf), "-preset", "medium"]

    komut = [
        str(yollar.ffmpeg()),
        "-y",
        "-i", str(video),
        "-i", str(temiz_wav),
        "-map", "0:v:0",
        "-map", "1:a:0",
        "-vf", "hqdn3d=1.5:1.5:6:6,eq=contrast=1.05:brightness=0.02:saturation=1.05,unsharp=5:5:0.5",
    ] + vcodec + [
        "-c:a", "aac",
        "-b:a", "192k",
        "-shortest",
        "-movflags", "+faststart",
        "-pix_fmt", "yuv420p",
        str(cikis),
    ]
    _ffmpeg_calistir(komut)


def upscale(video: Path, cikis: Path, ayar: Ayar) -> None:
    """Videoyu parçalara bölerek Real-ESRGAN ile büyütür ve ses ile birleştirir."""
    gunluk = logging.getLogger(__name__)
    video = Path(video).resolve()
    cikis = Path(cikis).resolve()
    cikis.parent.mkdir(parents=True, exist_ok=True)

    genislik, yukseklik, toplam_kare = _boyut_al(video)
    fps_metni = _fps_al(video)  # '30000/1001' gibi tam oran: yuvarlanırsa uzun videoda ses kayar
    fps = _fps_oran(fps_metni)

    olcek = _olcek_sec(yukseklik, ayar.goruntu.hedef_yukseklik, ayar.goruntu.upscale_model)
    if olcek == 0:
        gunluk.info(
            "Video yüksekliği (%d) hedef yükseklikten (%d) küçük değil, büyütme atlandı.",
            yukseklik,
            ayar.goruntu.hedef_yukseklik,
        )
        shutil.copy2(video, cikis)
        return

    try:
        re_exe = yollar.realesrgan()
    except Exception as e:
        raise UpscaleHatasi(f"Real-ESRGAN aracı bulunamadı: {e}") from e

    model_dizini: Path | None = None
    if ayar.goruntu.upscale_model == "genel":
        try:
            model_dizini = yollar.genel_model_dizini()
        except Exception as e:
            raise UpscaleHatasi(f"Real-ESRGAN genel model dizini bulunamadı: {e}") from e

    plan = _parca_plani(toplam_kare, fps, ayar.goruntu.parca_sn)
    if not plan:
        raise UpscaleHatasi(f"Parça planı oluşturulamadı (toplam kare: {toplam_kare})")

    ad = video.stem
    ara_dizin = cikis.parent / "_ara"
    ara_dizin.mkdir(parents=True, exist_ok=True)
    kareler_ana_dizin = ara_dizin / f"kareler_{ad}"
    concat_listesi = ara_dizin / f"concat_{ad}.txt"
    parca_dosyalari: list[Path] = []
    basarili = False

    try:
        for indeks, kare_sayisi, baslangic_sn in plan:
            gunluk.info("Upscale parça %d/%d", indeks + 1, len(plan))
            parca_mp4 = ara_dizin / f"parca_{indeks:04d}.mp4"
            parca_gecici = ara_dizin / f"parca_{indeks:04d}.tmp.mp4"
            parca_dosyalari.append(parca_mp4)

            if parca_mp4.exists() and parca_mp4.stat().st_size > 0:
                gunluk.info(
                    "Parça %d/%d zaten mevcut, atlanıyor: %s",
                    indeks + 1,
                    len(plan),
                    parca_mp4.name,
                )
                continue

            p_dizin = kareler_ana_dizin / f"p{indeks:04d}"
            giris_dizin = p_dizin / "giris"
            cikis_dizin = p_dizin / "cikis"
            giris_dizin.mkdir(parents=True, exist_ok=True)
            cikis_dizin.mkdir(parents=True, exist_ok=True)

            ara_format = ayar.goruntu.ara_format
            giris_desen = giris_dizin / f"k_%08d.{ara_format}"
            cikis_desen = cikis_dizin / f"k_%08d.{ara_format}"

            komut_kare = [
                str(yollar.ffmpeg()),
                "-y",
                "-ss", f"{baslangic_sn:.6f}",
                "-i", str(video),
                "-frames:v", str(kare_sayisi),
            ]
            if ara_format.lower() in ("jpg", "jpeg"):
                komut_kare.extend(["-q:v", "2"])
            komut_kare.append(str(giris_desen))

            try:
                subprocess.run(
                    komut_kare,
                    check=True,
                    capture_output=True,
                    text=True,
                    encoding="utf-8",
                    errors="replace",
                    creationflags=_penceresiz(),
                )
            except subprocess.CalledProcessError as e:
                ayrinti = "\n".join((e.stderr or "").strip().splitlines()[-5:])
                raise UpscaleHatasi(
                    f"Video kareleri çıkarılamadı (parça {indeks}):\n{ayrinti}"
                ) from e

            giris_kareler = list(giris_dizin.glob(f"*.{ara_format}"))
            if not giris_kareler:
                raise UpscaleHatasi(f"Parça {indeks} için videodan hiç kare çıkarılamadı.")

            komut_re = _re_komutu(
                re_exe=re_exe,
                giris=giris_dizin,
                cikis=cikis_dizin,
                model=ayar.goruntu.upscale_model,
                olcek=olcek,
                model_dizini=model_dizini,
                ara_format=ara_format,
            )
            try:
                subprocess.run(
                    komut_re,
                    check=True,
                    capture_output=True,
                    text=True,
                    encoding="utf-8",
                    errors="replace",
                    cwd=re_exe.parent,
                    creationflags=_penceresiz(),
                )
            except subprocess.CalledProcessError as e:
                ayrinti = "\n".join((e.stderr or e.stdout or "").strip().splitlines()[-5:])
                raise UpscaleHatasi(
                    f"Real-ESRGAN çalıştırılamadı (Vulkan/GPU hatası olabilir):\n{ayrinti}"
                ) from e
            except Exception as e:
                raise UpscaleHatasi(f"Real-ESRGAN yürütülemedi: {e}") from e

            cikis_kareler = list(cikis_dizin.glob(f"*.{ara_format}"))
            if not cikis_kareler:
                raise UpscaleHatasi(f"Real-ESRGAN büyütülmüş kareler üretmedi (parça {indeks}).")

            if ayar.goruntu.nvenc and _nvenc_destegi_var() and _nvidia_smi_calisiyor():
                vcodec = [
                    "-c:v", "h264_nvenc",
                    "-cq", str(ayar.goruntu.crf + 1),
                    "-preset", "p5",
                ]
            else:
                vcodec = [
                    "-c:v", "libx264",
                    "-crf", str(ayar.goruntu.crf),
                    "-preset", "medium",
                ]

            komut_parca = [
                str(yollar.ffmpeg()),
                "-y",
                "-framerate", fps_metni,
                "-i", str(cikis_desen),
                "-vf", f"scale=-2:{ayar.goruntu.hedef_yukseklik}:flags=lanczos",
            ] + vcodec + [
                "-pix_fmt", "yuv420p",
                str(parca_gecici),
            ]

            try:
                subprocess.run(
                    komut_parca,
                    check=True,
                    capture_output=True,
                    text=True,
                    encoding="utf-8",
                    errors="replace",
                    creationflags=_penceresiz(),
                )
            except subprocess.CalledProcessError as e:
                ayrinti = "\n".join((e.stderr or "").strip().splitlines()[-5:])
                raise UpscaleHatasi(
                    f"Parça video kodlanamadı (parça {indeks}):\n{ayrinti}"
                ) from e

            parca_gecici.replace(parca_mp4)  # yarım kalan parça "tamam" sayılmasın: kodlama bitince adlandırılır
            shutil.rmtree(p_dizin, ignore_errors=True)

        satirlar = [f"file '{p.resolve().as_posix()}'\n" for p in parca_dosyalari]
        concat_listesi.write_text("".join(satirlar), encoding="utf-8")

        komut_birlestir = [
            str(yollar.ffmpeg()),
            "-y",
            "-f", "concat",
            "-safe", "0",
            "-i", str(concat_listesi),
            "-i", str(video),
            "-map", "0:v:0",
            "-map", "1:a:0?",
            "-c:v", "copy",
            "-c:a", "copy",
            "-shortest",
            "-movflags", "+faststart",
            str(cikis),
        ]

        try:
            subprocess.run(
                komut_birlestir,
                check=True,
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                creationflags=_penceresiz(),
            )
        except subprocess.CalledProcessError as e:
            ayrinti = "\n".join((e.stderr or "").strip().splitlines()[-5:])
            raise UpscaleHatasi(f"Büyütülmüş parçalar birleştirilemedi:\n{ayrinti}") from e

        basarili = True
    finally:
        if kareler_ana_dizin.exists():
            shutil.rmtree(kareler_ana_dizin, ignore_errors=True)
        if concat_listesi.exists():
            concat_listesi.unlink(missing_ok=True)
        if basarili:
            for p_mp4 in parca_dosyalari:
                if p_mp4.exists():
                    p_mp4.unlink(missing_ok=True)
