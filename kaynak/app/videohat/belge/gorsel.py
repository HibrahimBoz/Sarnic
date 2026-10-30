"""Eski görüntüleri Pillow, gerektiğinde PyMuPDF ile okur."""


def sayfa_sayisi(yol):
    from PIL import Image
    try:
        with Image.open(yol) as im:
            im.load()
            return getattr(im, "n_frames", 1)
    except (OSError, ValueError):
        import pymupdf
        with pymupdf.open(yol) as belge:
            return len(belge)


def kare_oku(yol, no=1, dpi=None):
    from PIL import Image, ImageOps
    try:
        with Image.open(yol) as im:
            im.seek(no - 1)
            return ImageOps.exif_transpose(im).convert("RGB")
    except (OSError, ValueError):
        import pymupdf
        with pymupdf.open(yol) as belge:
            kw = {"colorspace": pymupdf.csRGB, "alpha": False}
            if dpi:
                kw["dpi"] = dpi
            pix = belge[no - 1].get_pixmap(**kw)
            return Image.frombytes("RGB", (pix.width, pix.height), pix.samples)
