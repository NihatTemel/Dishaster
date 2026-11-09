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
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

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
                            .Select(p => p.Replace('\\', '/')).OrderBy(p => p).ToArray();
        foreach (var fbx in fbxs)
        {
            string name = Path.GetFileNameWithoutExtension(fbx);
            var mat = Cookware.Contains(name) ? cook : ute;
            SetupModel(fbx, cook, ute);
            BuildPrefab(fbx, mat);
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"Arac kurulum: bitti ({fbxs.Length} alet).");
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
