"""Sürüm 2: eski biçimler, araç yedekleri ve saklanan modern kopyalar."""
from contextlib import nullcontext
from email.message import EmailMessage
import io
import json
import os
from pathlib import Path
import tempfile
from unittest.mock import Mock, patch
import zipfile

import pymupdf
from PIL import Image

from belge_destek import GeciciTest, SahteHavuz, gorsel
from videohat import yollar
from videohat.__main__ import parser_olustur
from videohat.belge import ESKI_OFIS_HEDEFLERI, UZANTILAR
from videohat.belge.cikar import cikar, metin_oku, _chm
from videohat.belge.hat import isle_belge, disa_yaz
from videohat.belge.model import BelgeSonucu, OcrSatir
from videohat.belge.ocr import OcrHavuzu
from videohat.belge.ofis import eski_cevir
from videohat.belge.surec import calistir
from videohat.dizin.dizinle import calistir as dizinle
from videohat.dizin.tarayici import tur_bul
from videohat.hatalar import IsHatasi, OrtamHatasi

METIN = "Eski arsiv: Radar kontrol raporu ve sistem guvenlik olcumu."
TURKCE = "ŞİĞÜÖÇ şığüöç"
OFIS_GRUPLARI = {
    "docx": ".doc .dot .wpd .wps .wri .lwp .sam .sxw .stw .sdw .mcw .602 .hwp .abw .zabw .cwk",
    "xlsx": ".xls .xlt .xlw .xlsb .wk1 .wk3 .wk4 .123 .wq1 .wb2 .sxc .stc .sdc .dbf .dif .slk",
    "pptx": ".ppt .pps .pot .sxi .sti .sdd .key",
    "pdf": ".vsd .vdx .pub .sxd .std .sda .cdr .wpg .cgm",
}
YEREL = ".xps .oxps .epub .mobi .fb2 .cbz .svg"
GORUNTU = ".pcx .tga .dib .jpe .jfif .jp2 .j2k .jpx .psd .pnm .pbm .pgm .ppm .djvu .djv"
METIN_UZANTILARI = ".txt .asc .nfo .lst .prn .dos"


def modern_yaz(yol):
    if yol.suffix == ".docx":
        from docx import Document
        belge = Document()
        belge.add_paragraph(METIN)
        belge.save(yol)
    elif yol.suffix == ".xlsx":
        from openpyxl import Workbook
        belge = Workbook()
        belge.active["A1"] = METIN
        belge.save(yol)
        belge.close()
    elif yol.suffix == ".pptx":
        from pptx import Presentation
        belge = Presentation()
        slayt = belge.slides.add_slide(belge.slide_layouts[0])
        slayt.shapes.title.text = METIN
        belge.save(yol)
    else:
        with pymupdf.open() as belge:
            belge.new_page().insert_text((20, 40), METIN)
            belge.save(yol)
    return yol


