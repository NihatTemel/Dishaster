// Oda parcalari kurulumu (Ozgur / art). Ust menude: Dishaster > Art > Oda parcalarini kur.
// Bir kez calistirilir; sonuc (.meta, Materials ve Prefabs klasorleri) depoya girer, Nihat'in tekrar calistirmasi gerekmez.
// FBX ya da doku yenilenince tekrar calistirilir. Var olan prefab silinmez: mesh, malzeme ve parca yerleri guncellenir,
// prefaba sonradan eklenen bilesenler (script, collider...) ve secili renk korunur.
//
// Assets/ART/3D/Room/
//   SM_<Ad>.fbx       tek parca, tek nesne. Blender'dan Game\Scripts\30_export_room_unity.py ile.
//   Textures\         T_Room_Wall_BaseColor_<Renk>.png, T_Room_Floor_BaseColor_<Renk>.png (1024)
//   Materials\        bu betik olusturur: M_Room_Wall_<Renk>, M_Room_Floor_<Renk>, M_Room_Glass (Standard, parlama yok)
//   Prefabs\          PF_Wall_100, PF_Wall_050, PF_Wall_Corner, PF_Wall_Window_200, PF_Wall_Door_150, PF_Floor_100
//   Scripts\OdaRengi.cs   renk secimi (oyunda da calisir)
//
// Olcu ve pivot (metre): duvarlar 280 cm, 15 cm kalin; pivot odaya bakan yuzun sol-alt kosesi, yerde. Duvarin oda yuzu
// pivotun +Z tarafina (one) bakar, kalinlik -Z'ye (arkaya) gider: dolaplar (sol-arka kose pivot) duvara tam yaslanir.
// Zemin 1 x 1 m, ust yuzu y = 0, pivot arka-sol kose. 50 cm izgaraya oturur.
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class OdaKurulum
{
    const string Root = "Assets/ART/3D/Room";
    const string TrimPath = "Assets/ART/3D/Furniture/Materials/M_Furniture_Trim.mat";   // kapi ve pencere ahsabi

    // OdaRengi.Renk sirasi: Krem, Bej, Adacayi, TozluMavi, GriMavi, Hardal, Terrakota
    static readonly string[] Renkler = { "Krem", "Bej", "Adacayi", "TozluMavi", "GriMavi", "Hardal", "Terrakota" };

    // Blender'daki konum (x sag, y arka, z yukari) -> Unity (FBX aktarim ayarlarimizla)
    static Vector3 B(float x, float y, float z) => new Vector3(-x, z, -y);

    [MenuItem("Dishaster/Art/Oda parcalarini kur")]
    public static void Run()
    {
        Directory.CreateDirectory($"{Root}/Prefabs");
        var duvar = Renkler.Select(r => Malzeme($"M_Room_Wall_{r}", $"{Root}/Textures/T_Room_Wall_BaseColor_{r}.png", true)).ToArray();
        var zemin = Renkler.Select(r => Malzeme($"M_Room_Floor_{r}", $"{Root}/Textures/T_Room_Floor_BaseColor_{r}.png", false)).ToArray();
        var cam = Cam();
        // Tavan: bal ahsap lambri, 1 m'de tekrar (tavan mesh'inin UV'si metre). Mutfak kurulumu bunu kullanir.
        Malzeme("M_Room_Ceiling", $"{Root}/Textures/T_Room_Ceiling_BaseColor.png", true);
        var trim = AssetDatabase.LoadAssetAtPath<Material>(TrimPath);
        if (!trim) Debug.LogWarning("Oda kurulum: M_Furniture_Trim yok, once Mobilyalari kur calistirilmali");

        var varsayilanDuvar = duvar[0];   // Krem
        var varsayilanZemin = zemin[1];   // Bej
        var kubbe = LambaMalzemesi();
        foreach (var fbx in Directory.GetFiles(Root, "SM_*.fbx", SearchOption.TopDirectoryOnly))
            Model(fbx.Replace('\\', '/'), varsayilanDuvar, varsayilanZemin, cam, trim, kubbe);

        Prefab("PF_Wall_100", duvar, OdaRengi.Renk.Krem, ("Model", "SM_Wall_100", Vector3.zero));
        Prefab("PF_Wall_050", duvar, OdaRengi.Renk.Krem, ("Model", "SM_Wall_050", Vector3.zero));
        Prefab("PF_Wall_Corner", duvar, OdaRengi.Renk.Krem, ("Model", "SM_Wall_Corner", Vector3.zero));
        Prefab("PF_Floor_100", zemin, OdaRengi.Renk.Bej, ("Model", "SM_Floor_100", Vector3.zero));
        // Pencere kanatlari ve kapi kanadi kendi menteselerinde (Blender'dan, duvar pivotuna gore), 11 Kas 2026
        Prefab("PF_Wall_Window_200", duvar, OdaRengi.Renk.Krem,
            ("Model", "SM_Wall_Window_200", Vector3.zero),
            ("Pencere", "SM_Window_01", Vector3.zero),
            ("Kanat_Sol", "SM_Window_01_Sash_L", B(0.4422f, 0.075f, 1.1986f)),
            ("Kanat_Sag", "SM_Window_01_Sash_R", B(1.5578f, 0.075f, 1.1986f)));
        Prefab("PF_Wall_Door_150", duvar, OdaRengi.Renk.Krem,
            ("Model", "SM_Wall_Door_150", Vector3.zero),
            ("Kapi_Cercevesi", "SM_DoorFrame_01", Vector3.zero),
            ("Kapi", "SM_Door_01", B(1.2674f, 0.075f, 0f)));

        LambaPrefab();

        AssetDatabase.SaveAssets();
        Debug.Log("Oda kurulum: bitti (5 duvar, zemin, sarkit lamba; 6 duvar ve 6 zemin rengi).");
    }

    // ---------- sarkit lamba (11 Kas 2026) ----------
    // Kubbe mat hardal cam: renk ve emission dokusu (tepede hardal, alt agza dogru sicak beyaz). Duy ve kablo mobilya trim'i.
    static Material LambaMalzemesi()
    {
        string bc = $"{Root}/Textures/T_Lamp_Shade_BaseColor.png", em = $"{Root}/Textures/T_Lamp_Shade_Emission.png";
        foreach (var p in new[] { bc, em })
        {
            var ti = AssetImporter.GetAtPath(p) as TextureImporter;
            if (ti == null) { Debug.LogWarning($"Oda kurulum: {p} yok"); return null; }
            ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.maxTextureSize = 512;
            ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Clamp; ti.SaveAndReimport();
        }
        var mat = MatAsset("M_Lamp_Shade");
        mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(bc));
        mat.SetColor("_Color", Color.white);
        Mat(mat);
        mat.SetFloat("_Glossiness", 0.25f);   // mat cam
        mat.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(em));
        mat.SetColor("_EmissionColor", Color.white * 1.5f);
        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Kok = kablonun duya baglandigi nokta (kubbenin alt agzindan 35 cm yukari). Kablo ayri cocuk (Kablo): pivotu
    // alt ucunda, localScale.y ile uzar (1 = 30,7 cm). Tavan 2,8 m'de, kok 2,49 m'de iken kablo olcegi 1.
    // Isik kubbenin altindaki isik diskinin hemen altinda (kokten 39 cm asagi).
    static void LambaPrefab()
    {
        string pfPath = $"{Root}/Prefabs/PF_Lamp_Pendant_01.prefab";
        bool exists = File.Exists(pfPath);
        var root = exists ? PrefabUtility.LoadPrefabContents(pfPath) : new GameObject("PF_Lamp_Pendant_01");
        try
        {
            Parca(root.transform, "Model", "SM_Lamp_Pendant_01", Vector3.zero);
            // kablo boyu sahnede ayarlanir: mevcut olcek korunur
            var kt = root.transform.Find("Kablo");
            var kOlcek = kt ? kt.localScale : Vector3.one;
            Parca(root.transform, "Kablo", "SM_Lamp_Pendant_01_Cable", Vector3.zero);
            root.transform.Find("Kablo").localScale = kOlcek;
            var t = root.transform.Find("Isik");
            bool yeniIsik = !t;
            if (yeniIsik) { t = new GameObject("Isik").transform; t.SetParent(root.transform, false); }
            t.localPosition = B(0f, 0f, -0.393f);
            var l = t.GetComponent<Light>();
            if (!l) l = t.gameObject.AddComponent<Light>();
            if (yeniIsik)   // ilk kurulumda; sonra sahnede ayarlanan degerler korunur
            {
                l.type = LightType.Point;
                l.color = new Color(1f, 0.886f, 0.722f);   // #FFE2B8 sicak beyaz
                l.intensity = 1.5f;
                l.range = 7f;
                l.shadows = LightShadows.Soft;
            }
            var lamba = root.GetComponent<Lamba>();
            if (!lamba) lamba = root.AddComponent<Lamba>();
            lamba.isik = l;
            lamba.kubbe = root.transform.Find("Model").GetComponent<MeshRenderer>();
            PrefabUtility.SaveAsPrefabAsset(root, pfPath);
        }
        finally
        {
            if (exists) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }
        Debug.Log("Oda kurulum: PF_Lamp_Pendant_01");
    }

    static Material Malzeme(string ad, string doku, bool repeat)
    {
        var ti = AssetImporter.GetAtPath(doku) as TextureImporter;
        if (ti == null) return null;   // bu parcada olmayan renk
        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = true;
        ti.maxTextureSize = 1024;
        ti.mipmapEnabled = true;
        // Duvar UV'si duvar boyunca metre (0-1 disina tasar): Repeat. Zemin karosu 0-1: Clamp.
        ti.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        ti.SaveAndReimport();
        var mat = MatAsset(ad);
        mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(doku));
        mat.SetColor("_Color", Color.white);
        Mat(mat);
        return mat;
    }

    static Material Cam()
    {
        var mat = MatAsset("M_Room_Glass");
        mat.SetTexture("_MainTex", null);
        mat.SetColor("_Color", new Color(0.63f, 0.73f, 0.82f));   // tozlu mavi #A1BBD1
        Mat(mat);
        mat.SetFloat("_Glossiness", 0.35f);   // camda hafif parlaklik
        return mat;
    }

    static Material MatAsset(string ad)
    {
        string path = $"{Root}/Materials/{ad}.mat";
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var std = Shader.Find("Standard");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat) { mat = new Material(std); AssetDatabase.CreateAsset(mat, path); }
        if (std && mat.shader != std) mat.shader = std;
        return mat;
    }

    static void Mat(Material mat)
    {
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Glossiness", 0.1f);
        mat.SetFloat("_SpecularHighlights", 0f);
        mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        mat.SetFloat("_GlossyReflections", 0f);
        mat.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
        EditorUtility.SetDirty(mat);
    }

    static void Model(string path, Material duvar, Material zemin, Material cam, Material trim, Material kubbe)
    {
        var mi = (ModelImporter)AssetImporter.GetAtPath(path);
        mi.globalScale = 1f;
        mi.useFileScale = true;
        mi.importCameras = false;
        mi.importLights = false;
        mi.importAnimation = false;
        mi.animationType = ModelImporterAnimationType.None;
        mi.importBlendShapes = false;
        mi.addCollider = false;
        mi.isReadable = false;
        mi.importNormals = ModelImporterNormals.Import;
        mi.importTangents = ModelImporterTangents.CalculateMikk;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        void Esle(string ad, Material m) { if (m) mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), ad), m); }
        Esle("M_Room_Wall", duvar); Esle("M_Room_Floor", zemin); Esle("M_Room_Glass", cam); Esle("M_Furniture_Trim", trim);
        Esle("M_Lamp_Shade", kubbe);
        mi.SaveAndReimport();
    }

    static void Prefab(string pfName, Material[] renkler, OdaRengi.Renk varsayilan, params (string ad, string mesh, Vector3 yer)[] parcalar)
    {
        string pfPath = $"{Root}/Prefabs/{pfName}.prefab";
        bool exists = File.Exists(pfPath);
        var root = exists ? PrefabUtility.LoadPrefabContents(pfPath) : new GameObject(pfName);
        try
        {
            foreach (var (ad, mesh, yer) in parcalar) Parca(root.transform, ad, mesh, yer);
            var r = root.GetComponent<OdaRengi>();
            bool yeni = !r;
            if (yeni) { r = root.AddComponent<OdaRengi>(); r.renk = varsayilan; }
            r.malzemeler = renkler;
            r.Uygula();
            PrefabUtility.SaveAsPrefabAsset(root, pfPath);
        }
        finally
        {
            if (exists) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }
        Debug.Log($"Oda kurulum: {pfName}");
    }

    // FBX'teki parcanin mesh'ini ve malzemelerini prefabdaki cocuga yazar (malzemeler aktarimda eslendi)
    static void Parca(Transform parent, string ad, string mesh, Vector3 yer)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/{mesh}.fbx");
        var mfSrc = model ? model.GetComponentInChildren<MeshFilter>(true) : null;
        if (!mfSrc) { Debug.LogWarning($"Oda kurulum: {mesh}.fbx yok, '{ad}' atlandi"); return; }
        var t = parent.Find(ad);
        if (!t) { t = new GameObject(ad).transform; t.SetParent(parent, false); }
        t.localPosition = yer;
        t.localRotation = mfSrc.transform == model.transform ? Quaternion.identity : mfSrc.transform.localRotation;
        t.localScale = Vector3.one;
        // Unity'de eksik bilesen "sahte null" doner; acik kontrolle eklenir
        var mf = t.GetComponent<MeshFilter>();
        if (!mf) mf = t.gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = mfSrc.sharedMesh;
        var mr = t.GetComponent<MeshRenderer>();
        if (!mr) mr = t.gameObject.AddComponent<MeshRenderer>();
        // secili renk korunsun: mevcut duvar/zemin malzemesi varsa yerinde birakilir, OdaRengi.Uygula yeniden atar
        mr.sharedMaterials = mfSrc.GetComponent<MeshRenderer>().sharedMaterials;
    }
}
