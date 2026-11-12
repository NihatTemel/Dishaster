# Çizer'in eşyaları: defter ve kalem

Bu klasör art tarafının (Özgür) teslim ettiği Çizer (Bilmiyorum) eşyalarını içerir. Bu dosya kod yazan kişi (Nihat) ve onun yapay zeka asistanı için hazırlandı.

Son güncelleme: 12 Kasım 2026 (ilk teslim).

## Eşyalar

| Prefab | Boyut (cm) | Pivot | Parçalar |
| --- | --- | --- | --- |
| `PF_RecipeBook_01` | 32,5 × 21,8 × 4,2 (spiral dahil) | defterin altının ortası | `Book`, `Page` |
| `PF_Marker_01` | 19,2 × 2,8 × 3,5 (kapak takılı) | gövdenin üstünde, ucundan 6,6 cm geride (uç -X'te) | `Body`, `Cap`, `Tip` |

Eşyalar karakterlerin ~1,5 kat büyük ellerine göre ölçeklendi; gerçek defter ve kalemden büyük olmaları bilinçli.

## Defter (`PF_RecipeBook_01`)

Yatay spiralli çizim defteri, hep açık: ön kapak arkaya katlı, tek boş sayfa yukarıda. Spiral sol kısa kenarda.

```text
PF_RecipeBook_01   kök; pivot defterin altının ortası (kapağın alt yüzü)
  Book             kapak + sayfa kenarları + 8 bakır halka   M_CharacterItems_Atlas
  Page             çizim yüzeyi                              M_RecipeBook_Page
```

- **Yön:** sayfa yüzü yerel **+Y**'ye bakıyor (prefab düz yatar). Uzun kenar X ekseninde, kısa kenar Z ekseninde. Spiral **+X** kısa kenarında (kurulumdan sonra doğrulandı).
- **Page:** düz bir yüzey, 13 yüz, **29,6 × 20,5 cm**, yerel Y = 2,3 cm (sayfanın üstü). UV tam **0–1**: U uzun kenar boyunca, **spiral tarafı U = 0**; V kısa kenar boyunca. Yani doku spiral solda olacak şekilde düz okunur. Canlı çizim dokusu (RenderTexture) doğrudan bu malzemeye ya da `Page`'in renderer'ına verilebilir.
- **Oran farkı (önemli):** sayfa oranı **≈ 1,44 : 1** (10:7). Şimdiki `NotebookInstaller` sayfası 36 × 18 cm, yani **2 : 1**, çizim dokusu da bu orana göre. Doku 2:1 kalırsa çizim bu sayfada yatay olarak sıkışır. Çizim dokusunun oranını 1,44:1 yapmak önerilir (örn. 1440 × 1000 ya da 1024 × 712). `PageSize` de `(0.296f, 0.205f)` olur.
- Eski geçici defterde spiral (Binding) üst kenardaydı; bu defterde sol kenarda. Elle tutma pozu (sol el) buna göre ayarlanmalı: sol el spiralin olduğu kenarı tutar.
- `M_RecipeBook_Page` düz çok açık krem (#F7F4EE). Kod kendi sayfa malzemesini (`M_NotebookPage`) kullanacaksa `Page`'in renderer'ına onu vermesi yeterli.
- Sunum pozu (`KarakterPozu.cs`, `Soket_Defter`) şimdilik geçici krem kutuyla kurulu; bu defter oraya konacak.

## Kalem (`PF_Marker_01`)

Keçe uçlu kalın kalem, çıkarılabilir kapaklı. Gövde krem, boyun ve kapak adaçayı (Çizer'in rol rengi), uç koyu kahve.

```text
PF_Marker_01       kök; pivot gövdenin üstünde
  Body             gövde + boyun + keçe uç     M_CharacterItems_Atlas
  Cap              kapak (takılı konumda), klipsli   M_CharacterItems_Atlas
  Tip              boş nesne: keçe ucun en ucu (çizim noktası)
```

- **Yön:** kalem yerel X ekseni boyunca uzanıyor, keçe uç **-X** tarafında; klips +Y'de (yukarıda).
- **Tip:** keçe ucun en ucu, yerel **(-0.066, 0, 0)** (kurulumdan sonra doğrulandı). `NotebookInstaller`'daki kalem eli "kökü kalemin ucu" mantığıyla kurulu; eli bu noktaya bağlamak ya da kalemi `Tip` bu noktaya gelecek şekilde konumlamak yeterli.
- **Cap:** çizerken `Cap` kapatılabilir ya da ele/kemere taşınabilir; takılıyken yerel konumu (0, 0, 0). Kapak çıkınca keçe uç görünür.
- Kalem kulakta da taşınabilir (brief'te "kulakta kalem" geçiyor); bunun için ayrı bir model yok, aynı prefab kullanılır.

## Dosyalar

```text
Assets/ART/3D/CharacterItems/
  SM_RecipeBook_01.fbx, SM_Marker_01.fbx
  Textures/T_CharacterItems_Atlas_BaseColor.png   (2048, iki eşyanın ortak dokusu)
  Materials/M_CharacterItems_Atlas.mat, M_RecipeBook_Page.mat   Standard
  Prefabs/PF_RecipeBook_01.prefab, PF_Marker_01.prefab
  Editor/KarakterEsyaKurulum.cs   menü: Dishaster > Art > Karakter esyalarini kur
```

- Dosya adlarında sürüm eki yok. Art güncellemede FBX ve dokuların üzerine yazar, `.meta` korunur.
- Kurulum yeniden çalıştırıldığında prefab silinmez: yalnızca mesh, malzeme ve konumlar güncellenir. Eklediğin bileşenler korunur. `Book`, `Page`, `Body`, `Cap`, `Tip` adlarını değiştirme; kurulum onları adla bulur.
- Collider yok. Defter için tek Box Collider, kalem için Capsule Collider uygun.
