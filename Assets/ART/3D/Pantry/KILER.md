# Kiler eşyaları

Süt, krema ve baharat kavanozları. Kurulum: üst menüde **Dishaster > Art > Kiler esyalarini kur** (`Editor/KilerKurulum.cs`). FBX ya da doku yenilenince tekrar çalıştırılır; Nihat'ın prefaba eklediği bileşenler korunur.

## Prefablar (`Prefabs/`)

| Prefab | Ölçü (G × D × Y) | İçinde |
| --- | --- | --- |
| `PF_MilkCarton_01` | 12 × 10 × 25 cm | `Model` (gövde), `Cap` (kapak) |
| `PF_CreamCarton_01` | 9 × 8 × 14 cm | `Model`, `Cap` |
| `PF_SpiceJar_<Baharat>` | Ø17 × 20 cm (kapaklı 26) | `Model` (gövde), `Lid` (ahşap kapak) |

Baharatlar ve halka renkleri: `Thyme` kekik (adaçayı), `ChiliFlakes` pul biber (terrakota), `BlackPepper` karabiber (koyu kahve), `Salt` tuz (tozlu mavi), `Cumin` kimyon (hardal), `Cinnamon` tarçın (bakır). Halka rengi ve etiket baharata sabit; her baharatın kendi malzemesi var (`M_SpiceJar_<Baharat>`), mesh ortak.

- Pivotlar: gövdelerde tabanın ortası. Karton kapağında eğik çatıya oturduğu yerin ortası (açarken orada döner), kavanoz kapağında kendi tabanının ortası (kaldırınca yukarı kalkar).
- Süt ve krema aynı ambalaj, farklı boy: Aşçı siyah-beyaz görüyor, ikisini boydan ayırır. Etiketlerde yazı yok.
- Ölçü kaşığı (`PF_MeasuringSpoon_01`, çanak 11,1 cm) kavanozun ağzından (iç Ø ~13,5 cm) girer.
- Collider ve Rigidbody yok (oyun tarafı ekler).

## Dosyalar

```text
Assets/ART/3D/Pantry/
  SM_MilkCarton_01.fbx, SM_MilkCarton_01_Cap.fbx, SM_CreamCarton_01.fbx, SM_CreamCarton_01_Cap.fbx
  SM_SpiceJar_01.fbx, SM_SpiceJar_01_Lid.fbx      Blender: Game\Scripts\28_export_pantry_unity.py
  Textures/T_Carton_Atlas_BaseColor.png           süt + krema ortak (1024, Repeat: gövde UV'si ön-sol köşede 0-1 dışına taşar)
  Textures/T_SpiceJar_<Baharat>_BaseColor.png     baharat başına (1024, ortak UV)
  Materials/M_Carton_Atlas.mat, M_SpiceJar_<Baharat>.mat   Standard, parlama yok
```

Yeni baharat: `Game\Scripts\27_build_spice_jars.py`'de `SPICES`'a ekle (etiket `01_Concept\Props\Gorseller\etiketler\T_SpiceLabel_<Ad>_v01.jpg`), dokuyu üret, `28_export_pantry_unity.py` ve `KilerKurulum.cs`'deki listeye ekle.
