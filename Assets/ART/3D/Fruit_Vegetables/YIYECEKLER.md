# Kesilebilir yiyecekler

Bu klasör art tarafının (Özgür) teslim ettiği kesilebilir yiyecekleri içerir. Bu dosya kod yazan kişi ve onun yapay zeka asistanı için hazırlandı.

Son güncelleme: 7 Kasım 2026. `Test` alt klasörü eski denemedir, bu sisteme dahil değildir.

## Yiyecekler

| Prefab | FBX | Dilim | Boy (cm) |
| --- | --- | --- | --- |
| `PF_Mushroom_01` | `SM_Mushroom_01.fbx` | 5 (boyuna) | 10 × 10 × 9,7 |
| `PF_Onion_01` | `SM_Onion_01.fbx` | 5 | 9 × 9 × 12,8 |
| `PF_Strawberry_01` | `SM_Strawberry_01.fbx` | 5 | 5,8 × 5,9 × 8,2 |
| `PF_Carrot_01` | `SM_Carrot_01.fbx` | 5 | 9 × 9 × 28,6 |
| `PF_Pepper_01` | `SM_Pepper_01.fbx` | 5 (içi boş halka) | 11 × 11,8 × 16,4 |
| `PF_Tomato_01` | `SM_Tomato_01.fbx` | 5 | 12,9 × 12,3 × 13,9 |
| `PF_Bread_Loaf_01` | `SM_Bread_Loaf_01.fbx` | 10 (uçlar da dilim) | 16 × 25 × 16,5 |
| `PF_Cheese_Block_01` | `SM_Cheese_Block_01.fbx` | 10 | 8,5 × 12,4 × 6 |

## Dosyalar

```text
Assets/ART/3D/Fruit_Vegetables/
  SM_<Ad>.fbx                 bütün hal (SM_<Ad>) + dilimler (SM_<Ad>_Seg01...)
  Textures/T_Food_Atlas_BaseColor.png   sekiz yiyeceğin ortak dokusu (2048)
  Materials/M_Food_Atlas.mat  tek malzeme (Standard, parlama yok)
  Prefabs/PF_<Ad>.prefab
  Editor/YiyecekKurulum.cs    menü: Dishaster > Art > Yiyecekleri kur
```

- Dosya adlarında sürüm eki yok. Art güncellemede FBX ve dokunun üzerine yazar, `.meta` korunur, bağlar kopmaz. Taşıma, yeniden adlandırma.
- Kurulum yeniden çalıştırıldığında prefab silinmez: yalnızca mesh, malzeme ve konumlar güncellenir. Prefaba eklediğin bileşenler (script, collider, rigidbody) korunur. Ama `Whole`, `Slices`, `Slice_NN` adlarını değiştirme; kurulum onları adla bulur.

## Prefab yapısı

```text
PF_Carrot_01        kök; pivot bütün halin tabanının ortasında
  Whole             bütün hal (açık)
  Slices            dilimler (kapalı)
    Slice_01..N     Slice_01 üst uçtaki parça
```

- Her dilimin pivotu kendi ortasında ve kesildiği yerde durur. `Whole` kapatılıp `Slices` açılınca aynı yiyecek dilimlenmiş görünür; dilimler sonra tek tek alınabilir.
- Ölçek metre. Collider, Rigidbody ve kesme davranışı yok; oyun tarafı ekler. Dilimler küçük ve basit, convex MeshCollider uygun.
- Hepsi tek malzeme ve tek doku kullanır (tek draw call grubu, batching'e uygun).
