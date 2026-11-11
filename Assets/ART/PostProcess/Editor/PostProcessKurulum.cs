// Post Processing (Built-in, com.unity.postprocessing) kurulumu (Ozgur / art).
// Ust menude: Dishaster > Art > Post process kur. Tekrar calistirmak guvenli: var olani bulur, ayarlari ezmez.
//
// 1. "PostProcessing" katmanini ekler (yoksa).
// 2. GamePlayer prefabindaki kameraya ve Game sahnesindeki Main Camera'ya Post-process Layer ekler
//    (volume katmani PostProcessing, kenar yumusatma SMAA).
// 3. Profil Assets/ART/PostProcess/PP_Mutfak.asset: ilk kurulumda hafif sicak renk, bloom (lambanin kubbesi icin)
//    ve hafif vinyet. Sonra degerler profilden (Inspector) ayarlanir; menu tekrar calisinca dokunmaz.
// 4. Game sahnesine "Global Volume" (Is Global) koyar ve profili baglar.
// Nihat'in CameraGrade efekti (M_Grade) notr degerlerde birakildi; ayarlar artik bu volume'den yapilir.
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.SceneManagement;

public static class PostProcessKurulum
{
    const string Katman = "PostProcessing";
    const string ProfilYolu = "Assets/ART/PostProcess/PP_Mutfak.asset";
    const string OyuncuPrefab = "Assets/_Dishaster/Prefabs/GamePlayer.prefab";
    const string GameSahne = "Assets/_Dishaster/Scenes/Game.unity";
    const string Kaynaklar = "Packages/com.unity.postprocessing/PostProcessing/PostProcessResources.asset";

    [MenuItem("Dishaster/Art/Post process kur")]
    public static void Run()
    {
        int layer = KatmanEkle();
        if (layer < 0) { Debug.LogError("Post process: bos katman yok"); return; }
        var profil = Profil();
        var kaynak = AssetDatabase.LoadAssetAtPath<PostProcessResources>(Kaynaklar);

        // oyuncu kamerasi
        var root = PrefabUtility.LoadPrefabContents(OyuncuPrefab);
        try
        {
            foreach (var cam in root.GetComponentsInChildren<Camera>(true)) LayerEkle(cam, layer, kaynak);
            PrefabUtility.SaveAsPrefabAsset(root, OyuncuPrefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        // Game sahnesi: Main Camera + Global Volume
        var sahne = SceneManager.GetSceneByPath(GameSahne);
        if (!sahne.isLoaded)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            sahne = EditorSceneManager.OpenScene(GameSahne, OpenSceneMode.Single);
        }
        foreach (var go in sahne.GetRootGameObjects())
            foreach (var cam in go.GetComponentsInChildren<Camera>(true)) LayerEkle(cam, layer, kaynak);
        var vol = sahne.GetRootGameObjects().FirstOrDefault(g => g.name == "Global Volume");
        if (!vol)
        {
            vol = new GameObject("Global Volume");
            SceneManager.MoveGameObjectToScene(vol, sahne);
        }
        vol.layer = layer;
        var v = vol.GetComponent<PostProcessVolume>();
        if (!v) v = vol.AddComponent<PostProcessVolume>();
        v.isGlobal = true;
        if (!v.sharedProfile) v.sharedProfile = profil;
        EditorSceneManager.MarkSceneDirty(sahne);
        EditorSceneManager.SaveScene(sahne);
        AssetDatabase.SaveAssets();
        Debug.Log("Post process: katman, oyuncu kamerasi, Game sahnesi ve PP_Mutfak profili hazir.");
    }

    static void LayerEkle(Camera cam, int layer, PostProcessResources kaynak)
    {
        var l = cam.GetComponent<PostProcessLayer>();
        bool yeni = !l;
        if (yeni) l = cam.gameObject.AddComponent<PostProcessLayer>();
        if (kaynak) l.Init(kaynak);
        l.volumeLayer = 1 << layer;
        if (!l.volumeTrigger) l.volumeTrigger = cam.transform;
        if (yeni) l.antialiasingMode = PostProcessLayer.Antialiasing.SubpixelMorphologicalAntialiasing;
        EditorUtility.SetDirty(l);
        Debug.Log($"Post process: {cam.name} kamerasina Post-process Layer {(yeni ? "eklendi" : "guncellendi")}");
    }

    static int KatmanEkle()
    {
        int mevcut = LayerMask.NameToLayer(Katman);
        if (mevcut >= 0) return mevcut;
        var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tm.FindProperty("layers");
        for (int i = 8; i < layers.arraySize; i++)
        {
            var p = layers.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(p.stringValue)) continue;
            p.stringValue = Katman;
            tm.ApplyModifiedPropertiesWithoutUndo();
            return i;
        }
        return -1;
    }

    static PostProcessProfile Profil()
    {
        var p = AssetDatabase.LoadAssetAtPath<PostProcessProfile>(ProfilYolu);
        if (p) return p;
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ProfilYolu));
        p = ScriptableObject.CreateInstance<PostProcessProfile>();
        AssetDatabase.CreateAsset(p, ProfilYolu);

        var cg = Ekle<ColorGrading>(p);
        cg.temperature.Override(5f);
        cg.saturation.Override(8f);
        cg.contrast.Override(5f);
        var bloom = Ekle<Bloom>(p);
        bloom.intensity.Override(2f);
        bloom.threshold.Override(1.1f);
        var vin = Ekle<Vignette>(p);
        vin.intensity.Override(0.15f);
        EditorUtility.SetDirty(p);
        return p;
    }

    static T Ekle<T>(PostProcessProfile p) where T : PostProcessEffectSettings
    {
        var s = p.AddSettings<T>();
        s.name = typeof(T).Name;
        s.enabled.Override(true);
        AssetDatabase.AddObjectToAsset(s, p);
        return s;
    }
}
