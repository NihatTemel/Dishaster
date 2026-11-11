// Manav reyonu ve tabak yigini (Ozgur / art). Ust menude: Dishaster > Art > Manav reyonu ve tabak yiginini kur.
// Yalnizca gorsel prefab uretir; alinabilirlik oyun tarafinda (Nihat): her kasanin ustunde bos bir "Al_<Malzeme>"
// noktasi var, Asci kasaya uzaninca oyun kodu orada alinabilir yeni bir yiyecek olusturur (kasa bosalmaz).
// Tekrar calistirilinca prefab bastan kurulur (icerik art'in; prefaba eklenen bilesenler korunmaz, kokteki haric).
//
// PF_Manav_Reyonu (Room/Prefabs): 2 sira x 3 kasa. On sira yerde, arka sira bos birer kasanin ustunde (30 cm).
//   Kasalar ice bakan taraf gorunsun diye one 10 derece egik. Pivot: arka kenarin ortasi, yerde; onu +Z.
//   Olcu ~1,9 m genislik, ~0,9 m derinlik, ~0,65 m yukseklik.
//   On sira (soldan): Domates, Biber, Havuc. Arka sira: Sogan, Mantar, Cilek.
// PF_Plate_Stack_06 (Room/Prefabs): 6 servis tabagi ust uste (ic ice). Pivot tabanin ortasi.
using System.IO;
using UnityEditor;
using UnityEngine;

public static class DekorKurulum
{
    const string Root = "Assets/ART/3D/Room/Prefabs";
    const string CratePath = "Assets/ART/3D/Furniture/Prefabs/Parcalar/PF_VeggieCrate_01.prefab";
    const string FoodDir = "Assets/ART/3D/Fruit_Vegetables/Prefabs";
    const string PlatePath = "Assets/ART/3D/Tools/Prefabs/PF_Plate_Serving_01.prefab";

    // Kasa (Blender'dan olculdu): 60 x 40 x 30 cm, ic taban 5,3 cm, ic alan ~52 x 30 cm
    const float CrateW = 0.60f, CrateD = 0.40f, CrateH = 0.30f, Floor = 0.053f;
    const float InnerW = 0.52f, InnerD = 0.30f;
    const float Tilt = 10f, Gap = 0.05f;

    // (malzeme adi, prefab, cap, boy) -- cap/boy olculdu (m)
    static readonly (string ad, string prefab, float cap, float boy)[] On =
    {
        ("Domates", "PF_Tomato_01", 0.128f, 0.139f),
        ("Biber", "PF_Pepper_01", 0.13f, 0.18f),
        ("Havuc", "PF_Carrot_01", 0.10f, 0.315f),
    };
    static readonly (string ad, string prefab, float cap, float boy)[] Arka =
    {
        ("Sogan", "PF_Onion_01", 0.118f, 0.166f),
        ("Mantar", "PF_Mushroom_01", 0.12f, 0.117f),
        ("Cilek", "PF_Strawberry_01", 0.076f, 0.107f),
    };

    [MenuItem("Dishaster/Art/Manav reyonu ve tabak yiginini kur")]
    public static void Run()
    {
        Directory.CreateDirectory(Root);
        Reyon();
        TabakYigini(6);
        AssetDatabase.SaveAssets();
        Debug.Log("Dekor kurulum: PF_Manav_Reyonu, PF_Plate_Stack_06");
    }

