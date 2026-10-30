"""Çevrimdışı belge çıkarma, OCR ve aranabilir PDF."""

GORUNTU_UZANTILARI = frozenset(".png .jpg .jpeg .tif .tiff .bmp .gif .webp".split())
BELGE_UZANTILARI = frozenset((
    ".pdf .docx .docm .pptx .pptm .xlsx .xlsm .odt .ods .odp .rtf "
    ".txt .csv .tsv .md .log .xml .json .ini .cfg .srt .vtt .html .htm .eml .msg .doc .xls .ppt"
).split())
UZANTILAR = BELGE_UZANTILARI | GORUNTU_UZANTILARI
