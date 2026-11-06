# Karakterler ve FPS elleri

Bu klasör art tarafının (Özgür) teslim ettiği oynanabilir karakterleri ve birinci şahıs ellerini içerir. Bu dosya kod yazan kişi ve onun yapay zeka asistanı için hazırlandı: dosyaların yeri, rig yapısı, kodla sürülmesi gereken parçalar ve dokunulmaması gerekenler.

Son güncelleme: 6 Kasım 2026.

## Karakterler

| Klasör | Rol | Manken | Boy | Zorunlu aksesuar (mesh adı) | Rol rengi |
| --- | --- | --- | --- | --- | --- |
| `Cizer` | Dilsiz Çizer: tarifi bilir, konuşamaz | kadın | 1,75 m | ağız bandı `Cizer_MouthBand` | adaçayı `#9AA06A` |
| `Yorumcu` | Sağır Yorumcu: görür, konuşur, duyamaz | erkek | 1,81 m | kulaklık `Yorumcu_Headphones` | hardal `#E4B355` |
| `Asci` | Kör Aşçı: duyar, konuşur, siyah-beyaz görür | tombul erkek | 1,87 m (şapka dahil) | opak gözlük `Asci_Glasses` | tozlu mavi `#A1BBD1` |

Aksesuarlar karakterin parçasıdır: animasyonda çıkmaz, gizlenmemeli.

## Dosyalar

```text
Assets/ART/3D/Karakterler/
  <Karakter>/
    SK_<Karakter>.fbx          karakter (Humanoid)
    SK_<Karakter>_FPHands.fbx  birinci şahıs elleri (Generic)
    Textures/                  T_<Karakter>_<Parça>_BaseColor / _Normal
    Materials/                 M_<Karakter>_<Parça>.mat, M_<Karakter>_FPCap.mat
  Editor/KarakterKurulum.cs    içe aktarma ayarlarını kuran menü
  KARAKTERLER.md               bu dosya
```

- Dosya adlarında sürüm eki yok. Art güncelleme yaptığında FBX'in ve dokuların üzerine yazar, `.meta` korunur, sahne ve prefab bağları kopmaz. Bu dosyaları taşıma ve yeniden adlandırma.
- İçe aktarma ayarlarını Inspector'dan elle değiştirme. Gereken ayar `KarakterKurulum.cs` içinde. Menü: **Dishaster > Art > Karakterleri kur**. Art çalıştırır, sonuç depoya girer; FBX yenilenince tekrar çalıştırılır.
- Kaynak dosyalar (Blender) art tarafındadır. Mesh, rig ya da doku değişikliği gerekirse art'a söyle; FBX'i Unity tarafında düzenleme.

## Ortak teknik bilgi