class EskiBicimTest(GeciciTest):
    def oku(self, yol, havuz=None):
        return cikar(yol, self.ayar, havuz, self.kok / (yol.name + "_ara"))

    def sahte_donusum(self, komut, sn, ad):
        self.assertEqual(sn, 120)
        self.assertEqual(ad, "LibreOffice")
        self.assertIn("--headless", komut)
        self.assertIn("--norestore", komut)
        self.assertTrue(any(str(s).startswith("-env:UserInstallation=file:///") for s in komut))
        uzanti = komut[komut.index("--convert-to") + 1]
        hedef = Path(komut[komut.index("--outdir") + 1]) / (Path(komut[-1]).stem + "." + uzanti)
        modern_yaz(hedef)
        return b""

    def test_tum_ofis_gruplari_donusum_ve_dagitim(self):
        beklenen = {uz: hedef for hedef, uzantilar in OFIS_GRUPLARI.items() for uz in uzantilar.split()}
        self.assertEqual(ESKI_OFIS_HEDEFLERI, beklenen)
        with patch("videohat.yollar.libreoffice", return_value=Path("soffice.exe")), \
                patch("videohat.belge.ofis.calistir", side_effect=self.sahte_donusum):
            for uz, hedef in beklenen.items():
                with self.subTest(uz=uz):
                    yol = self.kok / ("eski" + uz)
                    yol.write_bytes(uz.encode())
                    sonuc = self.oku(yol)
                    self.assertEqual(sonuc.durum, "tamam")
                    self.assertEqual(sonuc.bicim, uz[1:])
                    self.assertEqual(sonuc.duzenlenebilir.suffix, "." + hedef)
                    self.assertIn(METIN, "\n".join(s.metin for s in sonuc.sayfalar))

    def test_araclar_yok_turkce_neden(self):
        with patch("videohat.yollar.libreoffice", return_value=None), \
                patch("videohat.yollar.artcraft_cli", return_value=None) as cli:
            for uz in (".doc", ".xls", ".wpd", ".xlsb", ".key", ".pub"):
                with self.subTest(uz=uz):
                    cli.reset_mock()
                    sonuc = self.oku(self.kok / ("eski" + uz))
                    self.assertEqual(sonuc.durum, "desteklenmiyor")
                    self.assertIn("LibreOffice", sonuc.hata)
                    self.assertIn("bulunamadı", sonuc.hata)
                    self.assertIsNone(sonuc.duzenlenebilir)
                    if uz in (".doc", ".xls"):
                        ad = "wordcraft" if uz == ".doc" else "gridcraft"
                        self.assertIn(ad + "-cli", sonuc.hata)
                        cli.assert_called_once_with(ad)
                    else:
                        cli.assert_not_called()

    def test_artcraft_cli_yedekleri(self):
        for uz, ad in ((".doc", "wordcraft"), (".xls", "gridcraft")):
            with self.subTest(uz=uz):
                yol = self.kok / ("eski" + uz)
                yol.write_bytes(b"eski")
                exe = self.kok / (ad + "-cli.exe")

                def cevir(komut, sn, arac):
                    self.assertEqual(komut[:3], [exe, "convert", yol])
                    self.assertEqual((sn, arac), (120, ad + "-cli"))
                    modern_yaz(komut[3])

                with patch("videohat.yollar.libreoffice", return_value=None), \
                        patch("videohat.yollar.artcraft_cli", return_value=exe) as bul, \
                        patch("videohat.belge.ofis.calistir", side_effect=cevir):
                    sonuc = self.oku(yol)
                bul.assert_called_once_with(ad)
                self.assertIn(METIN, sonuc.sayfalar[0].metin)

    def test_donusum_eski_ciktiyi_basari_saymaz(self):
        ara = self.kok / "ara"
        ara.mkdir()
        modern_yaz(ara / "eski.docx")
        with patch("videohat.yollar.libreoffice", return_value=Path("soffice.exe")), \
                patch("videohat.belge.ofis.calistir", return_value=b""):
            with self.assertRaisesRegex(IsHatasi, "LibreOffice çıktı oluşturmadı"):
                eski_cevir(self.kok / "eski.doc", ara)

    def test_duzenlenebilir_depo_json_ve_onbellek(self):
        for uz in (".doc", ".xls", ".ppt", ".vsd"):
            with self.subTest(uz=uz):
                yol = self.kok / ("eski" + uz)
                yol.write_bytes(uz.encode())
                with patch("videohat.yollar.libreoffice", return_value=Path("soffice.exe")), \
                        patch("videohat.belge.ofis.calistir", side_effect=self.sahte_donusum):
                    kayit = isle_belge(yol, self.kok / "depo", self.ayar, SahteHavuz())
                ad = "duzenlenebilir." + ESKI_OFIS_HEDEFLERI[uz]
                self.assertEqual(kayit.veri["duzenlenebilir"], ad)
                self.assertTrue((kayit.hedef / ad).is_file())
                self.assertFalse((kayit.hedef / "_ara").exists())
                veri = json.loads((kayit.hedef / "belge.json").read_text(encoding="utf-8"))
                self.assertEqual(veri["duzenlenebilir"], ad)
                with patch("videohat.belge.hat.cikar", side_effect=AssertionError("Tekrar çıkarıldı")):
                    ikinci = isle_belge(yol, self.kok / "depo", self.ayar)
                self.assertEqual(ikinci.veri["duzenlenebilir"], ad)

    def test_duzenlenebilir_disa_yaz_ve_donusumsuz_null(self):
        yol = self.kok / "eski.doc"
        yol.write_bytes(b"doc")
        with patch("videohat.yollar.libreoffice", return_value=Path("soffice.exe")), \
                patch("videohat.belge.ofis.calistir", side_effect=self.sahte_donusum):
            kayit = disa_yaz(yol, self.kok / "cikti", self.ayar, SahteHavuz())
        self.assertTrue((kayit.hedef / "duzenlenebilir.docx").is_file())
        self.assertFalse((kayit.hedef / "_ara").exists())
        txt = self.kok / "metin.txt"
        txt.write_text(METIN, encoding="utf-8")
        kayit = isle_belge(txt, self.kok / "depo", self.ayar)
        self.assertIsNone(kayit.veri["duzenlenebilir"])
        self.assertIsNone(isle_belge(txt, self.kok / "depo", self.ayar).veri["duzenlenebilir"])

    def test_epub_metin(self):
        yol = self.kok / "kitap.epub"
        with zipfile.ZipFile(yol, "w") as z:
            z.writestr("mimetype", "application/epub+zip")
            z.writestr("META-INF/container.xml", '<?xml version="1.0"?><container version="1.0" '
                       'xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles>'
                       '<rootfile full-path="kitap.opf" media-type="application/oebps-package+xml"/>'
                       '</rootfiles></container>')
            z.writestr("kitap.opf", '<package version="2.0" xmlns="http://www.idpf.org/2007/opf" '
                       'unique-identifier="id"><metadata xmlns:dc="http://purl.org/dc/elements/1.1/">'
                       '<dc:identifier id="id">test</dc:identifier><dc:title>Arşiv</dc:title>'
                       '<dc:language>tr</dc:language></metadata><manifest><item id="s1" '
                       'href="sayfa.xhtml" media-type="application/xhtml+xml"/></manifest>'
                       '<spine><itemref idref="s1"/></spine></package>')
            z.writestr("sayfa.xhtml", '<html xmlns="http://www.w3.org/1999/xhtml"><head><title>Arşiv</title>'
                       f'</head><body><p>{METIN}</p></body></html>')
        sonuc = self.oku(yol)
        self.assertEqual(sonuc.sayfalar[0].kaynak, "metin")
        self.assertIn(METIN, " ".join(s.metin.replace("\n", " ") for s in sonuc.sayfalar))

    def test_yerel_dagitim_ve_svg_ocr(self):
        with patch("videohat.belge.cikar._pdf") as oku:
            for uz in YEREL.split():
                self.oku(self.kok / ("yerel" + uz))
            self.assertEqual(oku.call_count, len(YEREL.split()))
        yol = self.kok / "cizim.svg"
        yol.write_text('<svg xmlns="http://www.w3.org/2000/svg" width="200" height="100">'
                       '<rect width="180" height="80" fill="black"/></svg>', encoding="utf-8")
        motor = Mock()
        motor.oku.return_value = ([OcrSatir("çizim metni", .95, (5, 5, 150, 30))], .95)
        with patch("videohat.belge.ocr.OcrMotoru", return_value=motor), OcrHavuzu(self.ayar) as havuz:
            sonuc = self.oku(yol, havuz)
        self.assertEqual(sonuc.sayfalar[0].metin, "çizim metni")
        self.assertEqual(sonuc.sayfalar[0].kaynak, "ocr")
        self.assertTrue(motor.oku.called)

    def test_cbz_sayfalari(self):
        yol = self.kok / "kitap.cbz"
        with io.BytesIO() as buf:
            Image.new("RGB", (100, 50), "white").save(buf, format="PNG")
            with zipfile.ZipFile(yol, "w") as z:
                z.writestr("01.png", buf.getvalue())
                z.writestr("02.png", buf.getvalue())
        sonuc = self.oku(yol, SahteHavuz())
        self.assertEqual([s.no for s in sonuc.sayfalar], [1, 2])
        self.assertTrue(all(s.kaynak == "ocr" for s in sonuc.sayfalar))

    def test_eski_goruntuler_pillow(self):
        for uz, bicim in (("pcx", "PCX"), ("tga", "TGA"), ("dib", "DIB"), ("jpe", "JPEG"),
                          ("jfif", "JPEG"), ("pnm", "PPM"), ("pbm", "PPM"), ("pgm", "PPM"), ("ppm", "PPM")):
            with self.subTest(uz=uz):
                yol = self.kok / ("resim." + uz)
                kip = "1" if uz == "pbm" else "L" if uz == "pgm" else "RGB"
                Image.new(kip, (100, 50), "white").save(yol, format=bicim)
                motor = Mock()
                motor.oku.return_value = ([OcrSatir("eski resim", .95, (5, 5, 80, 25))], .95)
                with patch("videohat.belge.ocr.OcrMotoru", return_value=motor), OcrHavuzu(self.ayar) as havuz:
                    sonuc = self.oku(yol, havuz)
                self.assertEqual(sonuc.sayfalar[0].metin, "eski resim")
                self.assertEqual(sonuc.sayfalar[0].gorsel_boyut, (100, 50))

    def test_pillow_acamazsa_pymupdf_ocr_ve_pdf(self):
        yol = self.kok / "resim.jpx"
        Image.new("RGB", (700, 100), "white").save(yol, format="JPEG2000")
        motor = Mock()
        motor.oku.return_value = ([OcrSatir("guvenlik", .95, (20, 30, 300, 70))], .95)
        with patch("PIL.Image.open", side_effect=OSError("Pillow açamadı")), \
                patch("videohat.belge.ocr.OcrMotoru", return_value=motor), OcrHavuzu(self.ayar) as havuz:
            kayit = isle_belge(yol, self.kok / "depo", self.ayar, havuz)
        self.assertEqual(kayit.veri["sayfalar"][0]["metin"], "guvenlik")
        with pymupdf.open(kayit.hedef / "aranabilir.pdf") as belge:
            self.assertIn("guvenlik", belge[0].get_text())

    def test_djvu_metin_ve_ikinci_sayfada_ocr(self):
        yol = self.kok / "eski.djvu"

        def calis(komut, sn, ad):
            self.assertEqual(sn, 120)
            if ad == "djvused":
                self.assertEqual(komut[1:], [yol, "-e", "n"])
                return b"2\n"
            if ad == "djvutxt":
                return b"Metin katmani\f"
            self.assertEqual(komut[1:4], ["-format=tiff", "-page=2", yol])
            Image.new("RGB", (100, 50), "white").save(komut[-1])
            return b""

        motor = Mock()
        motor.oku.return_value = ([OcrSatir("ikinci sayfa", .95, (5, 5, 80, 25))], .95)
        with patch("videohat.yollar.djvulibre", side_effect=lambda ad: Path(ad + ".exe")), \
                patch("videohat.belge.cikar.calistir", side_effect=calis), \
                patch("videohat.belge.ocr.OcrMotoru", return_value=motor), OcrHavuzu(self.ayar) as havuz:
            sonuc = self.oku(yol, havuz)
        self.assertEqual([(s.no, s.kaynak, s.metin) for s in sonuc.sayfalar],
                         [(1, "metin", "Metin katmani"), (2, "ocr", "ikinci sayfa")])
        self.assertEqual(sonuc.sayfalar[1].gorsel_boyut, (100, 50))

    def test_djvu_arac_yok(self):
        with patch("videohat.yollar.djvulibre", return_value=None):
            sonuc = self.oku(self.kok / "eski.djv")
        self.assertEqual(sonuc.durum, "desteklenmiyor")
        self.assertIn("DjVuLibre", sonuc.hata)
        self.assertIn("djvutxt", sonuc.hata)
        with patch("videohat.yollar.djvulibre", side_effect=lambda ad: None if ad == "ddjvu" else Path(ad)), \
                patch("videohat.belge.cikar.calistir", side_effect=[b"", b"1"]):  # djvutxt boş, djvused 1 sayfa
            sonuc = self.oku(self.kok / "bos.djvu")
        self.assertEqual(sonuc.durum, "desteklenmiyor")
        self.assertIn("ddjvu", sonuc.hata)

    def test_mht_html_onceligi_ve_mhtml_duz_metin(self):
        ileti = EmailMessage()
        ileti.set_content("Seçilmemeli")
        ileti.add_alternative('<html><style>gizli stil</style><p>Şığ ölçümü</p>'
                              '<script>gizli kod</script></html>', subtype="html", charset="cp1254")
        yol = self.kok / "arsiv.mht"
        yol.write_bytes(ileti.as_bytes())
        sonuc = self.oku(yol)
        self.assertEqual(sonuc.sayfalar[0].metin, "Şığ ölçümü")
        ileti = EmailMessage()
        ileti.set_content(TURKCE)
        yol = self.kok / "arsiv.mhtml"
        yol.write_bytes(ileti.as_bytes())
        self.assertEqual(self.oku(yol).sayfalar[0].metin, TURKCE)

    def test_chm_bolum_sirasi_ve_arac_yok(self):
        yol = self.kok / "yardim.chm"
        with patch("videohat.yollar.yedizip", return_value=None):
            sonuc = self.oku(yol)
        self.assertEqual(sonuc.durum, "desteklenmiyor")
        self.assertIn("7-Zip", sonuc.hata)

        def ac(komut, sn, ad):
            self.assertEqual(komut[1], "x")
            self.assertEqual(komut[-1], yol)
            self.assertEqual((sn, ad), (120, "7-Zip"))
            self.assertTrue(all(arg in komut for arg in ("-y", "-snl-", "-snh-", "-p", "--")))
            tmp_arg = next(arg for arg in komut if str(arg).startswith("-o"))
            ara = Path(str(tmp_arg)[2:])
            (ara / "b.html").write_text("<p>İkinci</p>", encoding="utf-8")
            (ara / "a.HTM").write_text("<p>Birinci</p><script>gizli</script>", encoding="utf-8")
            (ara / "resim.png").write_bytes(b"gorsel")

        with patch("videohat.yollar.yedizip", return_value=Path("7z.exe")), \
                patch("videohat.belge.cikar.calistir", side_effect=ac):
            sonuc = self.oku(yol)
        self.assertEqual([(s.etiket, s.metin) for s in sonuc.sayfalar],
                         [("Bölüm 1", "Birinci"), ("Bölüm 2", "İkinci")])

    def test_eski_kodlamalar_bom_ve_cp857(self):
        for kodlama in ("utf-8", "utf-8-sig", "utf-16", "utf-32", "cp1254", "iso-8859-9", "cp857"):
            with self.subTest(kodlama=kodlama):
                self.assertEqual(metin_oku(TURKCE.encode(kodlama)), TURKCE)
        kutulu = "╔═══╗\n║ DOS arsivi ║\n╚═══╝"
        self.assertEqual(metin_oku(kutulu.encode("cp857")), kutulu)
        for uz in METIN_UZANTILARI.split():
            with self.subTest(uz=uz):
                yol = self.kok / ("eski" + uz)
                yol.write_bytes(TURKCE.encode("cp857"))
                self.assertEqual(self.oku(yol).sayfalar[0].metin, TURKCE)

    def test_rtf_atlanan_unicode_ilk_harfi_yutmaz(self):
        yol = self.kok / "eski.rtf"
        yol.write_bytes(br"{\rtf1{\fonttbl{\f0 Ad\u1234?;}}Eski arsiv}")
        self.assertEqual(self.oku(yol).sayfalar[0].metin, "Eski arsiv")

    def test_tum_yeni_turler(self):
        belgeler = " ".join(OFIS_GRUPLARI.values()) + " " + YEREL + " .mht .mhtml .chm " + METIN_UZANTILARI
        for uz in belgeler.split() + GORUNTU.split():
            with self.subTest(uz=uz):
                self.assertIn(uz, UZANTILAR)
                self.assertEqual(tur_bul(Path("arsiv") / ("ESKI" + uz.upper())),
                                 "goruntu" if uz in GORUNTU.split() else "belge")

    def test_arac_yolu_sirasi(self):
        paket, pf, x86 = (self.kok / ad for ad in ("paket", "pf", "x86"))
        adaylar = [paket / "araclar/libreoffice/program/soffice.exe", pf / "LibreOffice/program/soffice.exe",
                   x86 / "LibreOffice/program/soffice.exe"]
        with patch("videohat.yollar.kok", return_value=paket), \
                patch.dict(os.environ, {"ProgramFiles": str(pf), "ProgramFiles(x86)": str(x86)}):
            for bul, yollar_ in (
                (yollar.libreoffice, adaylar),
                (lambda: yollar.artcraft_cli("wordcraft"),
                 [paket / "artcraft/wordcraft/v1/bin/wordcraft-cli.exe", pf / "WordCraft/wordcraft-cli.exe"]),
                (lambda: yollar.artcraft_cli("gridcraft"),
                 [paket / "artcraft/gridcraft/v1/gridcraft-cli.exe", pf / "GridCraft/gridcraft-cli.exe"]),
                (lambda: yollar.djvulibre("djvutxt"),
                 [paket / "araclar/djvulibre/djvutxt.exe", x86 / "DjVuLibre/djvutxt.exe", pf / "DjVuLibre/djvutxt.exe"]),
                (yollar.yedizip, [paket / "araclar/7z/7z.exe", pf / "7-Zip/7z.exe"]),
            ):
                for p in yollar_:
                    p.parent.mkdir(parents=True, exist_ok=True)
                    p.touch()
                for p in yollar_:
                    self.assertEqual(bul(), p)
                    p.unlink()
                self.assertIsNone(bul())

    def test_veri_firini_ve_varsayilan_depo(self):
        self.assertIn("Veri Fırını", parser_olustur().format_help())
        self.assertIn("_veridedektifi", self.ayar.dizin.haric)
        self.assertIn("_bulgu", self.ayar.dizin.haric)
        with patch("videohat.dizin.dizinle.vt.yazar_kilidi", return_value=nullcontext()), \
                patch("videohat.dizin.dizinle._dongu", return_value=0) as dongu:
            self.assertEqual(dizinle(self.ayar, kokler=[self.kok], bir_kez=True), 0)
        self.assertEqual(dongu.call_args.args[2], self.kok / "_veridedektifi")

    def test_cp1254_noktalama_sapka_cp857_cakismasi(self):
        metin1 = '“Radar kâr raporu: maliyet 150 € » onaylandı”'
        self.assertEqual(metin_oku(metin1.encode("cp1254")), metin1)
        metin2 = '“Tüm hakları saklıdır” kâr marjı: 50 € » net'
        self.assertEqual(metin_oku(metin2.encode("cp1254")), metin2)

    def test_chm_symlink_ve_zip_slip_engeli(self):
        yol = self.kok / "zararli.chm"
        ara = self.kok / "chm_ara"
        ara.mkdir()
        sonuc = BelgeSonucu("chm")
        dis_dosya = self.kok / "disari_gizli.html"
        dis_dosya.write_text("<p>Gizli Veri</p>", encoding="utf-8")

        def sahte_ac(komut, sn, ad):
            self.assertIn("-snl-", komut)
            self.assertIn("-snh-", komut)
            self.assertIn("-p", komut)
            self.assertIn("--", komut)
            tmp_arg = next(arg for arg in komut if str(arg).startswith("-o"))
            tmp_dizin = Path(str(tmp_arg)[2:])
            (tmp_dizin / "guvenli.html").write_text("<p>Güvenli İçerik</p>", encoding="utf-8")
            (tmp_dizin / "zararli_link.html").write_text("<p>Symlink İçerik</p>", encoding="utf-8")
            (tmp_dizin / "slip.html").write_text("<p>Zipslip İçerik</p>", encoding="utf-8")

        gercek_is_symlink = Path.is_symlink
        gercek_resolve = Path.resolve

        def sahte_is_symlink(p):
            if "link" in p.name:
                return True
            return gercek_is_symlink(p)

        def sahte_resolve(p, *args, **kwargs):
            if "slip" in p.name:
                return dis_dosya.resolve()
            return gercek_resolve(p, *args, **kwargs)

        with patch("videohat.yollar.yedizip", return_value=Path("7z.exe")), \
                patch("videohat.belge.cikar.calistir", side_effect=sahte_ac), \
                patch.object(Path, "is_symlink", autospec=True, side_effect=sahte_is_symlink), \
                patch.object(Path, "resolve", autospec=True, side_effect=sahte_resolve):
            _chm(yol, self.ayar, None, ara, sonuc)

        self.assertEqual(len(sonuc.sayfalar), 1)
        self.assertEqual(sonuc.sayfalar[0].metin, "Güvenli İçerik")
        metinler = [s.metin for s in sonuc.sayfalar]
        self.assertNotIn("Symlink İçerik", metinler)
        self.assertNotIn("Zipslip İçerik", metinler)
        self.assertNotIn("Gizli Veri", metinler)

    def test_libreoffice_basarisiz_artcraft_yedegine_gecis(self):
        yol = self.kok / "eski.doc"
        yol.write_bytes(b"doc")
        lo_exe = Path("soffice.exe")
        cli_exe = self.kok / "wordcraft-cli.exe"

        cagrilanlar = []
        def sahte_calistir(komut, sn, ad):
            cagrilanlar.append(ad)
            if ad == "LibreOffice":
                raise IsHatasi("LibreOffice çöktü")
            elif ad == "wordcraft-cli":
                modern_yaz(komut[3])
                return b""

        with patch("videohat.yollar.libreoffice", return_value=lo_exe), \
                patch("videohat.yollar.artcraft_cli", return_value=cli_exe), \
                patch("videohat.belge.ofis.calistir", side_effect=sahte_calistir):
            sonuc = self.oku(yol)

        self.assertEqual(sonuc.durum, "tamam")
        self.assertIn("LibreOffice", cagrilanlar)
        self.assertIn("wordcraft-cli", cagrilanlar)
        self.assertIn(METIN, sonuc.sayfalar[0].metin)

    def test_artcraft_cli_coklu_surum_en_yuksek(self):
        paket = self.kok / "paket"
        lap = self.kok / "localapp"
        v12 = paket / "artcraft" / "wordcraft" / "1.2.0" / "wordcraft-cli.exe"
        v110 = paket / "artcraft" / "wordcraft" / "1.10.0" / "wordcraft-cli.exe"
        v20 = paket / "artcraft" / "wordcraft" / "v2.0.0" / "wordcraft-cli.exe"
        lap_exe = lap / "Programs" / "WordCraft" / "wordcraft-cli.exe"

        for p in (v12, v110, v20, lap_exe):
            p.parent.mkdir(parents=True, exist_ok=True)
            p.touch()

        with patch("videohat.yollar.kok", return_value=paket), \
                patch.dict(os.environ, {"LOCALAPPDATA": str(lap), "ProgramFiles": str(self.kok / "pf")}):
            self.assertEqual(yollar.artcraft_cli("wordcraft"), v20)
            v20.unlink()
            self.assertEqual(yollar.artcraft_cli("wordcraft"), v110)
            v110.unlink()
            self.assertEqual(yollar.artcraft_cli("wordcraft"), v12)
            v12.unlink()
            self.assertEqual(yollar.artcraft_cli("wordcraft"), lap_exe)


