"""VideoHat özel hata sınıfları."""


class VideoHatHatasi(Exception):
    """Tüm VideoHat hatalarının tabanı."""


class AyarHatasi(VideoHatHatasi):
    """ayar.toml okunamadı veya bozuk."""


class OrtamHatasi(VideoHatHatasi):
    """Eksik dosya/araç/model gibi ortam sorunu (çıkış kodu 2)."""


class IsHatasi(VideoHatHatasi):
    """Tek bir işin (video) işlenmesi sırasındaki hata (çıkış kodu 1)."""


class UpscaleHatasi(VideoHatHatasi):
    """Real-ESRGAN çalışmadı (Vulkan yok vb.); iş devam eder."""


class CeviriHatasi(VideoHatHatasi):
    """Çeviri modeli yüklenemedi veya çeviri sırasında hata oluştu."""
