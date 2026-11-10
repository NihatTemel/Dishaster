// Kesilebilir yiyecek kurulumu (Ozgur / art). Ust menude: Dishaster > Art > Yiyecekleri kur.
// Bir kez calistirilir; sonuc (.meta, Materials ve Prefabs klasorleri) depoya girer, Nihat'in tekrar calistirmasi gerekmez.
// FBX ya da doku yenilenince tekrar calistirilir. Var olan prefab silinmez: yalnizca mesh, malzeme ve konumlar
// guncellenir, Nihat'in prefaba ekledigi bilesenler (script, collider, rigidbody...) korunur.
//
// Assets/ART/3D/Fruit_Vegetables/
//   SM_<Ad>.fbx        bir yiyecek: butun hal (SM_<Ad>) + dilimler (SM_<Ad>_Seg01...). Blender'dan 20_export_food_unity.py ile.
//   Textures\          T_Food_Atlas_BaseColor.png: sekiz yiyecegin ortak dokusu (2048)
//                      T_Food_Atlas_BaseColor_Cooked.png, _Burnt.png: pismis ve yanmis durumlar (ayni UV)
//   Materials\         bu betik olusturur: M_Food_Atlas.mat, M_Food_Atlas_Cooked.mat, M_Food_Atlas_Burnt.mat
//                      (Standard, parlama yok)
//   Prefabs\           bu betik olusturur: PF_<Ad>.prefab
//
// Prefab yapisi (PF_Carrot_01 ornegi):
//   PF_Carrot_01        kok, pivot butun halin tabaninin ortasinda
//     Whole             butun hal (acik)
//     Slices            dilimler (kapali). Slice_01 ust/+ uctaki parca; her dilimin pivotu kendi ortasinda,
//       Slice_01..N     kesildigi yerde durur: Whole kapatilip Slices acilinca ayni yemek dilimlenmis gorunur.
// Collider, Rigidbody ve kesme davranisi yok; bunlari oyun tarafi (Nihat) ekler.
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class YiyecekKurulum
{
    const string Root = "Assets/ART/3D/Fruit_Vegetables";
    const string MatName = "M_Food_Atlas";

    [MenuItem("Dishaster/Art/Yiyecekleri kur")]
    public static void Run()
    {
        var tex = SetupTexture($"{Root}/Textures/T_Food_Atlas_BaseColor.png");
        var mat = SetupMaterial($"{Root}/Materials/{MatName}.mat", tex);
        // Pismis ve yanmis durumlar (10 Kas 2026): ayni mesh ve UV, yalnizca doku farkli. Prefablarda normal kalir;
        // oyun sirasinda MeshRenderer.sharedMaterial bu ikisinden birine cevrilir (Nihat).
        foreach (var durum in new[] { "Cooked", "Burnt" })
            SetupMaterial($"{Root}/Materials/{MatName}_{durum}.mat",
                          SetupTexture($"{Root}/Textures/T_Food_Atlas_BaseColor_{durum}.png"));
        Directory.CreateDirectory($"{Root}/Prefabs");

        // yalnizca kok klasordeki SM_*.fbx (Test klasoru haric)
        var fbxs = Directory.GetFiles(Root, "SM_*.fbx", SearchOption.TopDirectoryOnly)
                            .Select(p => p.Replace('\\', '/')).OrderBy(p => p).ToArray();
        foreach (var fbx in fbxs)
        {
            SetupModel(fbx, mat);
            BuildPrefab(fbx, mat);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"Yiyecek kurulum: bitti ({fbxs.Length} yiyecek).");
    }

    static Texture2D SetupTexture(string path)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { Debug.LogWarning($"Yiyecek kurulum: {path} yok"); return null; }
        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = true;
        ti.maxTextureSize = 2048;
        ti.mipmapEnabled = true;
        ti.wrapMode = TextureWrapMode.Clamp;   // atlas: kenar tasmasi olmasin
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material SetupMaterial(string path, Texture2D tex)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat)
        {
            mat = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, path);
        }
        var std = Shader.Find("Standard");
        if (std && mat.shader != std) mat.shader = std;
        mat.SetTexture("_MainTex", tex);
        mat.SetColor("_Color", Color.white);
        // parlama yok (karakterlerle ayni kural)
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Glossiness", 0f);
        mat.SetFloat("_GlossMapScale", 0f);
        mat.SetFloat("_SpecularHighlights", 0f);
        mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        mat.SetFloat("_GlossyReflections", 0f);
        mat.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void SetupModel(string path, Material mat)
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
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), MatName), mat);
        mi.SaveAndReimport();
    }

    static void BuildPrefab(string fbxPath, Material mat)
    {
        string food = Path.GetFileNameWithoutExtension(fbxPath);          // SM_Carrot_01
        string pfName = "PF_" + food.Substring(3);                         // PF_Carrot_01
        string pfPath = $"{Root}/Prefabs/{pfName}.prefab";
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (!model) { Debug.LogWarning($"Yiyecek kurulum: {fbxPath} yuklenemedi"); return; }

        var wholeSrc = model.transform.Find(food);
        var segSrc = model.transform.Cast<Transform>()
                          .Where(t => t.name.StartsWith(food + "_Seg")).OrderBy(t => t.name).ToArray();
        if (!wholeSrc) { Debug.LogWarning($"Yiyecek kurulum: {food} icinde butun hal yok"); return; }

        bool exists = File.Exists(pfPath);
        var root = exists ? PrefabUtility.LoadPrefabContents(pfPath) : new GameObject(pfName);
        try
        {
            Part(root.transform, "Whole", wholeSrc, mat).gameObject.SetActive(true);
            var slices = Child(root.transform, "Slices");
            slices.localPosition = Vector3.zero; slices.localRotation = Quaternion.identity; slices.localScale = Vector3.one;
            for (int i = 0; i < segSrc.Length; i++)
                Part(slices, $"Slice_{i + 1:00}", segSrc[i], mat);
            // FBX'te artik olmayan fazla dilimler silinir
            foreach (var t in slices.Cast<Transform>().Where(t => t.name.StartsWith("Slice_")).ToArray())
                if (!int.TryParse(t.name.Substring(6), out int n) || n > segSrc.Length)
                    Object.DestroyImmediate(t.gameObject);
            if (!exists) slices.gameObject.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(root, pfPath);
        }
        finally
        {
            if (exists) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }
        Debug.Log($"Yiyecek kurulum: {pfName} ({segSrc.Length} dilim)");
    }

    static Transform Child(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (!t)
        {
            t = new GameObject(name).transform;
            t.SetParent(parent, false);
        }
        return t;
    }

    // FBX'teki parcanin mesh'ini ve konumunu prefabdaki cocuga yazar
    static Transform Part(Transform parent, string name, Transform src, Material mat)
    {
        var t = Child(parent, name);
        t.localPosition = src.localPosition;
        t.localRotation = src.localRotation;
        t.localScale = src.localScale;
        // Unity nesnelerinde ?? guvenilmez; acik kontrol
        var mf = t.GetComponent<MeshFilter>();
        if (!mf) mf = t.gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = src.GetComponent<MeshFilter>().sharedMesh;
        var mr = t.GetComponent<MeshRenderer>();
        if (!mr) mr = t.gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        return t;
    }
}
