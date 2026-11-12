# Araç gereçler

Bu klasör art tarafının (Özgür) teslim ettiği mutfak araç gereçlerini içerir. Bu dosya kod yazan kişi ve onun yapay zeka asistanı için hazırlandı.

Son güncelleme: 12 Kasım 2026 (ölçü kabı eklendi).

## Aletler

| Prefab | Boyut (cm) | Pivot | Atlas |
| --- | --- | --- | --- |
| `PF_Pot_Copper_01` | 55,3 × 41,8 × 20,9 (kulplarla) | taban ortası | Cookware |
| `PF_Pot_Copper_Lid_01` | Ø41,8 × 10,7 | kapağın alt ortası | Cookware |
| `PF_Pan_Copper_01` | 75,2 × 40,7 × 9,5 (saplı) | gövde tabanının ortası | Cookware |
| `PF_MixingBowl_01` | Ø38,5 × 17,5 | taban ortası | Cookware |
| `PF_Bowl_Soup_01` | Ø22,7 × 12,8 | taban ortası | Cookware |
| `PF_Plate_Serving_01` | Ø40 × 4 | taban ortası | Cookware |
| `PF_CuttingBoard_01` | 72 × 43,2 × 5,8 | taban ortası | Cookware |
| `PF_Knife_Chef_01` | 60,5 × 10,8 × 4 | sapın ortası | Utensil |
| `PF_Spatula_01` | 56,9 × 14,9 × 3,9 | sapın ortası | Utensil |
| `PF_Spoon_Wooden_01` | 52,6 × 12,6 × 6,7 | sapın ortası | Utensil |
| `PF_Ladle_01` | 16,1 × 34,4 × 51,3 (dik) | sapın tutulan yeri | Utensil |
| `PF_MeasuringSpoon_01` | 35,6 × 11,1 × 6,5 | sapın ortası | Utensil |

- Elde tutulan aletlerin (bıçak, spatula, kaşıklar, kepçe) pivotu tutulan yerde: doğrudan Aşçı'nın el kemiğine (`RightHand`) bağlanabilir, ayrı bir tutma noktası gerekmez.
- Tencere kapağı ayrı prefab. Tencerenin üstüne oturtmak için kapağı tencerenin pivotundan **20,9 cm** yukarı koy.
- Aletler Aşçı'nın eline göre büyütüldü (Aşçı'nın eli gerçeğin ~1,5 katı); gerçek mutfak eşyasından büyük olmaları bilinçli.

## Ölçü kabı (12 Kasım 2026)

`PF_MeasuringCup_01`: şeffaf cam ölçü kabı, yaklaşık 21 × 14 × 15,5 cm (gaga ve kulpla), pivot taban ortası. Diğer aletlerden farklı olarak birden çok parçası ve kendi malzemeleri var (atlas kullanmaz).

```text
PF_MeasuringCup_01   kök, pivot taban ortası
  Glass_Outer        dış cam   M_MeasuringCup_Glass        şeffaf, render queue 3001
  Glass_Inner        iç cam    M_MeasuringCup_Glass_Inner  şeffaf, render queue 3000 (aynı doku)
  Base               taban halkası   M_MeasuringCup_Base   opak
  Marks              ölçek ve rakamlar (100–500)  M_MeasuringCup_Marks  opak
  Liquid             sıvı: kabın iç hacmi (gaga dahil), ağza kadar dolu  M_MeasuringCup_Liquid  KAPALI
  Levels             boş nesneler: Level_000ml, Level_100ml ... Level_600ml
```

### Sıvı ve doluluk (Nihat için)

**Sorun:** `KitchenCounterInstaller.BuildMeasuringCup` ve `KitchenContainer.UpdateFill` sıvıyı Unity silindiriyle (`Fill`) gösteriyor ve silindiri ölçekliyor. Bu kabın iç duvarı ~11 cm'ye (~430 ml) kadar düz bir silindir, ama üstünde **gaga** dışa açılıyor. Silindir gagaya girmez; 430 ml'nin üstünde gaganın içi boş görünür. Ayrıca dökerken kap 75° eğilince ölçeklenen silindir de kapla birlikte yan yatar, yüzey yatay kalmaz.

**Önerilen çözüm (çorbayla birlikte yapılacak):** prefabdaki `Liquid` mesh'i kabın iç hacmini gaga dahil tam dolduruyor ve **ölçeklenmemeli**. Seviyeyi bir gölgelendirici keser: dünya uzayında yatay bir düzlemin üstü atılır (clip), yüzey ön yüzlerin arkasındaki arka yüzlerle (Cull Off) doldurulur. Böylece yüzey hep yatay kalır, gagaya da kendiliğinden uyar. Kodda `fill.localScale` yerine malzemeye bir seviye yüksekliği verilir; renk ayarı (`MixedColor`) aynen kalır. Aynı gölgelendirici çorba tenceresi ve kasede de kullanılacak. Gölgelendirici gelene kadar `Liquid` kapalı (inactive) duruyor.

