// Karakter ve FPS el kurulumu (Ozgur / art). Ust menude: Dishaster > Art > Karakterleri kur.
// Bir kez calistirilir; sonuc (.meta ve Materials klasoru) depoya girer, Nihat'in tekrar calistirmasi gerekmez.
// FBX yenilenince tekrar calistirilabilir; var olan malzemelerin dokulari ve parlamasi yeniden ayarlanir.
//
// Her karakter klasorunde (Cizer, Yorumcu, Asci):
//   SK_<K>.fbx          govde. Humanoid avatar. Otomatik esleme Eye_L kemigini Jaw yuvasina koyuyor,
//                       avatar gecersiz oluyor; Jaw eslemesi silinir. Blend shape'ler acik.
//   SK_<K>_FPHands.fbx  birinci sahis elleri. Generic. Klip "FP_Poses": kare 0 dinlenme, 1 acik, 2 yumruk,
//                       3-7 bir'den bese sayma, 8 tutma, 9 avuc yukari (24 fps). Govdeyle ayni ten malzemesini kullanir.
//   Textures\           T_<K>_<Parca>_BaseColor / _Normal (normal haritalari OpenGL)
//   Materials\          bu betik olusturur: M_<K>_<Parca>.mat (Standard, parlama yok) ve M_<K>_FPCap.mat (bilek kapagi, rol rengi)
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class KarakterKurulum
{
    const string Root = "Assets/ART/3D/Karakterler";

    static readonly (string name, Color32 cap)[] Characters =
    {
        ("Cizer",   new Color32(0x9A, 0xA0, 0x6A, 255)), // adacayi
        ("Yorumcu", new Color32(0xE4, 0xB3, 0x55, 255)), // hardal
        ("Asci",    new Color32(0xA1, 0xBB, 0xD1, 255)), // tozlu mavi
    };

    [MenuItem("Dishaster/Art/Karakterleri kur")]
    public static void Run()
    {
        foreach (var (name, cap) in Characters)
        {
            string dir = $"{Root}/{name}";
            if (!AssetDatabase.IsValidFolder(dir)) { Debug.LogWarning($"Karakter kurulum: {dir} yok"); continue; }
            SetupTextures($"{dir}/Textures");
            string body = $"{dir}/SK_{name}.fbx";
            string hands = $"{dir}/SK_{name}_FPHands.fbx";
            if (File.Exists(body)) SetupBody(body, dir, name);
            if (File.Exists(hands)) SetupHands(hands, dir, name, cap);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Karakter kurulum: bitti.");
    }

    static void SetupTextures(string texDir)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { texDir }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            bool normal = Path.GetFileNameWithoutExtension(path).EndsWith("_Normal");
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (ti.textureType != type || ti.sRGBTexture == normal)
            {
                ti.textureType = type;
                ti.sRGBTexture = !normal;
                ti.SaveAndReimport();
            }
        }
    }

    static void BaseImport(ModelImporter mi)
    {
        mi.globalScale = 1f;
        mi.useFileScale = true;
        mi.importCameras = false;
        mi.importLights = false;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        mi.importAnimation = true;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
    }

    static void SetupBody(string path, string dir, string name)
    {
        var mi = (ModelImporter)AssetImporter.GetAtPath(path);
        BaseImport(mi);
        mi.importBlendShapes = true;
        mi.animationType = ModelImporterAnimationType.Human;
        var clips = mi.defaultClipAnimations;
        foreach (var c in clips) { c.name = "WeightTest"; c.loopTime = false; }
        mi.clipAnimations = clips;
        mi.SaveAndReimport();

        var hd = mi.humanDescription;
        if (hd.human != null && hd.human.Any(h => h.humanName == "Jaw"))
        {
            hd.human = hd.human.Where(h => h.humanName != "Jaw").ToArray();
            mi.humanDescription = hd;
            mi.SaveAndReimport();
        }
        var av = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
        if (av == null || !av.isHuman || !av.isValid) Debug.LogWarning($"Karakter kurulum: {name} Humanoid avatar olusmadi");
        else Debug.Log($"Karakter kurulum: {name} Humanoid avatar tamam");

        string matDir = $"{dir}/Materials";
        ExtractMaterials(path, matDir, _ => true);
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { matDir }))
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (mat.name.EndsWith("_FPCap")) continue;
            FixMaterial(mat, dir, name);
        }
    }

    static void SetupHands(string path, string dir, string name, Color cap)
    {
        var mi = (ModelImporter)AssetImporter.GetAtPath(path);
        BaseImport(mi);
        mi.importBlendShapes = false;
        mi.animationType = ModelImporterAnimationType.Generic;
        var clips = mi.defaultClipAnimations;
        foreach (var c in clips) { c.name = "FP_Poses"; c.loopTime = false; }
        mi.clipAnimations = clips;
        mi.SaveAndReimport();

        string matDir = $"{dir}/Materials";
        Directory.CreateDirectory(matDir);
        var bodyMat = AssetDatabase.LoadAssetAtPath<Material>($"{matDir}/M_{name}_Body.mat");
        var capMat = CapMaterial($"{matDir}/M_{name}_FPCap.mat", cap);

        // FBX'teki malzeme adlari Blender calisma dosyasindan gelir (M_Base_Female, M_FP_Cap_Cizer gibi)
        foreach (var m in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Select(m => m.name).Distinct().ToArray())
        {
            var target = m.Contains("FP_Cap") ? capMat : bodyMat;
            if (target == null) { Debug.LogWarning($"Karakter kurulum: {name} el malzemesi {m} icin hedef yok (once govde kurulmali)"); continue; }
            mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m), target);
        }
        mi.SaveAndReimport();
        Debug.Log($"Karakter kurulum: {name} FPS elleri tamam");
    }

    static void ExtractMaterials(string path, string matDir, System.Func<Material, bool> filter)
    {
        Directory.CreateDirectory(matDir);
        AssetDatabase.Refresh();
        foreach (var mat in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Where(filter))
        {
            string target = $"{matDir}/{mat.name}.mat";
            if (File.Exists(target)) continue;
            string err = AssetDatabase.ExtractAsset(mat, target);
            if (!string.IsNullOrEmpty(err)) Debug.LogWarning($"Karakter kurulum: {mat.name} cikarilamadi: {err}");
        }
        AssetDatabase.WriteImportSettingsIfDirty(path);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
    }

    // M_<K>_<Parca> -> T_<K>_<Parca>_BaseColor / _Normal. Parlama yok.
    static void FixMaterial(Material mat, string dir, string name)
    {
        var std = Shader.Find("Standard");
        if (std && mat.shader != std) mat.shader = std;
        string part = mat.name.StartsWith($"M_{name}_") ? mat.name.Substring($"M_{name}_".Length) : mat.name;
        var baseTex = FindTex(dir, $"T_{name}_{part}_BaseColor");
        var normal = FindTex(dir, $"T_{name}_{part}_Normal");
        if (baseTex) { mat.SetTexture("_MainTex", baseTex); mat.SetColor("_Color", Color.white); }
        if (normal) { mat.SetTexture("_BumpMap", normal); mat.SetFloat("_BumpScale", 1f); mat.EnableKeyword("_NORMALMAP"); }
        else { mat.SetTexture("_BumpMap", null); mat.DisableKeyword("_NORMALMAP"); }
        mat.SetTexture("_MetallicGlossMap", null);
        mat.DisableKeyword("_METALLICGLOSSMAP");
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Glossiness", 0f);
        mat.SetFloat("_GlossMapScale", 0f);
        mat.SetFloat("_SpecularHighlights", 0f);
        mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        mat.SetFloat("_GlossyReflections", 0f);
        mat.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
        EditorUtility.SetDirty(mat);
    }

    static Material CapMaterial(string path, Color color)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat)
        {
            mat = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_Color", color);
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Glossiness", 0f);
        mat.SetFloat("_SpecularHighlights", 0f);
        mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        mat.SetFloat("_GlossyReflections", 0f);
        mat.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Texture2D FindTex(string dir, string fileName)
    {
        foreach (var ext in new[] { ".png", ".jpg" })
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/Textures/{fileName}{ext}");
            if (t) return t;
        }
        return null;
    }
}
