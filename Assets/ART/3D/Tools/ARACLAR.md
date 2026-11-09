# Araç gereçler

Bu klasör art tarafının (Özgür) teslim ettiği mutfak araç gereçlerini içerir. Bu dosya kod yazan kişi ve onun yapay zeka asistanı için hazırlandı.

Son güncelleme: 9 Kasım 2026.

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

## Dosyalar

```text
Assets/ART/3D/Tools/
  SM_<Ad>.fbx                 tek alet, tek nesne
  Textures/T_Cookware_Atlas_{BaseColor,MetallicSmoothness,Normal}.png   (2048)
  Textures/T_Utensil_Atlas_{BaseColor,MetallicSmoothness,Normal}.png    (2048)
  Materials/M_Cookware_Atlas.mat, M_Utensil_Atlas.mat   Standard (Metallic)
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