    static void Reyon()
    {
        var crate = AssetDatabase.LoadAssetAtPath<GameObject>(CratePath);
        if (!crate) { Debug.LogWarning("Dekor kurulum: PF_VeggieCrate_01 yok (Mobilyalari kur)"); return; }
        string path = $"{Root}/PF_Manav_Reyonu.prefab";
        var root = new GameObject("PF_Manav_Reyonu");
        try
        {
            var rnd = new System.Random(11);
            float step = CrateW + Gap;
            // arka sira: kasalarin arkasi pivotta (z = 0), on sira bir kasa derinligi onde
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * step;
                // arka: bos taban kasasi + ustte dolu kasa
                var taban = (GameObject)PrefabUtility.InstantiatePrefab(crate, root.transform);
                taban.name = $"Taban_{i + 1}";
                taban.transform.localPosition = new Vector3(x, 0f, CrateD / 2f);
                Kasa(root.transform, crate, Arka[i], new Vector3(x, CrateH, CrateD / 2f), rnd);
                Kasa(root.transform, crate, On[i], new Vector3(x, 0f, CrateD * 1.5f + Gap), rnd);
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { Object.DestroyImmediate(root); }
    }

    // Egik kasa + ici dolu + alma noktasi. pos: kasanin taban ortasi (egilmeden once)
    static void Kasa(Transform parent, GameObject crate, (string ad, string prefab, float cap, float boy) y, Vector3 pos, System.Random rnd)
    {
        var g = new GameObject($"Kasa_{y.ad}").transform;
        g.SetParent(parent, false);
        // one egik: on kenar asagi iner; on alt kenar yere girmesin diye biraz kaldirilir
        g.localPosition = pos + new Vector3(0f, Mathf.Sin(Tilt * Mathf.Deg2Rad) * CrateD / 2f, 0f);
        g.localRotation = Quaternion.Euler(Tilt, 0f, 0f);
        var k = (GameObject)PrefabUtility.InstantiatePrefab(crate, g);
        k.name = "Kasa";

        var food = AssetDatabase.LoadAssetAtPath<GameObject>($"{FoodDir}/{y.prefab}.prefab");
        if (!food) { Debug.LogWarning($"Dekor kurulum: {y.prefab} yok"); return; }
        var ic = new GameObject("Ic").transform; ic.SetParent(g, false);

        if (y.ad == "Havuc") Havuclar(ic, food, y, rnd);
        else
        {
            // alt kat: izgara; ust kat: aralarin ortasina, biraz asagi gomulu (yigin)
            int nx = Mathf.Max(1, Mathf.FloorToInt(InnerW / y.cap));
            int nz = Mathf.Max(1, Mathf.FloorToInt(InnerD / y.cap));
            float sx = InnerW / nx, sz = InnerD / nz;
            for (int ix = 0; ix < nx; ix++)
                for (int iz = 0; iz < nz; iz++)
                    Koy(ic, food, new Vector3(-InnerW / 2f + sx * (ix + 0.5f), Floor, -InnerD / 2f + sz * (iz + 0.5f)), rnd);
            for (int ix = 0; ix < nx - 1; ix++)
                for (int iz = 0; iz < Mathf.Max(1, nz - 1); iz++)
                    Koy(ic, food, new Vector3(-InnerW / 2f + sx * (ix + 1f), Floor + y.boy * 0.55f,
                                              nz > 1 ? -InnerD / 2f + sz * (iz + 1f) : 0f), rnd);
        }

        var al = new GameObject($"Al_{y.ad}").transform;   // oyun kodu icin: yiyecegin elde olusacagi yer
        al.SetParent(g, false);
        al.localPosition = new Vector3(0f, CrateH + 0.05f, 0f);
    }

    static void Koy(Transform parent, GameObject food, Vector3 pos, System.Random rnd)
    {
        var f = (GameObject)PrefabUtility.InstantiatePrefab(food, parent);
        float j = 0.012f;
        f.transform.localPosition = pos + new Vector3(((float)rnd.NextDouble() - 0.5f) * j, 0f, ((float)rnd.NextDouble() - 0.5f) * j);
        f.transform.localRotation = Quaternion.Euler(((float)rnd.NextDouble() - 0.5f) * 16f, (float)rnd.NextDouble() * 360f, ((float)rnd.NextDouble() - 0.5f) * 16f);
    }

    // Havuc yatik: boyu X boyunca. Pivot havucun tabaninda (kalin uc), boyu yerel +Y yonunde.
    static void Havuclar(Transform parent, GameObject food, (string ad, string prefab, float cap, float boy) y, System.Random rnd)
    {
        float r = y.cap / 2f;
        int sira = Mathf.FloorToInt(InnerD / y.cap);
        for (int kat = 0; kat < 2; kat++)
            for (int s = 0; s < sira - kat; s++)
                {
                    // her sirada tek havuc, yonu siradan siraya degisir (uclar bir sola bir saga)
                    int yon = (s + kat) % 2;
                    float z = -InnerD / 2f + y.cap * (s + 0.5f + 0.5f * kat);
                    float yk = Floor + r + kat * y.cap * 0.75f;
                    float cx = (yon == 0 ? -1f : 1f) * (InnerW / 2f - y.boy / 2f) * 0.8f;
                    var f = (GameObject)PrefabUtility.InstantiatePrefab(food, parent);
                    // +Y'yi X'e yatir: Z ekseninde -90 (uc +X'e) ya da +90 (uc -X'e)
                    var rot = Quaternion.Euler(((float)rnd.NextDouble() - 0.5f) * 20f, 0f, yon == 0 ? -90f : 90f);
                    Vector3 dir = rot * Vector3.up;
                    f.transform.localRotation = rot * Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                    f.transform.localPosition = new Vector3(cx, yk, z) - dir * (y.boy / 2f);
                }
    }

    static void TabakYigini(int adet)
    {
        var plate = AssetDatabase.LoadAssetAtPath<GameObject>(PlatePath);
        if (!plate) { Debug.LogWarning("Dekor kurulum: PF_Plate_Serving_01 yok (Araclari kur)"); return; }
        string path = $"{Root}/PF_Plate_Stack_{adet:00}.prefab";
        var root = new GameObject($"PF_Plate_Stack_{adet:00}");
        try
        {
            // tabak 4 cm yuksek, ic ice girince her biri ~1,8 cm yukselir
            for (int i = 0; i < adet; i++)
            {
                var p = (GameObject)PrefabUtility.InstantiatePrefab(plate, root.transform);
                p.name = $"Tabak_{i + 1}";
                p.transform.localPosition = new Vector3(0f, i * 0.018f, 0f);
                p.transform.localRotation = Quaternion.Euler(0f, i * 23f, 0f);
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { Object.DestroyImmediate(root); }
    }
}
