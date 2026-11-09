# Mutfak mobilyası

Bu klasör art tarafının (Özgür) teslim ettiği mutfak mobilyasını içerir. Bu dosya kod yazan kişi (Nihat) ve onun yapay zeka asistanı için hazırlandı: neyin nerede olduğu, prefabların nasıl kullanılacağı, kapak ve çekmecenin oyunda nasıl açılacağı.

Son güncelleme: 9 Kasım 2026.

## Kısaca

- Mutfak **hazır modüllerle** kurulur: bir dolap prefabını sahneye sürükle, **inspector'dan** tezgah, ön yüz (kapak / çekmece / açık), kulp, raf ve renk seç. Alt nesneleri elle açıp kapatmaya gerek yok.
- Bütün mobilya **tek trim sheet malzemesi** kullanır (`M_Furniture_Trim`, Built-in Standard). Renk temaları aynı UV ile ayrı malzemelerdir.
- Prefablar yalnızca **görsel**: collider, Rigidbody, network ya da etkileşim script'i yok. Kapak ve çekmece hiyerarşisi açılmaya hazır (pivotlar menteşede); açma mantığını oyun tarafı yazar.
- Ölçek metre, 50 cm ızgara. Ön yüz **+Z**'ye bakar.

## Kurulum ve güncelleme

- Menü: **Dishaster > Art > Mobilyalari kur** (`Editor/MobilyaKurulum.cs`). Art FBX ya da doku yenilediğinde çalıştırılır; sonuç (prefab, malzeme, `.meta`) depoya girer, ayrıca çalıştırman gerekmez.
- Yeniden çalışınca prefablar silinmez: mesh, malzeme ve parça yerleri güncellenir; prefaba eklediğin bileşenler (collider, script) ve inspector seçenekleri korunur.
- **Dikkat:** kurulum parça yerlerini kendi değerlerine geri koyar. Prefab içinde bir parçanın yerini elle değiştirirsen değeri `MobilyaKurulum.cs`'e de yaz (ya da Özgür'e söyle), yoksa bir sonraki kurulumda geri döner.
- Kurulum, modüllere taşınmış eski prefabları siler (`PF_Dolap_Cekmeceli_100`, `PF_Kose_070`, eski tek parçalar).
- Alt nesne adlarını değiştirme (`Govde`, `Kapak_Sol`, `Kulp`, `Kat_1` …); kurulum ve modül script'leri onları adla ya da referansla bulur.

## Prefablar (`Prefabs/`)

