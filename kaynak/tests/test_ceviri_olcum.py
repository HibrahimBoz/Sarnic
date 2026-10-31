"""NLLB-200 çeviri kalite (chrF++) ve süre ölçüm testi."""
import os
from pathlib import Path
import time
import unittest

from videohat import yollar
from videohat.ceviri import Cevirmen, _cihaz

VERI = {
    "en": [
        ("The meeting will start at nine o'clock tomorrow morning.", "Toplantı yarın sabah saat dokuzda başlayacak."),
        ("Please send the signed contract to the finance department by Friday.", "Lütfen imzalı sözleşmeyi cuma gününe kadar finans departmanına gönderin."),
        ("The temperature of the engine rose above the safe limit during the test.", "Test sırasında motorun sıcaklığı güvenli sınırın üzerine çıktı."),
        ("In this lecture we will discuss the causes of the First World War.", "Bu derste Birinci Dünya Savaşı'nın nedenlerini tartışacağız."),
        ("The report shows that sales increased by twelve percent last year.", "Rapor, satışların geçen yıl yüzde on iki arttığını gösteriyor."),
        ("Do not open the cover while the device is connected to power.", "Cihaz elektriğe bağlıyken kapağı açmayın."),
        ("The witness said he saw a black car near the bank at midnight.", "Tanık, gece yarısı bankanın yakınında siyah bir araba gördüğünü söyledi."),
        ("All employees must complete the security training before the end of the month.", "Tüm çalışanlar ay sonundan önce güvenlik eğitimini tamamlamalıdır."),
    ],
    "az": [
        ("Görüş sabah saat doqquzda başlayacaq.", "Görüşme sabah saat dokuzda başlayacak."),
        ("Zəhmət olmasa, imzalanmış müqaviləni cümə gününə qədər maliyyə şöbəsinə göndərin.", "Lütfen imzalı sözleşmeyi cuma gününe kadar finans departmanına gönderin."),
        ("Sınaq zamanı mühərrikin temperaturu təhlükəsiz həddən yuxarı qalxdı.", "Test sırasında motorun sıcaklığı güvenli sınırın üzerine çıktı."),
        ("Bu mühazirədə Birinci Dünya Müharibəsinin səbəblərini müzakirə edəcəyik.", "Bu derste Birinci Dünya Savaşı'nın nedenlerini tartışacağız."),
        ("Hesabat göstərir ki, keçən il satışlar on iki faiz artıb.", "Rapor, satışların geçen yıl yüzde on iki arttığını gösteriyor."),
        ("Cihaz elektrik şəbəkəsinə qoşulu olarkən qapağı açmayın.", "Cihaz elektriğe bağlıyken kapağı açmayın."),
        ("Şahid gecə yarısı bankın yaxınlığında qara maşın gördüyünü dedi.", "Tanık, gece yarısı bankanın yakınında siyah bir araba gördüğünü söyledi."),
        ("Bütün işçilər ayın sonuna qədər təhlükəsizlik təlimini başa vurmalıdır.", "Tüm çalışanlar ay sonundan önce güvenlik eğitimini tamamlamalıdır."),
    ],
    "ru": [
        ("Встреча начнётся завтра в девять часов утра.", "Toplantı yarın sabah saat dokuzda başlayacak."),
        ("Пожалуйста, отправьте подписанный договор в финансовый отдел до пятницы.", "Lütfen imzalı sözleşmeyi cuma gününe kadar finans departmanına gönderin."),
        ("Во время испытания температура двигателя поднялась выше безопасного предела.", "Test sırasında motorun sıcaklığı güvenli sınırın üzerine çıktı."),
        ("На этой лекции мы обсудим причины Первой мировой войны.", "Bu derste Birinci Dünya Savaşı'nın nedenlerini tartışacağız."),
        ("Отчёт показывает, что в прошлом году продажи выросли на двенадцать процентов.", "Rapor, satışların geçen yıl yüzde on iki arttığını gösteriyor."),
        ("Не открывайте крышку, пока устройство подключено к сети.", "Cihaz elektriğe bağlıyken kapağı açmayın."),
        ("Свидетель сказал, что видел чёрную машину возле банка в полночь.", "Tanık, gece yarısı bankanın yakınında siyah bir araba gördüğünü söyledi."),
        ("Все сотрудники должны пройти обучение по безопасности до конца месяца.", "Tüm çalışanlar ay sonundan önce güvenlik eğitimini tamamlamalıdır."),
    ],
}


class CeviriOlcumTesti(unittest.TestCase):
    def test_24_cumle_chrf_ve_sure(self):
        # 1. Gerekli kütüphaneler kontrolü
        try:
            import sacrebleu
            import ctranslate2
            import sentencepiece
        except ImportError as e:
            self.skipTest(f"Gerekli kütüphaneler eksik (sacrebleu/ctranslate2/sentencepiece): {e}")

        # 2. Model yolu kontrolü
        env_yol = os.environ.get("SARNIC_CEVIRI_MODELI")
        model_yolu = Path(env_yol) if env_yol else yollar.ceviri_modeli()
        if not model_yolu.is_dir() or not (model_yolu / "model.bin").is_file():
            self.skipTest(f"Çeviri model klasörü bulunamadı: {model_yolu}")

        # 3. Çevirmen kurulumu
        cihaz, compute_type = _cihaz()
        try:
            cevirmen = Cevirmen(model_yolu=model_yolu, cihaz=cihaz, compute_type=compute_type)
        except Exception as e:
            self.skipTest(f"Çevirmen başlatılamadı: {e}")

        esikler = {
            "en": 80.0,
            "az": 65.0,
            "ru": 65.0,
        }

        skorlar: dict[str, float] = {}
        t0 = time.time()

        for dil in ("en", "az", "ru"):
            ciftler = VERI[dil]
            kaynaklar = [k for k, _ in ciftler]
            referanslar = [r for _, r in ciftler]

            hip = cevirmen.cevir(kaynaklar, dil)
            skor = sacrebleu.corpus_chrf(hip, [referanslar], word_order=2).score
            skorlar[dil] = skor

        toplam_sure = time.time() - t0
        print(f"\n[ÇEVİRİ ÖLÇÜMÜ] Cihaz: {cihaz}, 24 cümle toplam süre: {toplam_sure:.2f} s")
        for dil, skor in skorlar.items():
            print(f"  {dil}->tr chrF++: {skor:.2f} (eşik: {esikler[dil]})")

        for dil, esik in esikler.items():
            self.assertGreaterEqual(
                skorlar[dil],
                esik,
                f"{dil}->tr chrF++ skoru ({skorlar[dil]:.2f}) eşiğin ({esik}) altında!",
            )


if __name__ == "__main__":
    unittest.main()
