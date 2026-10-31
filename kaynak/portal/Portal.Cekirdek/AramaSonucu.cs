namespace Portal.Cekirdek;

/// <summary>
/// Arama motoru sorgu sonucundaki segment eşleşmesini temsil eden kayıt.
/// </summary>
/// <param name="DersAd">Dersin adı.</param>
/// <param name="JsonYolu">Transkript JSON dosyasının yolu.</param>
/// <param name="SegmentNo">Segment numarası.</param>
/// <param name="BasSn">Segment başlangıç zamanı (saniye).</param>
/// <param name="Metin">Efektif (düzeltmeler uygulanmış), orijinal yazımlı metin.</param>
/// <param name="Puan">Arama puanı (yüksek puan önce sıralanır).</param>
/// <param name="Vurgular">Orijinal metin üzerindeki vurgu aralıkları (başlangıç ve uzunluk).</param>
public record AramaSonucu(
    string DersAd,
    string JsonYolu,
    int SegmentNo,
    double BasSn,
    string Metin,
    double Puan,
    (int Bas, int Uzunluk)[] Vurgular,
    bool CevirideEslesti = false
);