| Prefab | Bileşen | Ölçü (G × D × Y) | Ne seçilir |
| --- | --- | --- | --- |
| `PF_Dolap_100` | `MutfakModulu` | 100 × 70 × 90 cm | tezgah ucu; iki kapak / tek kapak (menteşe sol-sağ) / açık / tek çekmece (altı açık raf) / iki çekmece; kulp; ayraç; raf |
| `PF_Dolap_050` | `MutfakModulu` | 50 × 70 × 90 cm | tezgah ucu; tek kapak (menteşe sol-sağ) / açık; kulp |
| `PF_Evye_100` | `MutfakModulu` | 100 × 70 × 90 cm | lavabo, musluk ve iki vana sabit; iki kapak / tek / açık; kulp (çıkıntılı tezgah yok) |
| `PF_Dolgu_025` | `MutfakModulu` | 25 × 70 × 90 cm | yalnızca tezgah ucu (ara dolgu) |
| `PF_Duvar_Dolabi_100` | `MutfakModulu` | 100 × 70 × 80 cm, yerden 160–240 cm | iki kapak / tek / açık; kulp (kapağın altına yakın); raf |
| `PF_Kose_L` | `KoseModulu` | 120 ya da 170 cm her kol | arka kol ve yan kol boyu (50 / 100'lük dolap); tezgahın boş uçları |
| `PF_Duvar_Rafi_150` | `RafModulu` | 150 × 54 cm | kat sayısı 1–4, ilk kat yüksekliği, kat arası |
| `PF_Ada_200` | `AdaModulu` | 200 × 150 × 90 cm | ön ve arka sırada ikişer 100'lük yuva: her biri `Kapali` (düz panel) ya da `Dolap` |

Dolap, L köşe, ada ve duvar dolabının kökünde ayrıca `MobilyaRengi` var (renk teması).

`Prefabs/Parcalar/` yalnızca hiçbir modülde olmayan küçük ada parçalarını tutar: `PF_Island_Top_050/100`, `PF_Island_Carcass_050` (her biri kök + `Model`).

### `AdaModulu` (`PF_Ada_200`)

- 200 × 150 cm tezgah (`Tezgah`, her kenarı yuvarlak), altında sırt sırta iki sıra: ön sıra +Z'ye, arka sıra −Z'ye bakar. Tezgah önde ve arkada 5 cm taşar, yanlarda gövdeyle hizalı (gövdeler 2 × 1,4 m).
- `onSol`, `onSag`, `arkaSol`, `arkaSag` (sol/sağ: önden bakınca): `Kapali` = düz panel (`*_Kapali`), `Dolap` = `PF_Dolap_100` kopyası (`*_Dolap`, iç içe prefab, tezgahı kapalı; kapak/çekmece ayarı için o dolabı seç). Varsayılan: ön sıra dolap, arka sıra kapalı.
- Pivot: tezgahın sol-arka-alt köşesi, yerde. Arka sıradaki dolaplar 180° döndürülmüş; kapak ve çekmece yerel değerleri aynı geçerli.

### `MutfakModulu` (düz dolaplar, lavabo, duvar dolabı)

| Alan | Değerler |
| --- | --- |
| `tezgah` | `Yok`, `Duz`, `SolCikinti`, `SagCikinti`, `IkiCikinti`. Çıkıntı: tezgahın o ucu 5 cm taşar (sıra sonu ya da tek başına duran dolap). Yanına başka dolap gelecek uç `Duz` kalmalı ki tezgahlar ek yeri olmadan birleşsin. |
| `onYuz` | `Bos`, `IkiKapak`, `SolMenteseKapak`, `SagMenteseKapak`, `Cekmece`, `IkiCekmece` |
| `kulp`, `ayrac`, `raf` | bool |

- Özel inspector (`Editor/MutfakModuluEditor.cs`) listelerde yalnızca o dolapta gerçekten olan seçenekleri gösterir. Parça referansları kapalı "Parcalar" bölümünde (kurulum doldurur).
- `PF_Dolap_100`'de `Cekmece` seçilince gövde `Govde_Cekmeceli`'ye geçer (üstte çekmece boşluğu, altı açık raf; raf seçilebilir). `IkiCekmece` düz gövdeyle `Cekmece` + `Cekmece_Alt` gösterir. Çekmece seçiliyken ayraç kapalı.
- Olmayan bir seçenek kodla verilirse en yakını gösterilir ve konsola uyarı düşer.
- Kod ile: alanı değiştir, `Uygula()` çağır. Editörde OnValidate kendiliğinden uygular.

Hiyerarşi (`PF_Dolap_100`):

```text
PF_Dolap_100              MutfakModulu, MobilyaRengi   pivot: sol-arka-alt köşe, yerde
  Govde                   kapaklı/açık gövde
  Govde_Cekmeceli         tek çekmece seçilince
  Tezgah_Duz / _SolCikinti / _SagCikinti / _IkiCikinti
  Kapak_Sol               pivot menteşede (sol kenar)
    Kulp
  Kapak_Sag               pivot menteşede (sağ kenar)
    Kulp
  Cekmece
    Kulp
  Cekmece_Alt
    Kulp
  Ayrac, Raf
```

`PF_Evye_100` ek olarak `Lavabo`, `Musluk`, `Vana_Sol`, `Vana_Sag` taşır (vanalar kendi ortasında, Y ekseninde döner).

### `KoseModulu` (`PF_Kose_L`)

- Köşe **sağ-arkada**: arka kol arka duvar boyunca sola (−X), yan kol sağ duvar boyunca öne (+Z) uzanır. Başka bir köşe için prefabı Y'de 90° çevir.
- `arkaKol`, `yanKol`: `Kisa50` (70 cm kör köşe + 50'lik dolap = 120 cm) ya da `Uzun100` (+100'lük dolap = 170 cm).
- `tezgahUclari`: `Duz`, `ArkaKolCikinti`, `YanKolCikinti`, `IkiUcCikinti` (kolların boş ucu 5 cm taşar).
- İçinde: `Govde_Kose` (kör köşe, 70 × 70, önü yan kolun dolabıyla kapalı), `Arka_Dolap_050/100`, `Yan_Dolap_050/100` (normal `PF_Dolap_050/100` kopyaları, iç içe prefab, tezgahları kapalı), 16 L tezgah (`Tezgah_<arka>x<yan>[_EndL|_EndR|_EndLR]`, cm).
- Kollardaki dolapların kapak, çekmece ve kulp ayarı için hiyerarşide o dolabı seç; kendi `MutfakModulu`'su var. Yerlerini ve aktifliğini `KoseModulu` yönetir, elle taşıma.
- Köşedeki 50'lik dolaplarda varsayılan `SagMenteseKapak`: kulp iç köşeden uzakta kalır (iki kolun kulpları köşede çarpışıyordu). Bu kapak menteşesi köşe tarafında olduğu için tam açılınca öbür kolun kapağına değebilir; açılma açısını ~80°'de sınırlamak yeterli.
- İç köşede iki kolun kapakları 3 mm üst üste biniyor (bevel içinde, görünmüyor).

### `RafModulu` (`PF_Duvar_Rafi_150`)

- `katSayisi` 1–4, `ilkKatYuksekligi` (en alt rafın alt yüzü, yerden; varsayılan 1,4 m), `katArasi` 0,3–0,6 m (0,40'ta direkler üst üste tek dikme gibi birleşir).
- Her kat (`Kat_1` … `Kat_4`): 150 × 35 × 10 cm ahşap raf + iki çelik tutacak (`Direk_Sol/Sag` duvarda, `Kol_Sol/Sag` rafın altında; kolun ucundaki çıkıntı rafın önünü tutar).
- Pivot: duvar çizgisinde, rafın sol ucu, yerde. Raf duvardan 10 cm önde; toplam derinlik 54 cm.

### `MobilyaRengi`

- `renk`: `Krem` (varsayılan), `Ahsap`, `Adacayi`, `TozluMavi`, `Hardal`, `Terrakota`. Hepsi oyun paletinden; yalnızca krem boyalı yerler değişir (gövde, kapak, çekmece kutusu); tezgah ahşabı, bakır kulp, çelik lavabo aynı kalır.
- Her renk ayrı malzeme: `Materials/M_Furniture_Trim_<Renk>.mat` (`Krem` = `M_Furniture_Trim`). Aynı renkteki dolaplar aynı malzemeyi paylaşır (batching).
- L köşede kökteki renk içteki dolapları da boyar.

## Oyunda açma kapama (oyun tarafı için)

Bütün değerler **modülün kendi yerel uzayında** (kök döndürülmüşse onunla birlikte döner). L köşedeki dolaplar kendi kökleriyle döndürülmüş durumda; aynı yerel değerler orada da geçerli.

| Parça | Hareket | Değer |
| --- | --- | --- |
| `Kapak_Sol` | `localEulerAngles.y` 0 → **+90** (dışa açılır) | ~90–100°; L köşede menteşesi köşe tarafındaki kapak için ~80° |
| `Kapak_Sag` | `localEulerAngles.y` 0 → **−90** | aynı |
| `Cekmece`, `Cekmece_Alt` | `localPosition.z` artar (öne kayar) | 0,40 m güvenli (kutu 65 cm derin) |
| `Vana_Sol`, `Vana_Sag` | `localEulerAngles.y` | serbest |

- Açılma yönleri (+90 / −90) mesh yönünden hesaplandı, Unity'de dönerek denenmedi; ters açılırsa işareti çevir.
- Kulplar kapağın / çekmecenin çocuğu: onlarla birlikte hareket eder.
- Kapak 45 × 60 × 5 cm (bilerek küçük), alt dolapta yerden 15–75 cm, duvar dolabında 170–230 cm.
- Collider önerisi: gövdeye (ya da kök) BoxCollider, her kapak ve çekmeceye kendi BoxCollider'ı (açılınca onlarla döner/kayar). Tezgah üstü yürüme/koyma yüzeyi 90 cm'de.
- Inspector seçenekleri (tezgah, ön yüz, renk) sahne kurulurken ayarlanmak için. Oyun sırasında değiştirilecekse (ör. Mirror ile) `Uygula()` her istemcide çağrılmalı; seçenekler kendiliğinden senkron olmaz.

## Ölçü ve pivot

- Gövde ve tezgah pivotu: **sol-arka-alt köşe, yerde** (önden bakınca sol). Dolap pivotundan **−X** yönüne uzanır, önü **+Z**. Unity'de ızgara adımını 0,5 (ya da 0,25) yap; dolaplar yan yana tık tık oturur.
- Gövde 80 cm + tezgah 10 cm = 90 cm. Gövde derinliği 70, tezgah 75 cm (öne 5 cm taşar).
- Aletler (Tools) Aşçı'nın eline göre ~1,5 kat büyük; mobilya ölçüleri buna göre (derin tezgah, iri kulp: top 5 cm, 7 cm dışarı çıkar).
- Tezgah ahşabı araç ahşabından bilerek koyu (Aşçı siyah-beyaz görüyor; tahta tezgahta kaybolmasın).

## Dosyalar

```text
Assets/ART/3D/Furniture/
  SM_<Ad>.fbx                    tek parça, tek nesne, pivot nesnenin orijini (Blender: Game\Scripts\25_export_furniture_unity.py)
  Textures/T_Furniture_Trim_BaseColor[_<Renk>].png
  Textures/T_Furniture_Trim_{Normal,MetallicSmoothness}[_Ahsap].png   (2048, trim sheet, Repeat; renkli temalar kreminkini kullanır)
  Materials/M_Furniture_Trim[_<Renk>].mat    Standard (Metallic)
  Prefabs/PF_*.prefab            modüller
  Prefabs/Parcalar/PF_*.prefab   ada parçaları
  Scripts/MutfakModulu.cs, KoseModulu.cs, RafModulu.cs, AdaModulu.cs, MobilyaRengi.cs   (oyunda da çalışır)
  Editor/MobilyaKurulum.cs       menü
  Editor/MutfakModuluEditor.cs   sade inspector
```

- Dosya adlarında sürüm eki yok; art güncellemede üzerine yazar, `.meta` korunur, referanslar kopmaz. Dosyaları taşıma ya da yeniden adlandırma.
- Normal haritası OpenGL (Unity ile aynı). Metallerin Metallic değeri 0,6 (araçlarla aynı); mutfağa bir Reflection Probe konursa bakır ve çelik daha doğal görünür.
- Trim sheet: parçaların UV'si şeritlerin içinde ve yatayda 0–1 dışına taşar; dokular **Repeat** olmalı (Clamp olursa ahşap çizgi gibi uzar).
