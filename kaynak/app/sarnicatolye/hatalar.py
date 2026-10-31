"""Sarnıç Atölye özel hata sınıfları."""


class SarnicAtolyeHatasi(Exception):
    """Tüm Sarnıç Atölye hatalarının tabanı."""


class AyarHatasi(SarnicAtolyeHatasi):
    """ayar.toml okunamadı veya bozuk."""


class OrtamHatasi(SarnicAtolyeHatasi):
    """Eksik dosya/araç/model gibi ortam sorunu (çıkış kodu 2)."""


class IsHatasi(SarnicAtolyeHatasi):
    """Tek bir işin (video) işlenmesi sırasındaki hata (çıkış kodu 1)."""


class UpscaleHatasi(SarnicAtolyeHatasi):
    """Real-ESRGAN çalışmadı (Vulkan yok vb.); iş devam eder."""


class CeviriHatasi(SarnicAtolyeHatasi):
    """Çeviri modeli yüklenemedi veya çeviri sırasında hata oluştu."""
