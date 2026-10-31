"""Çeviri çekirdeği birim testleri."""
from pathlib import Path
import unittest
from unittest.mock import Mock

from videohat import yollar
from videohat.ayar import CeviriAyar
from videohat.ceviri import cumlelere_bol, dil_tahmin, Cevirmen
from videohat.hatalar import CeviriHatasi


class CeviriTestleri(unittest.TestCase):
    def test_romen_harfli_sozcuk_cumleyi_boler(self):
        # İnceleme bulgusu: "dil.", "did." Romen rakamı sanılıyordu.
        self.assertEqual(len(cumlelere_bol("Bu ana dil. Sonra okul geldi.")), 2)
        self.assertEqual(len(cumlelere_bol("He did. Then he left.")), 2)
        self.assertEqual(len(cumlelere_bol("XIV. Louis geldi. Sonra gitti.")), 2)

    def test_cumle_sira_sayisi(self):
        metin = "I. Dünya Savaşı başladı. Sonra bitti."
        cumleler = cumlelere_bol(metin)
        self.assertEqual(cumleler, ["I. Dünya Savaşı başladı.", "Sonra bitti."])

    def test_kisa_sozcuk_sonu(self):
        metin = "Bugün iş. Yarın ay."
        cumleler = cumlelere_bol(metin)
        self.assertEqual(cumleler, ["Bugün iş.", "Yarın ay."])

    def test_tek_satir_sonu_bolmez(self):
        metin = "Toplantı\nyarın başlar."
        cumleler = cumlelere_bol(metin)
        self.assertEqual(cumleler, ["Toplantı yarın başlar."])

    def test_bosluksuz_uzun(self):
        metin = "A" * 900
        parcalar = cumlelere_bol(metin)
        self.assertEqual(len(parcalar), 3)
        self.assertEqual([len(p) for p in parcalar], [400, 400, 100])

    def test_dil_tahmin_x_ve_q_turkce_ingilizceyi_azerice_yapmaz(self):
        # İnceleme bulgusu: q/x oranı kuralı Türkçe matematik ve İngilizce metni "az" sayıyordu.
        self.assertEqual(dil_tahmin("f(x) fonksiyonu x değişkeni için türevlenebilir bir fonksiyondur"), "tr")
        self.assertEqual(dil_tahmin("Explain the experiment and the box plot for the next exam"), "en")
        self.assertEqual(dil_tahmin("Install Intel drivers"), "en")
        self.assertEqual(dil_tahmin("RAPOR İÇİN VE BU KONU İLE İLGİLİ NOT"), "tr")
        self.assertEqual(dil_tahmin("Biz bu qerari xalq ucun verdik ve bu qanun olan bir xidmetdir"), "az")

    def test_dil_tahmin(self):
        # ru/az/tr/en birer örnek
        self.assertEqual(dil_tahmin("Это простое предложение на русском языке для теста."), "ru")
        self.assertEqual(dil_tahmin("Bu cümlə Azərbaycan dilində yazılmış bir nümunədir."), "az")
        self.assertEqual(dil_tahmin("Bu rapor toplantı için hazırlandı ve sunuldu."), "tr")
        self.assertEqual(dil_tahmin("This is an English sentence created for testing language detection."), "en")

        # Özel kurallar
        self.assertEqual(
            dil_tahmin("Bu raporda 'meeting minutes' geçiyor ve bu çok önemli."),
            "tr",
        )
        self.assertEqual(
            dil_tahmin("Biz bu qerari xalq ucun verdik ve bu qanun olan bir xidmetdir"),
            "az",
        )
        self.assertEqual(
            dil_tahmin("The meeting ıs on Friday and the report ış ready for the team"),
            "en",
        )
        self.assertEqual(dil_tahmin("Q3 Report"), "en")
        self.assertEqual(dil_tahmin("OK"), "tr")

    def test_bos_metin_cevrilmez(self):
        sahte = Mock()
        cevirmen = Cevirmen(model_yolu=Path("sahte"), cevirici=sahte)
        girdi = ["", "  "]
        cikti = cevirmen.cevir(girdi, kaynak_dil="en")
        self.assertEqual(cikti, girdi)
        sahte.translate_batch.assert_not_called()

    def test_uzun_parca(self):
        # 600 karakter noktasız metin
        kelime = "deneme "
        metin = (kelime * (600 // len(kelime) + 1))[:600]
        self.assertEqual(len(metin), 600)

        sahte = Mock()

        def sahte_translate(batch, **kwargs):
            return [Mock(hypotheses=[["tur_Latn", "çeviri"]]) for _ in batch]

        sahte.translate_batch.side_effect = sahte_translate
        cevirmen = Cevirmen(model_yolu=Path("sahte"), cevirici=sahte)
        sonuc = cevirmen.cevir([metin], kaynak_dil="en")
        self.assertTrue(sonuc)

        self.assertTrue(sahte.translate_batch.called)
        # translate_batch'e giden kaynak_belirteclerini incele
        for cagri in sahte.translate_batch.call_args_list:
            kaynak_belirtecleri = cagri[0][0]
            for tok_listesi in kaynak_belirtecleri:
                # [kod] + tok + ["</s>"]
                asıl_tok = tok_listesi[1:-1]
                parca_metni = " ".join(asıl_tok)
                self.assertLessEqual(len(parca_metni), 400)

    def test_model_yok(self):
        with self.assertRaises(CeviriHatasi):
            Cevirmen(model_yolu=Path("bulunmayan/bir/yol/model"), cihaz="cpu", compute_type="int8")

    def test_tr_cevrilmez(self):
        sahte = Mock()
        cevirmen = Cevirmen(model_yolu=Path("sahte"), cevirici=sahte)
        girdi = ["Bu bir Türkçe metindir.", "İkinci cümle."]
        cikti = cevirmen.cevir(girdi, kaynak_dil="tr")
        self.assertEqual(cikti, girdi)
        sahte.translate_batch.assert_not_called()

    def test_ayar_ve_yol(self):
        ayar = CeviriAyar()
        self.assertTrue(ayar.etkin)
        self.assertEqual(ayar.model, "")
        self.assertEqual(ayar.diller, ("en", "az", "ru"))
        self.assertEqual(ayar.batch, 16)
        self.assertTrue(str(yollar.ceviri_modeli()).endswith("nllb-200-distilled-1.3B-ct2-int8"))

    def test_paragraflar_korunur(self):
        sahte = Mock()
        sahte.translate_batch.side_effect = lambda batch, **kw: [
            Mock(hypotheses=[["tur_Latn", f"c{i}"]]) for i in range(len(batch))]
        cevirmen = Cevirmen(model_yolu=Path("sahte"), cevirici=sahte)
        sonuc = cevirmen.cevir(["Report title\n\nFirst one here. Second one here."], kaynak_dil="en")
        self.assertEqual(sonuc, ["c0\n\nc1 c2"])

    def test_onbellek(self):
        sahte = Mock()
        sahte.translate_batch.return_value = [Mock(hypotheses=[["tur_Latn", "merhaba"]])]
        cevirmen = Cevirmen(model_yolu=Path("sahte"), cevirici=sahte)
        metin = ["Hello"]
        ilk = cevirmen.cevir(metin, kaynak_dil="en")
        self.assertEqual(ilk, ["merhaba"])
        self.assertEqual(sahte.translate_batch.call_count, 1)

        # Aynı metin tekrar çevrilince sahte çağrılmamalı
        ikinci = cevirmen.cevir(metin, kaynak_dil="en")
        self.assertEqual(ikinci, ["merhaba"])
        self.assertEqual(sahte.translate_batch.call_count, 1)


if __name__ == "__main__":
    unittest.main()
