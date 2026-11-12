// Cizer'in defteri ve kalemi (Ozgur / art). Ust menude: Dishaster > Art > Karakter esyalarini kur.
// Bir kez calistirilir; sonuc (.meta, Materials ve Prefabs klasorleri) depoya girer, Nihat'in tekrar calistirmasi gerekmez.
// FBX ya da doku yenilenince tekrar calistirilir. Var olan prefab silinmez: yalnizca mesh, malzeme ve konumlar
// guncellenir, Nihat'in prefaba ekledigi bilesenler (script, collider, ...) korunur.
//
// Assets/ART/3D/CharacterItems/
//   SM_RecipeBook_01.fbx   Book (kapak + kenar + spiral) ve Page (cizim yuzeyi, UV tam 0-1)
//   SM_Marker_01.fbx       Body, Cap (cikarilabilir kapak) ve Tip (bos nesne: kecenin ucu)
//   Textures\T_CharacterItems_Atlas_BaseColor.png   (2048, iki esya ortak)
//   Materials\             bu betik olusturur: M_CharacterItems_Atlas.mat, M_RecipeBook_Page.mat
//   Prefabs\               bu betik olusturur: PF_RecipeBook_01, PF_Marker_01
// Blender'dan Game\Scripts\33_export_character_items_unity.py ile.
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class KarakterEsyaKurulum
{
    const string Root = "Assets/ART/3D/CharacterItems";

    [MenuItem("Dishaster/Art/Karakter esyalarini kur")]
    public static void Run()
    {
        var atlas = AtlasMaterial();
        var page = PageMaterial();
        Directory.CreateDirectory($"{Root}/Prefabs");

        int n = 0;
        if (Build("SM_RecipeBook_01", "PF_RecipeBook_01", atlas, page)) n++;
        if (Build("SM_Marker_01", "PF_Marker_01", atlas, page)) n++;
        AssetDatabase.SaveAssets();
        Debug.Log($"Karakter esya kurulum: bitti ({n} esya).");
    }

    static Material AtlasMaterial()
    {
        string tex = $"{Root}/Textures/T_CharacterItems_Atlas_BaseColor.png";
        var ti = AssetImporter.GetAtPath(tex) as TextureImporter;
        if (ti != null)
        {
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.alphaSource = TextureImporterAlphaSource.None;
            ti.maxTextureSize = 2048;
            ti.mipmapEnabled = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.SaveAndReimport();
        }
        else Debug.LogWarning($"Karakter esya kurulum: {tex} yok");

        var mat = LoadOrCreate($"{Root}/Materials/M_CharacterItems_Atlas.mat");
        mat.SetColor("_Color", Color.white);
        mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(tex));
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Glossiness", 0.15f);   // mat yuzey, parlama yok
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Defter sayfasi: duz cok acik krem (#F7F4EE). Canli cizim icin oyun kodu bu malzemeyi kendi
    // cizim malzemesiyle degistirebilir ya da _MainTex'ine RenderTexture verebilir (UV tam 0-1).
    static Material PageMaterial()
    {
        string path = $"{Root}/Materials/M_RecipeBook_Page.mat";
        bool isNew = !File.Exists(path);
        var mat = LoadOrCreate(path);
        if (isNew)
        {
            mat.SetColor("_Color", new Color(0.969f, 0.957f, 0.933f));
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Glossiness", 0.1f);
            EditorUtility.SetDirty(mat);
        }
        return mat;
    }

    static Material LoadOrCreate(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var std = Shader.Find("Standard");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat) { mat = new Material(std); AssetDatabase.CreateAsset(mat, path); }
        if (std && mat.shader != std) mat.shader = std;
        return mat;
    }

    static bool Build(string fbxName, string pfName, Material atlas, Material page)
    {
        string fbx = $"{Root}/{fbxName}.fbx";
        var mi = AssetImporter.GetAtPath(fbx) as ModelImporter;
        if (mi == null) { Debug.LogWarning($"Karakter esya kurulum: {fbx} yok"); return false; }
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
        mi.importTangents = ModelImporterTangents.None;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_CharacterItems_Atlas"), atlas);
        mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_RecipeBook_Page"), page);
        mi.SaveAndReimport();

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        if (!model) return false;

        string pfPath = $"{Root}/Prefabs/{pfName}.prefab";
        bool exists = File.Exists(pfPath);
        var root = exists ? PrefabUtility.LoadPrefabContents(pfPath) : new GameObject(pfName);
        try
        {
            foreach (var src in model.GetComponentsInChildren<Transform>(true).Where(t => t != model.transform))
            {
                var t = root.transform.Find(src.name);
                if (!t) { t = new GameObject(src.name).transform; t.SetParent(root.transform, false); }
                t.localPosition = model.transform.InverseTransformPoint(src.position);
                t.localRotation = Quaternion.Inverse(model.transform.rotation) * src.rotation;
                t.localScale = Vector3.one;

                var mfSrc = src.GetComponent<MeshFilter>();
                if (!mfSrc) continue;   // bos nesne (Tip)
                var mf = t.GetComponent<MeshFilter>();
                if (!mf) mf = t.gameObject.AddComponent<MeshFilter>();
                mf.sharedMesh = mfSrc.sharedMesh;
                var mr = t.GetComponent<MeshRenderer>();
                if (!mr) mr = t.gameObject.AddComponent<MeshRenderer>();
                mr.sharedMaterial = src.name == "Page" ? page : atlas;
            }
            PrefabUtility.SaveAsPrefabAsset(root, pfPath);
        }
        finally
        {
            if (exists) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }
        Debug.Log($"Karakter esya kurulum: {pfName}");
        return true;
    }
}