class GercekEskiAracTest(GeciciTest):
    def arac_calistir(self, komut, sn, ad):
        try:
            return calistir(komut, sn, ad)
        except PermissionError as e:
            self.skipTest(f"{ad}: sandbox çalıştırmaya izin vermedi: {e}")

    def libreoffice_dongusu(self, eski, modern):
        exe = yollar.libreoffice()
        if exe is None:
            self.skipTest("LibreOffice bulunamadı")
        kaynak = modern_yaz(self.kok / ("deneme." + modern))
        ara = self.kok / "eski"
        ara.mkdir()
        with tempfile.TemporaryDirectory(prefix="lo_profil_test_", dir=self.kok) as profil:
            self.arac_calistir([exe, f"-env:UserInstallation={Path(profil).as_uri()}", "--headless", "--norestore",
                               "--convert-to", eski, "--outdir", ara, kaynak], 120, "LibreOffice")
        yol = ara / ("deneme." + eski)
        self.assertTrue(yol.is_file(), f"LibreOffice .{eski} üretmedi")
        with patch("videohat.belge.ofis.calistir", side_effect=self.arac_calistir):
            sonuc = cikar(yol, self.ayar, None, self.kok / "okuma")
            self.assertIn(METIN, "\n".join(s.metin for s in sonuc.sayfalar))
            if eski != "rtf":
                kayit = isle_belge(yol, self.kok / "depo", self.ayar, SahteHavuz())
                self.assertEqual(kayit.veri["duzenlenebilir"], "duzenlenebilir." + modern)
                self.assertTrue((kayit.hedef / ("duzenlenebilir." + modern)).is_file())

    def test_gercek_libreoffice_doc(self):
        self.libreoffice_dongusu("doc", "docx")

    def test_gercek_libreoffice_xls(self):
        self.libreoffice_dongusu("xls", "xlsx")

    def test_gercek_libreoffice_ppt(self):
        self.libreoffice_dongusu("ppt", "pptx")

    def test_gercek_libreoffice_rtf(self):
        self.libreoffice_dongusu("rtf", "docx")

    def test_gercek_djvu_bos_metin_ocr(self):
        araclar = {ad: yollar.djvulibre(ad) for ad in ("cjb2", "djvused", "djvutxt", "ddjvu")}
        if not all(araclar.values()):
            self.skipTest("DjVuLibre araçları bulunamadı")
        try:
            for ad in ("PP-OCRv6_det_small.onnx", "PP-OCRv6_rec_small.onnx", "ch_ppocr_mobile_v2.0_cls_mobile.onnx"):
                yollar.ocr_model(ad)
        except OrtamHatasi as e:
            self.skipTest(str(e))
        pbm, yol = self.kok / "sayfa.pbm", self.kok / "sayfa.djvu"
        Image.fromarray(gorsel()).convert("1").save(pbm)
        self.arac_calistir([araclar["cjb2"], "-dpi", "150", pbm, yol], 120, "cjb2")
        self.assertFalse(self.arac_calistir([araclar["djvutxt"], "--page=1", yol], 120, "djvutxt").strip())
        with patch("videohat.belge.cikar.calistir", side_effect=self.arac_calistir), OcrHavuzu(self.ayar) as havuz:
            sonuc = cikar(yol, self.ayar, havuz, self.kok / "ara")
        self.assertEqual(sonuc.sayfalar[0].kaynak, "ocr")
        for sozcuk in ("frekansı", "ölçümü", "güvenlik"):
            self.assertIn(sozcuk, sonuc.sayfalar[0].metin)

    def test_gercek_7zip_html_cikarma(self):
        exe = yollar.yedizip()
        if exe is None:
            self.skipTest("7-Zip bulunamadı")
        yol = self.kok / "yardim.zip"  # CHM derleyicisi yok; gerçek açma komutu ZIP ile sınanır.
        with zipfile.ZipFile(yol, "w") as z:
            z.writestr("b.html", "<p>İkinci</p>")
            z.writestr("a.htm", "<p>Birinci</p>")
        ara = self.kok / "ara"
        ara.mkdir()
        sonuc = BelgeSonucu("chm")
        with patch("videohat.belge.cikar.calistir", side_effect=self.arac_calistir):
            _chm(yol, self.ayar, None, ara, sonuc)
        self.assertEqual([s.metin for s in sonuc.sayfalar], ["Birinci", "İkinci"])
