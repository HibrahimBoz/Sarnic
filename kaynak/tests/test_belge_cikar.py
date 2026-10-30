import io
from email.message import EmailMessage
from pathlib import Path
import sys
from types import SimpleNamespace
from unittest.mock import patch
import xml.etree.ElementTree as ET
import zipfile
import zlib

from belge_destek import GeciciTest, SahteHavuz, pdf_yaz
from videohat.belge.cikar import cikar
from videohat.belge.ofis import eski_cevir, docx_bolumleri, odf_parcalari
from videohat.hatalar import IsHatasi


class CikarmaTest(GeciciTest):
    def oku(self, yol, havuz=None):
        return cikar(yol, self.ayar, havuz, self.kok / (yol.name + "_ara"))

    def test_docx_sayfa_sonu_tablo_ust_alt_bilgi(self):
        from docx import Document
        d = Document()
        d.sections[0].header.paragraphs[0].text = "Üst bilgi"
        d.sections[0].footer.paragraphs[0].text = "Alt bilgi"
        d.add_paragraph("Birinci ﬁkir")
        d.add_page_break()
        d.add_paragraph("İkinci bölüm")
        t = d.add_table(rows=1, cols=2)
        t.cell(0, 0).text, t.cell(0, 1).text = "Ölçüm", "Sonuç"
        yol = self.kok / "belge.docx"
        d.save(yol)
        s = self.oku(yol).sayfalar
        self.assertEqual(len(s), 2)
        self.assertIn("Birinci fikir", s[0].metin)
        self.assertIn("Üst bilgi", s[0].metin)
        self.assertIn("Alt bilgi", s[0].metin)
        self.assertNotIn("Üst bilgi", s[1].metin)
        self.assertIn("Ölçüm\tSonuç", s[1].metin)

    def test_docx_rendered_break_bos_sayfa_ve_3000(self):
        from docx import Document
        from docx.oxml import OxmlElement
        d = Document()
        p = d.add_paragraph("İlk")
        p.add_run()._r.append(OxmlElement("w:lastRenderedPageBreak"))
        p.add_run("Son")
        d.add_page_break()
        d.add_page_break()
        d.add_paragraph("x" * 2000)
        d.add_paragraph("y" * 2000)
        yol = self.kok / "bol.docx"
        d.save(yol)
        s = self.oku(yol).sayfalar
        self.assertEqual([p.metin for p in s[:3]], ["İlk", "Son", ""])
        self.assertEqual(len(s), 4)  # Açık sayfa sonları varsa 3000 karakterden ayrıca bölünmez.
        self.assertEqual(s[0].etiket, "Sayfa 1")
        d = Document()
        d.add_paragraph("x" * 2000)
        d.add_paragraph("y" * 2000)
        yol = self.kok / "yaklasik.docx"
        d.save(yol)
        s = self.oku(yol).sayfalar
        self.assertEqual([p.etiket for p in s], ["Bölüm 1", "Bölüm 2"])

    def test_docx_sectpr_ve_bos_tablo_gorsel(self):
        # 8. bulgu: w:sectPr boş bölüm üretmez; 0 satırlı tabloda görsel IndexError vermez
        from docx import Document
        from docx.oxml.ns import qn
        from docx.oxml import OxmlElement
        d = Document()
        d.add_paragraph("a" * 3100)  # 3000 karakteri aşıyor
        # 0 satırlı tablo ekle
        tbl = d.add_table(rows=0, cols=0)
        blip = OxmlElement("a:blip")
        blip.set(qn("r:embed"), "rId99")
        tbl._element.append(blip)
        yol = self.kok / "sectpr.docx"
        d.save(yol)
        parcalar = list(docx_bolumleri(yol))
        # Sonunda fazladan boş bölüm üretilmemeli
        self.assertEqual(len(parcalar), 1)
        self.assertIn("a" * 100, parcalar[0][1])

    def test_docx_buyuk_gorsel_ocr_ve_kapatma(self):
        from docx import Document
        from PIL import Image
        d = Document()
        for boy in (100, 220):
            buf = io.BytesIO()
            Image.new("RGB", (boy, boy), "white").save(buf, format="PNG")
            buf.seek(0)
            d.add_picture(buf)
        yol = self.kok / "gorsel.docx"
        d.save(yol)
        havuz = SahteHavuz()
        s = self.oku(yol, havuz)
        self.assertEqual(len(havuz.isler), 1)
        self.assertIn("frekansı", s.sayfalar[0].metin)
        self.ayar.ocr.gomulu_gorsel = False
        s = cikar(yol, self.ayar, SahteHavuz(), self.kok / "kapali")
        self.assertEqual(s.sayfalar[0].metin, "")

    def test_docm(self):
        from docx import Document
        d = Document()
        d.add_paragraph("Makrolu belge")
        kaynak = self.kok / "a.docx"
        d.save(kaynak)
        yol = self.kok / "a.docm"
        with zipfile.ZipFile(kaynak) as z, zipfile.ZipFile(yol, "w") as out:
            for ad in z.namelist():
                b = z.read(ad)
                if ad == "[Content_Types].xml":
                    b = b.replace(b"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
                                  b"application/vnd.ms-word.document.macroEnabled.main+xml")
                out.writestr(ad, b)
        self.assertIn("Makrolu belge", self.oku(yol).sayfalar[0].metin)

    def test_pptx_iki_slayt_tablo_not_gorsel(self):
        from pptx import Presentation
        from pptx.util import Inches
        from PIL import Image
        d = Presentation()
        for i in range(2):
            s = d.slides.add_slide(d.slide_layouts[1])
            s.shapes.title.text = f"Başlık {i + 1}"
            s.notes_slide.notes_text_frame.text = "Konuşmacı notu"
        t = d.slides[0].shapes.add_table(1, 2, Inches(1), Inches(2), Inches(4), Inches(1)).table
        t.cell(0, 0).text, t.cell(0, 1).text = "Tür", "Ölçüm"
        buf = io.BytesIO()
        Image.new("RGB", (220, 220), "white").save(buf, format="PNG")
        buf.seek(0)
        d.slides[1].shapes.add_picture(buf, 0, 0)
        yol = self.kok / "s.pptx"
        d.save(yol)
        s = self.oku(yol, SahteHavuz()).sayfalar
        self.assertEqual([p.etiket for p in s], ["Slayt 1", "Slayt 2"])
        self.assertIn("Not: Konuşmacı notu", s[0].metin)
        self.assertIn("Tür\tÖlçüm", s[0].metin)
        self.assertIn("frekansı", s[1].metin)

    def test_xlsx_450_satir_200luk_parca(self):
        from openpyxl import Workbook
        d = Workbook()
        s = d.active
        s.title = "Ölçümler"
        for i in range(1, 451):
            s.append([i, f"Kayıt {i}"] if i != 2 else [None, None])
        yol = self.kok / "s.xlsx"
        d.save(yol)
        d.close()
        s = self.oku(yol).sayfalar
        self.assertEqual([p.etiket for p in s], ["Ölçümler (satır 1-200)", "Ölçümler (satır 201-400)", "Ölçümler (satır 401-450)"])
        self.assertIn("450\tKayıt 450", s[2].metin)
        self.assertNotIn("\n\n", s[0].metin)

    def test_odt_ods_odp(self):
        bas = ('<office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" '
               'xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0" '
               'xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0" '
               'xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0"><office:body>')
        veriler = {"odt": '<office:text><text:p>Türkçe <text:span>ölçüm</text:span><text:s text:c="2"/>son</text:p></office:text>',
                   "ods": '<office:spreadsheet><table:table table:name="Veri"><table:table-row table:number-rows-repeated="201"><table:table-cell><text:p>ölçüm</text:p></table:table-cell></table:table-row></table:table></office:spreadsheet>',
                   "odp": '<office:presentation><draw:page><text:p>ölçüm</text:p></draw:page><draw:page><text:p>son</text:p></draw:page></office:presentation>'}
        for uz, govde in veriler.items():
            with self.subTest(uz=uz):
                yol = self.kok / ("a." + uz)
                with zipfile.ZipFile(yol, "w") as z:
                    z.writestr("content.xml", bas + govde + "</office:body></office:document-content>")
                s = self.oku(yol).sayfalar
                self.assertIn("ölçüm", s[0].metin)
                self.assertEqual(len(s), 1 if uz == "odt" else 2)

    def test_odf_header_ve_nested_p_duplikasyon_engeli(self):
        # 13. bulgu: .ods header row dahil edilmeli, sonda boş parça üretilmemeli; .odt iç içe text:p çiftlenmemeli
        bas = ('<office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" '
               'xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0" '
               'xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0" '
               'xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0"><office:body>')
        # ODS başlık satırı ve sondaki boş tekrar satırları
        ods_xml = (bas + '<office:spreadsheet><table:table table:name="Tablo1">'
                   '<table:table-header-rows><table:table-row><table:table-cell><text:p>Başlık</text:p></table:table-cell></table:table-row></table:table-header-rows>'
                   '<table:table-row><table:table-cell><text:p>Veri1</text:p></table:table-cell></table:table-row>'
                   '<table:table-row table:number-rows-repeated="10"><table:table-cell><text:p></text:p></table:table-cell></table:table-row>'
                   '</table:table></office:spreadsheet></office:body></office:document-content>')
        ods_yol = self.kok / "tablo.ods"
        with zipfile.ZipFile(ods_yol, "w") as z:
            z.writestr("content.xml", ods_xml)
        ods_parcalar = list(odf_parcalari(ods_yol))
        self.assertEqual(len(ods_parcalar), 1)
        self.assertIn("Başlık", ods_parcalar[0][1])
        self.assertIn("Veri1", ods_parcalar[0][1])

        # ODT iç içe text:p (ör. frame/not içinde paragraf)
        odt_xml = (bas + '<office:text>'
                   '<text:p>Dış metin <draw:frame><draw:text-box><text:p>İç metin</text:p></draw:text-box></draw:frame></text:p>'
                   '</office:text></office:body></office:document-content>')
        odt_yol = self.kok / "metin.odt"
        with zipfile.ZipFile(odt_yol, "w") as z:
            z.writestr("content.xml", odt_xml)
        odt_parcalar = list(odf_parcalari(odt_yol))
        tam_metin = "\n".join(p[1] for p in odt_parcalar)
        self.assertEqual(tam_metin.count("İç metin"), 1)

    def test_rtf_cp1254_unicode_atlanan_gruplar(self):
        yol = self.kok / "a.rtf"
        yol.write_bytes(br"{\rtf1\ansi\ansicpg1254 I\'fd\'fe\'fdk {\*\gizli SAKLI} \u351? \uc2\u287??\par Son}")
        s = self.oku(yol).sayfalar[0].metin
        self.assertEqual(s, "Iışık  ş ğ\nSon")
        self.assertNotIn("SAKLI", s)

    def test_txt_cp1254_utf16_utf8_bom_ve_diger_metinler(self):
        for i, enc in enumerate(("cp1254", "utf-16", "utf-8-sig")):
            yol = self.kok / f"a{i}.txt"
            yol.write_bytes("  ölçüm ﬁkir  ".replace("ﬁ", "fi").encode(enc))
            self.assertEqual(self.oku(yol).sayfalar[0].metin, "ölçüm fikir")
        for uz in ("csv", "tsv", "md", "log", "xml", "json", "ini", "cfg", "srt", "vtt"):
            yol = self.kok / ("a." + uz)
            yol.write_text("Ölçüm ﬁkir", encoding="utf-8")
            self.assertEqual(self.oku(yol).sayfalar[0].metin, "Ölçüm fikir")
        yol = self.kok / "bos.txt"
        yol.touch()
        self.assertEqual([s.metin for s in self.oku(yol).sayfalar], [""])

    def test_html_script_style_atlanir(self):
        yol = self.kok / "a.html"
        yol.write_text("<style>gizli</style><p>Ölçüm &amp; güvenlik</p><script>saklı</script><div>Son</div>", encoding="utf-8")
        metin = self.oku(yol).sayfalar[0].metin
        self.assertIn("Ölçüm & güvenlik", metin)
        self.assertNotIn("gizli", metin)
        self.assertNotIn("saklı", metin)

    def test_eml_txt_eki_ve_derinlik_siniri(self):
        ic = EmailMessage()
        ic.set_content("İç ileti")
        ic.add_attachment(b"GIZLI", maintype="text", subtype="plain", filename="ic.txt")
        m = EmailMessage()
        m.set_content("Ana ileti ölçümü")
        m.add_attachment("Ek güvenlik".encode(), maintype="text", subtype="plain", filename="../ek.txt")
        m.add_attachment(ic)
        yol = self.kok / "a.eml"
        yol.write_bytes(m.as_bytes())
        s = self.oku(yol)
        self.assertEqual(s.sayfalar[0].etiket, "İleti")
        self.assertIn("Ek: ek.txt / Bölüm 1", [p.etiket for p in s.sayfalar])
        self.assertFalse((self.kok / "ek.txt").exists())
        self.assertIn("İç ileti", "\n".join(p.metin for p in s.sayfalar))
        self.assertNotIn("GIZLI", "\n".join(p.metin for p in s.sayfalar))
        self.assertTrue(any("derinlik 1" in u for u in s.uyarilar))

    def test_bozuk_ek_ve_yakalanmayan_istisnalar(self):
        # 7. bulgu testi: bozuk ek tüm iletiyi düşürmez; RuntimeError, ParseError, LookupError, zlib.error IsHatasi olur
        m = EmailMessage()
        m.set_content("Ana ileti")
        m.add_attachment(b"BOZUK PDF", maintype="application", subtype="pdf", filename="bozuk.pdf")
        yol = self.kok / "bozuk_ek.eml"
        yol.write_bytes(m.as_bytes())
        res = self.oku(yol)
        self.assertEqual(res.sayfalar[0].etiket, "İleti")
        self.assertTrue(any("bozuk.pdf" in u for u in res.uyarilar))

        # Çeşitli yakalanmayan istisnalar
        for exc in (RuntimeError("pdf error"), ET.ParseError("xml"), LookupError("cp0"), zlib.error("zlib"), TypeError("str")):
            with patch("videohat.belge.cikar._cikar", side_effect=exc):
                with self.assertRaises(IsHatasi):
                    cikar(self.kok / "test.pdf", self.ayar, None, self.kok / "ara")

    def test_pdf_metin_bos_sayfa_parola(self):
        import pymupdf
        yol = pdf_yaz(self.kok / "a.pdf", sayi=2)
        s = self.oku(yol)
        self.assertEqual(len(s.sayfalar), 2)
        self.assertEqual(s.sayfalar[0].kaynak, "metin")
        bos = self.kok / "bos.pdf"
        with pymupdf.open() as d:
            d.new_page()
            d.save(bos)
        s = self.oku(bos)
        self.assertEqual(s.sayfalar[0].metin, "")
        self.assertTrue(s.uyarilar)
        parolali = self.kok / "sifre.pdf"
        with pymupdf.open(yol) as d:
            d.save(parolali, encryption=pymupdf.PDF_ENCRYPT_AES_256, user_pw="gizli", owner_pw="gizli")
        with self.assertRaisesRegex(IsHatasi, "Parolalı PDF"):
            self.oku(parolali)

    def test_gorsel_tiff_ve_diger_bicimler(self):
        from PIL import Image
        yol = self.kok / "cok.tiff"
        im = Image.new("RGB", (220, 220), "white")
        im.save(yol, save_all=True, append_images=[im])
        s = self.oku(yol, SahteHavuz())
        self.assertEqual([p.no for p in s.sayfalar], [1, 2])
        for uz in ("png", "jpg", "jpeg", "tif", "bmp", "gif", "webp"):
            yol = self.kok / ("g." + uz)
            im.save(yol)
            self.assertEqual(self.oku(yol, SahteHavuz()).sayfalar[0].kaynak, "ocr")

    def test_eski_ofis_yok_ve_sahte_donusum(self):
        from docx import Document
        yol = self.kok / "eski.doc"
        yol.write_bytes(b"eski")
        with patch("videohat.belge.ofis.yollar.libreoffice", return_value=None), \
                patch("videohat.belge.ofis.yollar.artcraft_cli", return_value=None):
            s = self.oku(yol)
        self.assertEqual(s.durum, "desteklenmiyor")
        self.assertEqual(s.hata, "Eski .doc biçimi: LibreOffice veya wordcraft-cli bulunamadı")

        def donustur(komut, sn, ad):
            self.assertEqual(sn, 120)
            self.assertTrue(any(str(s).startswith("-env:UserInstallation=file:///") for s in komut))
            d = Document()
            d.add_paragraph("Çevrildi")
            d.save(Path(komut[-2]) / "eski.docx")

        with patch("videohat.belge.ofis.yollar.libreoffice", return_value=Path("soffice.exe")), patch("videohat.belge.ofis.calistir", side_effect=donustur):
            s = self.oku(yol)
        self.assertIn("Çevrildi", s.sayfalar[0].metin)

    def test_libreoffice_zaman_asimi(self):
        yol = self.kok / "a.doc"
        with patch("videohat.belge.ofis.yollar.libreoffice", return_value=Path("soffice.exe")), patch("videohat.belge.ofis.calistir", side_effect=IsHatasi("LibreOffice zaman aşımı")):
            with self.assertRaisesRegex(IsHatasi, "zaman aşımı"):
                eski_cevir(yol, self.kok / "ara")

    def test_harici_surec_zaman_asiminda_agac_oldurulur(self):
        import subprocess
        import os
        from videohat.belge.surec import calistir
        with patch("videohat.belge.surec.subprocess.Popen") as popen, patch("videohat.belge.surec.subprocess.run") as agac:
            p = popen.return_value
            p.pid = 1234
            p.communicate.side_effect = [subprocess.TimeoutExpired("soffice", 120), (b"", b"")]
            with self.assertRaisesRegex(IsHatasi, "LibreOffice zaman aşımı"):
                calistir(["soffice.exe"], 120, "LibreOffice")
            p.kill.assert_called_once()
            if os.name == "nt":
                self.assertEqual(agac.call_args.args[0], ["taskkill", "/PID", "1234", "/T", "/F"])

    def test_msg_modul_yok_ve_sahte_ileti(self):
        yol = self.kok / "a.msg"
        yol.write_bytes(b"mock")
        with patch.dict(sys.modules, {"extract_msg": None}):
            self.assertEqual(self.oku(yol).durum, "desteklenmiyor")
        class Ileti:
            body = "MSG gövde"
            htmlBody = "<html><body>MSG html</body></html>"  # 7(c) str olarak htmlBody
            attachments = [SimpleNamespace(data=b"ek metni", longFilename="a.txt", shortFilename=None)]
            def __enter__(self): return self
            def __exit__(self, *args): pass
        with patch.dict(sys.modules, {"extract_msg": SimpleNamespace(openMsg=lambda _: Ileti())}):
            s = self.oku(yol)
        self.assertEqual(s.sayfalar[0].metin, "MSG gövde")
        self.assertIn("ek metni", s.sayfalar[1].metin)