**Şimdilik silindirle kullanmak istersen** (`KitchenContainer` alanları, kökün yerel uzayında, metre):

| Alan | Değer | Not |
| --- | --- | --- |
| `capacity` | **600** | Ölçek 500'e kadar okunuyor, ağız ~600. Daha fazlası gerekirse ikinci kap kullanılır (Özgür). |
| `fillBottom` | **0.0119** | 100–500 çizgilerinden geriye uzatılan 0 ml |
| `fillTop` | **0.1471** | aynı çizgilerle 600 ml |
| `fillRadiusBottom` / `fillRadiusTop` | **0.059** / **0.060** | camın iç yarıçapı |

Bu değerlerle her 100 ml ≈ 2,25 cm ve sıvı yüzeyi çizgilerle hizalanır. Gerçek iç taban 1,59 cm'de (`Level_000ml`); çok az sıvıda silindirin alt kısmı camın dibine gömülür, görünmez. ~430 ml'nin üstünde gaga boş kalır.

`Base` ve `Marks` görünen parçalar; `Fill` silindirini prefabın içine ekleme, `KitchenCounterInstaller` zaten ekliyor (art prefabına ikinci bir `Fill` konursa çift olur).

- **İç ve dış cam neden ayrı:** şeffaf malzemede Unity bir mesh'in kendi üçgenlerini sıralamaz; tek parça camda iç duvar dış duvarın önüne çizilip kırık üçgen deseni oluşuyordu. Ayrı parça ve farklı render queue ile önce iç, sonra dış cam çizilir. İkisinin render queue değerini değiştirme.
- Cam Standard "Transparent" modunda (Fade değil; Fade parlamayı da soldurur, cam görünmez olur). Opaklık BaseColor dokusunun alfasında (gövde ~0,1, kenarlar ~0,2). Cam gölge düşürmez (Cast Shadows kapalı).
- **Doluluk:** `Levels` altındaki boş nesneler ölçek çizgilerinin yüksekliğinde (pivot'a göre): 000 ml 1,59 cm (iç taban), 100 ml 3,44, 200 ml 5,66, 300 ml 7,91, 400 ml 10,15, 500 ml 12,45, 600 ml 15,03 cm (ağız). Aradaki değerler doğrusal ara değerle bulunur. Sıvı için aşağıdaki "Sıvı ve doluluk" bölümüne bak.
- Kör Aşçı rakamları okuyamaz; seviyeyi Yorumcu söyler. Rakamlar dokuda değil geometride (kabartma), her çözünürlükte okunur.
- Collider yok. Kap için convex MeshCollider ya da bir Capsule + kulp için Box uygun.

## Dosyalar

```text
Assets/ART/3D/Tools/
  SM_<Ad>.fbx                 tek alet, tek nesne
  Textures/T_Cookware_Atlas_{BaseColor,MetallicSmoothness,Normal}.png   (2048)
  Textures/T_Utensil_Atlas_{BaseColor,MetallicSmoothness,Normal}.png    (2048)
  SM_MeasuringCup_01.fbx      ölçü kabı: 4 parça + Level_* boş nesneleri
  Textures/T_MeasuringCup_{Glass,Base,Marks}_{BaseColor,MetallicSmoothness}.png   (1024; camın BaseColor alfası = opaklık)
  Materials/M_Cookware_Atlas.mat, M_Utensil_Atlas.mat   Standard (Metallic)
  Materials/M_MeasuringCup_{Glass,Glass_Inner,Base,Marks,Liquid}.mat
  Prefabs/PF_<Ad>.prefab
  Editor/AracKurulum.cs       menü: Dishaster > Art > Araclari kur
```

- Dosya adlarında sürüm eki yok. Art güncellemede FBX ve dokuların üzerine yazar, `.meta` korunur, bağlar kopmaz. Taşıma, yeniden adlandırma.
- Kurulum yeniden çalıştırıldığında prefab silinmez: yalnızca mesh ve malzeme güncellenir. Prefaba eklediğin bileşenler (script, collider, rigidbody) korunur. Ama `Model` alt nesnesinin adını değiştirme; kurulum onu adla bulur.

## Prefab yapısı

```text
PF_Knife_Chef_01    kök; pivot yukarıdaki tabloda
  Model             MeshFilter + MeshRenderer
```

- Ölçek metre. Collider ve Rigidbody yok; oyun tarafı ekler. Kap kacak için convex MeshCollider ya da birkaç Box/Capsule collider uygun.
- Yedi kap kacak `M_Cookware_Atlas`, beş el aleti `M_Utensil_Atlas` malzemesini paylaşır (iki draw call grubu, batching'e uygun).
- Normal haritaları OpenGL; Unity ile aynı yön, çevirmeye gerek yok.
- Metallerin Metallic değeri 0,6 (bakır ve çelik). Sahnede Reflection Probe yokken metaller yalnızca gökyüzünü yansıtıp koyu görünüyordu; mutfağa bir Reflection Probe konursa metaller daha doğal görünür.
