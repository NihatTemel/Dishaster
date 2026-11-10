# Oda parçaları

Duvar, köşe, zemin, kapı ve pencere. Kurulum: üst menüde **Dishaster > Art > Oda parcalarini kur** (`Editor/OdaKurulum.cs`). Önce **Mobilyalari kur** çalışmış olmalı (kapı ve pencere mobilya ahşabını, `M_Furniture_Trim`, kullanır). FBX ya da doku yenilenince tekrar çalıştırılır; prefaba eklenen bileşenler ve seçili renk korunur.

## Prefablar (`Prefabs/`)

| Prefab | Ölçü | İçinde |
| --- | --- | --- |
| `PF_Wall_100`, `PF_Wall_050` | 100 / 50 cm genişlik, 280 cm yükseklik, 15 cm kalınlık | `Model` (duvar + 10 cm süpürgelik) |
| `PF_Wall_Corner` | 15 × 15 cm | iki duvarın birleştiği köşedeki boşluğu doldurur |
| `PF_Wall_Window_200` | 200 cm | `Model`, `Pencere` (sabit çerçeve), `Kanat_Sol`, `Kanat_Sag`; pencere 120 × 110 cm, alt kenarı yerden 110 cm |
| `PF_Wall_Door_150` | 150 cm | `Model`, `Kapi_Cercevesi`, `Kapi` (kanat); boşluk 110 × 230 cm |
| `PF_Floor_100` | 100 × 100 cm | `Model`; 50 cm'lik dama |
| `PF_Lamp_Pendant_01` | Ø45 cm kubbe, gövde 35,4 cm + kablo | `Model`, `Kablo`, `Isik` (Point Light); kökte `Lamba` |

Her prefabın kökünde `OdaRengi` var (yalnızca duvar boyası ve zeminin renkli kareleri değişir):

- Duvar: `Krem` (varsayılan), `Bej`, `Adacayi`, `TozluMavi`, `Hardal`, `Terrakota`. Süpürgelik hep ahşap.
- Zemin: `Bej` (varsayılan), `Adacayi`, `TozluMavi`, `GriMavi`, `Hardal`, `Terrakota`. Damanın öteki karesi hep krem.
- Bütün odayı birden boyamak için rengi `Prefabs` klasöründeki prefabın kendisinde değiştir; sahnedeki kopyalar izler. Tek bir duvarı farklı yapmak için sahnedeki kopyada değiştir.
- Kapı ve pencere renk seçiminden etkilenmez.

## Sarkıt lamba (`PF_Lamp_Pendant_01`)

- Kubbe mat hardal cam gibi: tepede hardal tonunda, alt ağza doğru sıcak beyaz parlar (emission dokusu). Bakır duy ve kablo mobilya ahşap/bakır malzemesinden (`M_Furniture_Trim`).
- Pivot: kablonun duya bağlandığı nokta (kubbenin alt ağzından 35 cm yukarı). Kök y = 2,49'a konunca kubbenin alt ağzı yerden ~2,15 m'de, karakterlerin başının üstünde kalır.
- `Kablo`: ayrı çocuk, pivotu alt ucunda. Boyu `localScale.y` ile ayarlanır: ölçek 1 = 30,7 cm (tavan 2,8 m, kök 2,49 m). Lamba daha aşağı sarkacaksa kökü indir, kablonun ölçeğini (tavan − kök) / 0,307 yap. Kurulum tekrar çalışınca kablo ölçeği korunur.
- `Isik`: Point Light, kökten 39 cm aşağıda (ışık diskinin hemen altı), renk #FFE2B8, intensity 1,5, range 7, yumuşak gölge. İlk kurulumdan sonra sahnede ayarlanan değerler korunur. Birden çok lamba varsa gölge maliyetine dikkat (gerekirse bazılarında gölge kapatılır).
- `Lamba` bileşeni: `acik` (ışık ve kubbe parlaması birlikte), `parlaklik` (kubbenin emission çarpanı, varsayılan 1,5). Kodla: alanı değiştir, `Uygula()` çağır. Her lamba kendi parlaklığını taşır (malzeme ortak).
- Önerilen yer: adanın üstüne 1 m arayla iki tane.

## Ölçü ve pivot

- Duvar pivotu: **odaya bakan yüzün sol-alt köşesi, yerde**. Oda yüzü pivotun önüne (+Z) bakar, kalınlık arkaya (−Z) gider. Dolapların pivotu da sol-arka köşe olduğu için duvarın önüne konan dolap tam yaslanır. 50 cm ızgaraya oturur; yan duvar için prefabı Y'de 90° çevir.
- Köşe: iki duvar bir köşede birleşince arkada kalan 15 × 15 cm'lik boşluğu kapatır; köşe noktasına konur.
- Zemin: üst yüz y = 0, pivot arka-sol köşe (mobilyayla aynı), 1 m adımla dizilir.

## Açılan parçalar (oyun tarafı için)

Değerler parçanın kendi yerel uzayında; duvar döndürülmüşse onunla birlikte döner.

| Parça | Hareket | Değer |
| --- | --- | --- |
| `Kapi` | `localEulerAngles.y` 0 → **−90** (odaya doğru açılır) | menteşe sağ kenarda |
| `Kanat_Sol` | `localEulerAngles.y` 0 → **+70** | menteşe sol dış kenarda |
| `Kanat_Sag` | `localEulerAngles.y` 0 → **−70** | menteşe sağ dış kenarda, kulp bu kanatta |

Açılma yönleri Blender'da denendi, Unity'de denenmedi; ters açılırsa işareti çevir. Collider yok (oyun tarafı ekler).

## Dosyalar

```text
Assets/ART/3D/Room/
  SM_<Ad>.fbx                       Blender: Game\Scripts\30_export_room_unity.py (03_Blender\Assets\room\Room_vNN.blend)
  Textures/T_Room_Wall_BaseColor_<Renk>.png    (1024, Repeat: duvar UV'si duvar boyunca metre)
  Textures/T_Room_Floor_BaseColor_<Renk>.png   (1024, Clamp)
  Textures/T_Lamp_Shade_{BaseColor,Emission}.png   (512, lamba kubbesi)
  Materials/M_Room_Wall_<Renk>, M_Room_Floor_<Renk>, M_Room_Glass   Standard, parlama yok (camda hafif)
  Scripts/OdaRengi.cs               renk seçimi (oyunda da çalışır)
  Scripts/Lamba.cs                  lamba aç / kapa
  Editor/OdaKurulum.cs              menü
```

Yeni renk: `Game\Scripts\29_build_room_textures.py`'de renk listesine ekle, dokuyu üret, `30_export_room_unity.py`, `OdaRengi.Renk` ve `OdaKurulum.Renkler`'e ekle.
