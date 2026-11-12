// Arac gerec kurulumu (Ozgur / art). Ust menude: Dishaster > Art > Araclari kur.
// Bir kez calistirilir; sonuc (.meta, Materials ve Prefabs klasorleri) depoya girer, Nihat'in tekrar calistirmasi gerekmez.
// FBX ya da doku yenilenince tekrar calistirilir. Var olan prefab silinmez: yalnizca mesh ve malzeme
// guncellenir, Nihat'in prefaba ekledigi bilesenler (script, collider, rigidbody...) korunur.
//
// Assets/ART/3D/Tools/
//   SM_<Ad>.fbx        tek alet, tek nesne. Blender'dan 21_export_tools_unity.py ile.
//   Textures\          iki atlasin dokulari (2048): T_<Atlas>_BaseColor, _MetallicSmoothness, _Normal
//   Materials\         bu betik olusturur: M_Cookware_Atlas.mat, M_Utensil_Atlas.mat (Standard, Metallic)
//   Prefabs\           bu betik olusturur: PF_<Ad>.prefab
//
// Prefab yapisi (PF_Knife_Chef_01 ornegi):
//   PF_Knife_Chef_01    kok, pivot FBX'teki pivotla ayni
//     Model             MeshFilter + MeshRenderer
// Pivot: kap kacakta tabanin ortasi; bicak, spatula, kasiklar ve kepcede sapin tutulan yeri.
// Collider ve Rigidbody yok; bunlari oyun tarafi (Nihat) ekler.
//
// Olcu kabi (SM_MeasuringCup_01.fbx) atlas kullanmaz, kendine ait 4 malzemesi var; ayri kurulur (KurOlcuKabi).
// Blender'dan 32_export_measuring_cup_unity.py ile. Prefab PF_MeasuringCup_01:
//   PF_MeasuringCup_01   kok, pivot tabanin ortasi
//     Glass_Outer        dis cam, M_MeasuringCup_Glass (seffaf, render queue 3001)
//     Glass_Inner        ic cam,  M_MeasuringCup_Glass_Inner (ayni doku, render queue 3000: once ic, sonra dis cizilir)
//     Base               taban halkasi, M_MeasuringCup_Base
//     Marks              olcek ve rakamlar, M_MeasuringCup_Marks
//     Levels             bos nesneler Level_000ml ... Level_600ml: o cizginin yuksekligi (doluluk hesabi icin)
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class AracKurulum
{
    const string Root = "Assets/ART/3D/Tools";

    // Hangi alet hangi atlasi kullanir
    static readonly string[] Cookware =
    {
        "SM_Pot_Copper_01", "SM_Pot_Copper_Lid_01", "SM_Pan_Copper_01", "SM_MixingBowl_01",
        "SM_Bowl_Soup_01", "SM_Plate_Serving_01", "SM_CuttingBoard_01"
    };

    [MenuItem("Dishaster/Art/Araclari kur")]
    public static void Run()
    {
        var cook = SetupAtlas("Cookware_Atlas");
        var ute = SetupAtlas("Utensil_Atlas");
        Directory.CreateDirectory($"{Root}/Prefabs");

        var fbxs = Directory.GetFiles(Root, "SM_*.fbx", SearchOption.TopDirectoryOnly)
                            .Select(p => p.Replace('\\', '/'))
                            .Where(p => Path.GetFileNameWithoutExtension(p) != CupName)
                            .OrderBy(p => p).ToArray();
        foreach (var fbx in fbxs)
        {
            string name = Path.GetFileNameWithoutExtension(fbx);
            var mat = Cookware.Contains(name) ? cook : ute;
            SetupModel(fbx, cook, ute);
            BuildPrefab(fbx, mat);
        }
        bool cup = KurOlcuKabi();
        AssetDatabase.SaveAssets();
        Debug.Log($"Arac kurulum: bitti ({fbxs.Length} alet{(cup ? " + olcu kabi" : "")}).");
    }

    // ------------------------------------------------------------------ olcu kabi
    const string CupName = "SM_MeasuringCup_01";
    // FBX'teki parca adi -> malzeme adi
    static readonly (string part, string mat)[] CupParts =
    {
        ("Glass_Outer", "M_MeasuringCup_Glass"),
        ("Glass_Inner", "M_MeasuringCup_Glass_Inner"),
        ("Base", "M_MeasuringCup_Base"),
        ("Marks", "M_MeasuringCup_Marks"),
        ("Liquid", "M_MeasuringCup_Liquid"),
    };

    static bool KurOlcuKabi()
    {
        string fbx = $"{Root}/{CupName}.fbx";
        if (!File.Exists(fbx)) { Debug.LogWarning($"Arac kurulum: {fbx} yok"); return false; }

        // Malzemeler: dis ve ic cam ayni dokulari kullanir, yalnizca cizim sirasi farkli
        var mats = new System.Collections.Generic.Dictionary<string, Material>
        {
            ["M_MeasuringCup_Glass"] = CupMaterial("M_MeasuringCup_Glass", "Glass", 3001),
            ["M_MeasuringCup_Glass_Inner"] = CupMaterial("M_MeasuringCup_Glass_Inner", "Glass", 3000),
            ["M_MeasuringCup_Base"] = CupMaterial("M_MeasuringCup_Base", "Base", -1),
            ["M_MeasuringCup_Marks"] = CupMaterial("M_MeasuringCup_Marks", "Marks", -1),
            ["M_MeasuringCup_Liquid"] = LiquidMaterial(),
        };

        var mi = (ModelImporter)AssetImporter.GetAtPath(fbx);
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
        foreach (var kv in mats)
            mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
        mi.SaveAndReimport();

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        if (!model) { Debug.LogWarning($"Arac kurulum: {fbx} yuklenemedi"); return false; }

        string pfPath = $"{Root}/Prefabs/PF_MeasuringCup_01.prefab";
        bool exists = File.Exists(pfPath);
        var root = exists ? PrefabUtility.LoadPrefabContents(pfPath) : new GameObject("PF_MeasuringCup_01");
        try
        {
            foreach (var (part, matName) in CupParts)
            {
                var src = FindDeep(model.transform, part);
                var mfSrc = src ? src.GetComponent<MeshFilter>() : null;
                if (!mfSrc) { Debug.LogWarning($"Arac kurulum: olcu kabinda {part} yok"); continue; }
                bool created = !root.transform.Find(part);
                var t = Child(root.transform, part);
                CopyPose(src, model.transform, t);
                // Unity nesnelerinde ?? kullanilmaz (bos bilesen "sahte null" doner)
                var mf = t.GetComponent<MeshFilter>();
                if (!mf) mf = t.gameObject.AddComponent<MeshFilter>();
                mf.sharedMesh = mfSrc.sharedMesh;
                var mr = t.GetComponent<MeshRenderer>();
                if (!mr) mr = t.gameObject.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mats[matName];
                if (part.StartsWith("Glass")) mr.shadowCastingMode = ShadowCastingMode.Off;   // cam golge dusurmesin
                // Sivi: kabin ic hacmi, agiza kadar dolu. Seviye golgelendiricisi gelene kadar kapali dursun
                // (ilk kurulumda kapatilir; sonradan Nihat acarsa tekrar kurulum dokunmaz).
                if (part == "Liquid" && created) t.gameObject.SetActive(false);
            }
            var levels = Child(root.transform, "Levels");
            levels.localPosition = Vector3.zero; levels.localRotation = Quaternion.identity; levels.localScale = Vector3.one;
            foreach (var src in model.GetComponentsInChildren<Transform>(true).Where(x => x.name.StartsWith("Level_")))
            {
                var t = Child(levels, src.name);
                t.localPosition = model.transform.InverseTransformPoint(src.position);
                t.localRotation = Quaternion.identity;
                t.localScale = Vector3.one;
            }
            PrefabUtility.SaveAsPrefabAsset(root, pfPath);
        }
        finally
        {
            if (exists) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }
        Debug.Log("Arac kurulum: PF_MeasuringCup_01");
        return true;
    }

    // renderQueue >= 0: seffaf (Standard "Transparent" modu), < 0: opak
    static Material CupMaterial(string name, string part, int renderQueue)
    {
        var baseColor = SetupTexture($"{Root}/Textures/T_MeasuringCup_{part}_BaseColor.png", true, false);
        var metSmooth = SetupTexture($"{Root}/Textures/T_MeasuringCup_{part}_MetallicSmoothness.png", false, false);

        string path = $"{Root}/Materials/{name}.mat";
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var std = Shader.Find("Standard");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat) { mat = new Material(std); AssetDatabase.CreateAsset(mat, path); }
        if (std && mat.shader != std) mat.shader = std;

        mat.SetColor("_Color", Color.white);
        mat.SetTexture("_MainTex", baseColor);
        mat.SetTexture("_MetallicGlossMap", metSmooth);
        mat.EnableKeyword("_METALLICGLOSSMAP");
        mat.SetFloat("_GlossMapScale", 1f);
        mat.SetFloat("_SmoothnessTextureChannel", 0f);
        mat.SetTexture("_BumpMap", null);
        mat.DisableKeyword("_NORMALMAP");

        bool transparent = renderQueue >= 0;
        // Standard: _Mode 0 Opaque, 3 Transparent (parlama korunur; Fade parlamayi da soldurur)
        mat.SetFloat("_Mode", transparent ? 3f : 0f);
        mat.SetOverrideTag("RenderType", transparent ? "Transparent" : "");
        mat.SetInt("_SrcBlend", (int)BlendMode.One);
        mat.SetInt("_DstBlend", (int)(transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
        mat.SetInt("_ZWrite", transparent ? 0 : 1);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHABLEND_ON");
        if (transparent) mat.EnableKeyword("_ALPHAPREMULTIPLY_ON"); else mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = transparent ? renderQueue : -1;

        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Sivinin gecici malzemesi: duz beyaz, opak. Rengi oyun kodu verir; seviye kesen golgelendirici sonra.
    static Material LiquidMaterial()
    {
        const string path = Root + "/Materials/M_MeasuringCup_Liquid.mat";
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var std = Shader.Find("Standard");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat)
        {
            mat = new Material(std);
            mat.SetColor("_Color", new Color(0.95f, 0.92f, 0.86f));
            mat.SetFloat("_Glossiness", 0.6f);
            AssetDatabase.CreateAsset(mat, path);
        }
        return mat;
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var r = FindDeep(c, name); if (r) return r; }
        return null;
    }

    static Transform Child(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (!t) { t = new GameObject(name).transform; t.SetParent(parent, false); }
        return t;
    }

    // Parcanin FBX kokune gore konum ve donusu prefab cocuguna yazilir
    static void CopyPose(Transform src, Transform modelRoot, Transform dst)
    {
        dst.localPosition = modelRoot.InverseTransformPoint(src.position);
        dst.localRotation = Quaternion.Inverse(modelRoot.rotation) * src.rotation;
        dst.localScale = src.lossyScale;
    }

    static Material SetupAtlas(string atlas)
    {
        var baseColor = SetupTexture($"{Root}/Textures/T_{atlas}_BaseColor.png", true, false);
        var metSmooth = SetupTexture($"{Root}/Textures/T_{atlas}_MetallicSmoothness.png", false, false);
        var normal = SetupTexture($"{Root}/Textures/T_{atlas}_Normal.png", false, true);

        string path = $"{Root}/Materials/M_{atlas}.mat";
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var std = Shader.Find("Standard");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat)
        {
            mat = new Material(std);
            AssetDatabase.CreateAsset(mat, path);
        }
        if (std && mat.shader != std) mat.shader = std;

        mat.SetColor("_Color", Color.white);
        mat.SetTexture("_MainTex", baseColor);

        // Metallic haritasi: RGB = metallic, alfa = smoothness (1 - roughness)
        mat.SetTexture("_MetallicGlossMap", metSmooth);
        mat.EnableKeyword("_METALLICGLOSSMAP");
        mat.SetFloat("_GlossMapScale", 1f);
        mat.SetFloat("_SmoothnessTextureChannel", 0f);   // Metallic Alpha

        mat.SetTexture("_BumpMap", normal);
        mat.SetFloat("_BumpScale", 1f);
        if (normal) mat.EnableKeyword("_NORMALMAP"); else mat.DisableKeyword("_NORMALMAP");

        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Texture2D SetupTexture(string path, bool srgb, bool isNormal)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { Debug.LogWarning($"Arac kurulum: {path} yok"); return null; }
        ti.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        ti.sRGBTexture = srgb;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.alphaIsTransparency = false;
        ti.maxTextureSize = 2048;
        ti.mipmapEnabled = true;
        ti.wrapMode = TextureWrapMode.Clamp;   // atlas: kenar tasmasi olmasin
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void SetupModel(string path, Material cook, Material ute)
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
        mi.importTangents = ModelImporterTangents.CalculateMikk;   // normal haritasi icin
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_Cookware_Atlas"), cook);
        mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_Utensil_Atlas"), ute);
        mi.SaveAndReimport();
    }

    static void BuildPrefab(string fbxPath, Material mat)
    {
        string tool = Path.GetFileNameWithoutExtension(fbxPath);           // SM_Knife_Chef_01
        string pfName = "PF_" + tool.Substring(3);                         // PF_Knife_Chef_01
        string pfPath = $"{Root}/Prefabs/{pfName}.prefab";
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (!model) { Debug.LogWarning($"Arac kurulum: {fbxPath} yuklenemedi"); return; }

        // FBX'te tek nesne var; kok nesne ya da onun cocugu olabilir
        var mfSrc = model.GetComponentInChildren<MeshFilter>(true);
        if (!mfSrc) { Debug.LogWarning($"Arac kurulum: {tool} icinde mesh yok"); return; }

        bool exists = File.Exists(pfPath);
        var root = exists ? PrefabUtility.LoadPrefabContents(pfPath) : new GameObject(pfName);
        try
        {
            var t = root.transform.Find("Model");
            if (!t)
            {
                t = new GameObject("Model").transform;
                t.SetParent(root.transform, false);
            }
            t.localPosition = Vector3.zero;
            t.localRotation = mfSrc.transform == model.transform ? Quaternion.identity : mfSrc.transform.localRotation;
            t.localScale = Vector3.one;
            var mf = t.GetComponent<MeshFilter>();
            if (!mf) mf = t.gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = mfSrc.sharedMesh;
            var mr = t.GetComponent<MeshRenderer>();
            if (!mr) mr = t.gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            PrefabUtility.SaveAsPrefabAsset(root, pfPath);
        }
        finally
        {
            if (exists) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }
        Debug.Log($"Arac kurulum: {pfName}");
    }
}
