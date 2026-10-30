"""Çevrimdışı belge çıkarma, OCR ve aranabilir PDF."""

ESKI_OFIS_HEDEFLERI = {
    uz: hedef for hedef, uzantilar in (
        ("docx", ".doc .dot .wpd .wps .wri .lwp .sam .sxw .stw .sdw .mcw .602 .hwp .abw .zabw .cwk"),
        ("xlsx", ".xls .xlt .xlw .xlsb .wk1 .wk3 .wk4 .123 .wq1 .wb2 .sxc .stc .sdc .dbf .dif .slk"),
        ("pptx", ".ppt .pps .pot .sxi .sti .sdd .key"),
        ("pdf", ".vsd .vdx .pub .sxd .std .sda .cdr .wpg .cgm"),
    ) for uz in uzantilar.split()
}
YEREL_UZANTILARI = frozenset(".xps .oxps .epub .mobi .fb2 .cbz .svg".split())
DJVU_UZANTILARI = frozenset((".djvu", ".djv"))
GORUNTU_UZANTILARI = frozenset((
    ".png .jpg .jpeg .tif .tiff .bmp .gif .webp "
    ".pcx .tga .dib .jpe .jfif .jp2 .j2k .jpx .psd .pnm .pbm .pgm .ppm"
).split()) | DJVU_UZANTILARI
BELGE_UZANTILARI = frozenset((
    ".pdf .docx .docm .pptx .pptm .xlsx .xlsm .odt .ods .odp .rtf "
    ".txt .csv .tsv .md .log .xml .json .ini .cfg .srt .vtt .html .htm .eml .msg "
    ".mht .mhtml .chm .asc .nfo .lst .prn .dos"
).split()) | frozenset(ESKI_OFIS_HEDEFLERI) | YEREL_UZANTILARI
UZANTILAR = BELGE_UZANTILARI | GORUNTU_UZANTILARI
