// Kiler esyalari kurulumu (Ozgur / art). Ust menude: Dishaster > Art > Kiler esyalarini kur.
// Bir kez calistirilir; sonuc (.meta, Materials ve Prefabs klasorleri) depoya girer, Nihat'in tekrar calistirmasi gerekmez.
// FBX ya da doku yenilenince tekrar calistirilir. Var olan prefab silinmez: yalnizca mesh, malzeme ve yerler
// guncellenir, Nihat'in prefaba ekledigi bilesenler (script, collider, rigidbody...) korunur.
//
// Assets/ART/3D/Pantry/
//   SM_<Ad>.fbx        tek parca, tek nesne. Blender'dan Game\Scripts\28_export_pantry_unity.py ile.
//   Textures\          T_Carton_Atlas_BaseColor.png (sut + krema ortak), T_SpiceJar_<Baharat>_BaseColor.png
//   Materials\         bu betik olusturur: M_Carton_Atlas.mat, M_SpiceJar_<Baharat>.mat (Standard, parlama yok)
//   Prefabs\           bu betik olusturur: PF_MilkCarton_01, PF_CreamCarton_01, PF_SpiceJar_<Baharat>
//
// Prefab yapisi:
//   PF_MilkCarton_01     kok, pivot tabanin ortasi
//     Model              govde
//     Cap                kapak; pivot egik catiya oturdugu yerin ortasi (acarken bu noktada doner)
//   PF_SpiceJar_Thyme    kok, pivot tabanin ortasi
//     Model              govde (halka ve etiket dokuda)
//     Lid                ahsap kapak; pivot kapagin tabaninin ortasi (kaldirinca yukari kalkar)
// Her baharatin kendi malzemesi var (halka rengi ve etiket baharata sabit). Collider ve Rigidbody yok (Nihat).
using System.IO;
using UnityEditor;
using UnityEngine;

public static class KilerKurulum
{
    const string Root = "Assets/ART/3D/Pantry";

    // Baharat adi (doku ve prefab adinda) - halka rengi dokuda
    static readonly string[] Baharatlar = { "Thyme", "ChiliFlakes", "BlackPepper", "Salt", "Cumin", "Cinnamon" };

    // Blender'daki konum (x sag, y arka, z yukari) -> Unity (FBX aktarim ayarlarimizla)
    static Vector3 B(float x, float y, float z) => new Vector3(-x, z, -y);

    [MenuItem("Dishaster/Art/Kiler esyalarini kur")]
    public static void Run()
    {
        Directory.CreateDirectory($"{Root}/Prefabs");
        // Karton dokusunda govde UV'si on-sol kosede 0-1 disina tasar: Repeat olmali
        var karton = SetupMaterial("M_Carton_Atlas", SetupTexture($"{Root}/Textures/T_Carton_Atlas_BaseColor.png", true));

        foreach (var fbx in Directory.GetFiles(Root, "SM_*.fbx", SearchOption.TopDirectoryOnly))
            SetupModel(fbx.Replace('\\', '/'));

        // Kapak yerleri Blender'dan (govde pivotuna gore, metre), 10 Kas 2026
        Prefab("PF_MilkCarton_01", "SM_MilkCarton_01", "Cap", "SM_MilkCarton_01_Cap", B(0f, -0.0263f, 0.2018f), karton);
        Prefab("PF_CreamCarton_01", "SM_CreamCarton_01", "Cap", "SM_CreamCarton_01_Cap", B(0f, -0.0192f, 0.1107f), karton);

        foreach (var b in Baharatlar)
        {
            var tex = SetupTexture($"{Root}/Textures/T_SpiceJar_{b}_BaseColor.png", false);
            var mat = SetupMaterial($"M_SpiceJar_{b}", tex);
            Prefab($"PF_SpiceJar_{b}", "SM_SpiceJar_01", "Lid", "SM_SpiceJar_01_Lid", B(0f, 0f, 0.2014f), mat);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"Kiler kurulum: bitti (sut, krema, {Baharatlar.Length} baharat).");
    }

    static Texture2D SetupTexture(string path, bool repeat)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { Debug.LogWarning($"Kiler kurulum: {path} yok"); return null; }
        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = true;
        ti.maxTextureSize = 1024;
        ti.mipmapEnabled = true;
        ti.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material SetupMaterial(string name, Texture2D tex)
    {
        string path = $"{Root}/Materials/{name}.mat";
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var std = Shader.Find("Standard");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat)
        {
            mat = new Material(std);
            AssetDatabase.CreateAsset(mat, path);
        }
        if (std && mat.shader != std) mat.shader = std;
        mat.SetTexture("_MainTex", tex);
        mat.SetColor("_Color", Color.white);
        // parlama yok (yiyeceklerle ayni)
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Glossiness", 0.1f);
        mat.SetFloat("_SpecularHighlights", 0f);
        mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        mat.SetFloat("_GlossyReflections", 0f);
        mat.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void SetupModel(string path)
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
        mi.materialImportMode = ModelImporterMaterialImportMode.None;   // malzemeyi prefab verir
        mi.SaveAndReimport();
    }

    static void Prefab(string pfName, string govde, string kapakAd, string kapak, Vector3 kapakYeri, Material mat)
    {
        string pfPath = $"{Root}/Prefabs/{pfName}.prefab";
        bool exists = File.Exists(pfPath);
        var root = exists ? PrefabUtility.LoadPrefabContents(pfPath) : new GameObject(pfName);
        try
        {
            Parca(root.transform, "Model", govde, Vector3.zero, mat);
            Parca(root.transform, kapakAd, kapak, kapakYeri, mat);
            PrefabUtility.SaveAsPrefabAsset(root, pfPath);
        }
        finally
        {
            if (exists) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }
        Debug.Log($"Kiler kurulum: {pfName}");
    }

    static void Parca(Transform parent, string ad, string mesh, Vector3 pos, Material mat)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/{mesh}.fbx");
        var mfSrc = model ? model.GetComponentInChildren<MeshFilter>(true) : null;
        if (!mfSrc) { Debug.LogWarning($"Kiler kurulum: {mesh}.fbx yok, '{ad}' atlandi"); return; }
        var t = parent.Find(ad);
        if (!t)
        {
            t = new GameObject(ad).transform;
            t.SetParent(parent, false);
        }
        t.localPosition = pos;
        t.localRotation = mfSrc.transform == model.transform ? Quaternion.identity : mfSrc.transform.localRotation;
        t.localScale = Vector3.one;
        // Unity'de eksik bilesen "sahte null" doner; acik kontrolle eklenir
        var mf = t.GetComponent<MeshFilter>();
        if (!mf) mf = t.gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = mfSrc.sharedMesh;
        var mr = t.GetComponent<MeshRenderer>();
        if (!mr) mr = t.gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
    }
}