- Ölçek metre, pivot ayak tabanının ortasında. Karakter +Z yönüne bakar.
- Render: projedeki gibi Built-in, Standard shader. Parlama istenmiyor: smoothness 0, metallic 0, speküler ve yansıma kapalı. Normal haritaları OpenGL (Unity'nin yönü).
- Dokular yalnızca BaseColor ve Normal.

## Karakter: `SK_<Karakter>.fbx`

### Rig

- 70 kemik, üç karakterde aynı ad ve hiyerarşi. Mixamo adlandırması. FBX'te rig nesnesinin adı `Armature`.
- Hiyerarşi:
  - `Root` > `Hips` > `Spine` > `Spine1` > `Spine2` > `Neck` > `Head` > `HeadTop_End`, `Eye_L`, `Eye_R`, `Lid_L`, `Lid_R`
  - `Spine2` > `LeftShoulder` > `LeftArm` > `LeftForeArm` > `LeftHand` > parmaklar (sağ taraf aynı)
  - Parmaklar: `LeftHandThumb1..4`, `LeftHandIndex1..4`, `LeftHandMiddle1..4`, `LeftHandRing1..4`, `LeftHandPinky1..4`. 4 numara uç kemiğidir, ağırlığı yoktur.
  - `Hips` > `LeftUpLeg` > `LeftLeg` > `LeftFoot` > `LeftToeBase` > `LeftToe_End` (sağ aynı)
- **Humanoid avatar.** Unity'nin otomatik eşlemesi `Eye_L`'yi Jaw yuvasına koyup avatarı geçersiz yapıyor. Kurulum Jaw eşlemesini siler; çene kemiği yok. Avatarı Configure ekranından yeniden otomatik eşlersen bu sorun geri gelir.
- Ağırlıklar normalize, nokta başına en çok 4 kemik. A-poz.

### Mesh parçaları (SkinnedMeshRenderer adları)

- Gövde parçalara bölünmüştür. Yalnızca görünen parçalar FBX'tedir: `BODY_Head`, `BODY_ArmsHands`, Çizer'de ayrıca `BODY_Ankle`.
- Kıyafet `<Karakter>_Jacket`, `_Trousers`, `_Shoe(s)`, ayrıca `Asci_ChefHat` ve `Yorumcu_Bandana`.
- Gözler Çizer'de `Cizer_Eyes`, Aşçı'da `Asci_Eyes` adlı ayrı nesnelerde; Yorumcu'da `BODY_Head` içinde.

### Göz kırpma (kodla sürülür)

- `Lid_L` ve `Lid_R` kemikleri. Humanoid animasyonlar bu kemikleri taşımaz.
- Kemik, kendi dinlenme dönüşünden başlayarak yerel X ekseni etrafında döner.
- Tam kapanma açısı Çizer ve Aşçı için **−90°**, Yorumcu için **−75°**. Bu değerler Unity'de denendi.

```csharp
// rest = Lid_L.localRotation (Awake'te saklanır), blink 0..1
lidL.localRotation = rest * Quaternion.AngleAxis(lidAngle * blink, Vector3.right);  // lidAngle: -90 ya da -75
```

Animator kapakları ezmesin diye `LateUpdate` içinde yazılmalı.

### Ağız ve kaş (blend shape)

`BODY_Head` üzerinde: `Viseme_MBP`, `Viseme_OO`, `Viseme_EE`, `Brow_Angry`. Aşçı'da aynı adlar `BODY_ArmsHands` üzerinde de var; ada göre bul, ikisine de yaz.

**Kapalı ağız kuralı:** Varsayılan ağız kapalıdır, yani `Viseme_MBP = 100`.

- Konuşurken MBP = 1 − açıklık, OO = açıklık.
- MBP + OO + EE toplamı 1'i (Unity'de 100'ü) geçmemeli. MBP ile açık ağız aynı anda tam açılırsa mesh bozulur.
- FBX'te bütün değerler 0 gelir, yani ağız ilk karede açık görünür. Kapalı ağzı kod kurmalı.

```csharp
float oo = Mathf.Clamp01(openness), ee = 0f;            // ses sistemi tek "açıklık" değeri verir
head.SetBlendShapeWeight(iMBP, (1f - oo - ee) * 100f);
head.SetBlendShapeWeight(iOO, oo * 100f);
head.SetBlendShapeWeight(iEE, ee * 100f);
```

- **Çizer konuşmaz.** Ağzı bantlı; OO ve EE hep 0, MBP 100 kalır.
- `Brow_Angry` 0–100, serbest kullanılır.

### Klip

`WeightTest` (Çizer ve Aşçı): ağırlık kontrolü için test pozları. Oyunda kullanılmaz.

## FPS elleri: `SK_<Karakter>_FPHands.fbx`

Birinci şahıs kamerasında görünen, gövdeden kopuk eller. Bilekten 3 cm geride kesilmiştir, bilek ağzı rol renginde düz bir kapakla kapalıdır. Ten, karakterin kendi elidir.

- **Rig:** Generic. `Armature` > `Root` > `LeftForeArm` > `LeftHand` > parmaklar (sağ aynı), 45 kemik.
  - Kemik adları ve eksenleri karakterle birebir aynı. Karakter için yazılan parmak pozları (sayma emote'ları gibi) ellere de aynen uygulanabilir.
  - Ağırlıklar art tarafında düzeltildi, iki el simetrik.
- **Malzeme:** Ten için karakterin `M_<Karakter>_Body.mat`'ı kullanılır (karakterle ortak). Kapak için `M_<Karakter>_FPCap.mat`.
- **Üçgen sayısı (iki el birlikte):** Çizer 5.312, Yorumcu 5.260, Aşçı 7.034.
- **Dinlenme pozu:** Ellerin karakterin A-pozundaki yeri, yani kalça hizasında. Ekranda görünmesi için kameraya göre poz verilmelidir.

### Kameraya yerleştirme ve `FP_Poses` klibi

Klip, göz noktası el modelinin yerel koordinatında `(0, 1.60, 0.06)` olacak şekilde kuruldu. Eller şöyle yerleştirilir:

```csharp
hands.transform.SetParent(cameraTransform, false);
hands.transform.localRotation = Quaternion.identity;
hands.transform.localPosition = new Vector3(0f, -1.60f, -0.06f);
clip.SampleAnimation(hands, frame / 24f);   // ya da Animator ile
```

Kareler (24 fps, kareler arası geçiş yok):

| Kare | Poz |
| --- | --- |
| 0 | dinlenme (A-poz, ekranda görünmez) |
| 1 | açık, el sırtı yukarıda |
| 2 | yumruk |
| 3–7 | parmakla sayma 1, 2, 3, 4, 5 |
| 8 | tutma (yarı kapalı) |
| 9 | avuç yukarı |

**Doğrulama durumu:**
- Bu pozlar Blender'da ve FBX'i Blender'a geri alarak kontrol edildi.
- Bu projede (Built-in) henüz denenmedi. Ayrı bir URP test projesinde aynı FBX'ler için kurulum yazıldı, sonuç da henüz raporlanmadı.
- Blender'ın FBX içe aktarıcısında kareler bir kayıyordu. Unity'de klip bir kare kayık görünürse kare numarasına ±1 ekle.

Bu klip yalnızca başlangıç içindir. Oyundaki tutma, karıştırma ve uzanma animasyonları ayrıca yapılacak; elin yeri ve boyutu oyun testinde ayarlanabilir.

### Notlar

- Kör Aşçı'nın siyah-beyaz görüşü ellere de uygulanır.
- Kamera duvara yaklaşınca ellerin içine girmemesi için ayrı kamera katmanı ya da benzeri bir çözüm gerekebilir; seçim kod tarafının.
- Ayaklar şimdilik yok (karar: yalnızca eller).

## Güncelleme akışı

1. Art, Blender'da değişiklik yapar.
2. Art FBX'i yeniden üretip bu klasördeki dosyanın üzerine yazar. Dosya adı aynı kalır.
3. Gerekirse art **Dishaster > Art > Karakterleri kur** menüsünü çalıştırır ve commit eder.
4. Kod tarafı pull eder. Ek bir adım gerekmez.
