using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Dishaster.EditorTools
{
    /// <summary>
    /// CharacterVisuals prefab'ını (rol başına art'ın karakteri, FPS elleri ve FP_Poses klibi) ve IK geçişi açık
    /// boş Animator Controller'ı oluşturur, Game sahnesine koyar. Art dosyaları Assets/ART/3D/Karakterler altında
    /// (bkz. KARAKTERLER.md); onlara dokunmuyor, sadece referans veriyor. Version değişince yeniden kurar.
    /// </summary>
    [InitializeOnLoad]
    public static class CharactersInstaller
    {
        const string Version = "1";
        const string VersionFile = "ProjectSettings/DishasterCharacters.version";
        const string PrefabPath = "Assets/_Dishaster/Prefabs/CharacterVisuals.prefab";
        const string AnimationFolder = "Assets/_Dishaster/Animation";
        const string ControllerPath = AnimationFolder + "/AC_CharacterIK.controller";
        const string ArtRoot = "Assets/ART/3D/Karakterler";

        // MonkeyRoles sırası: Görmedim, Duymadım, Bilmiyorum. Kapak açıları KARAKTERLER.md'den.
        static readonly (string name, float lidAngle)[] Characters =
        {
            ("Asci", -90f),
            ("Yorumcu", -75f),
            ("Cizer", -90f),
        };

        static CharactersInstaller()
        {
            EditorApplication.delayCall += AutoInstall;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) AutoInstall();
            };
        }

        static void AutoInstall()
        {
            if (File.Exists(VersionFile) && File.ReadAllText(VersionFile) == Version) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;

            Install();
        }

        [MenuItem("Tools/Dishaster/11 Install Characters", priority = 11)]
        static void Install()
        {
            // Art dosyaları eksikse kurulum tamam sayılmıyor: bir sonraki açılışta yeniden deneniyor.
            bool complete = BuildPrefab();
            if (GameScenePrefabs.EnsurePlaced(PrefabPath, Vector3.zero, Quaternion.identity, true) && complete)
                File.WriteAllText(VersionFile, Version);
        }

        /// <summary>Game sahnesinde yoksa ekler. Sahne kurucu Game'i yeniden yaratınca da çağırıyor.</summary>
        public static bool PlaceInGameScene()
        {
            if (!File.Exists(PrefabPath)) BuildPrefab();
            return GameScenePrefabs.EnsurePlaced(PrefabPath, Vector3.zero, Quaternion.identity);
        }

        static bool BuildPrefab()
        {
            var root = new GameObject("CharacterVisuals");
            var visuals = root.AddComponent<CharacterVisuals>();
            visuals.controller = GetController();
            visuals.characters = new CharacterVisuals.Character[Characters.Length];

            bool complete = true;
            for (int i = 0; i < Characters.Length; i++)
            {
                (string name, float lidAngle) = Characters[i];
                string bodyPath = $"{ArtRoot}/{name}/SK_{name}.fbx";
                string handsPath = $"{ArtRoot}/{name}/SK_{name}_FPHands.fbx";

                var character = new CharacterVisuals.Character
                {
                    name = name,
                    body = AssetDatabase.LoadAssetAtPath<GameObject>(bodyPath),
                    hands = AssetDatabase.LoadAssetAtPath<GameObject>(handsPath),
                    handPoses = AssetDatabase.LoadAllAssetsAtPath(handsPath).OfType<AnimationClip>()
                        .FirstOrDefault(clip => clip.name == "FP_Poses"),
                    lidAngle = lidAngle,
                };
                visuals.characters[i] = character;

                if (character.body == null || character.hands == null || character.handPoses == null)
                {
                    complete = false;
                    Debug.LogWarning($"[Dishaster] {name}: karakter, FPS eli ya da FP_Poses klibi bulunamadı ({ArtRoot}/{name}). " +
                                     "Art'ın 'Dishaster > Art > Karakterleri kur' menüsü çalıştırıldı mı?");
                }
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return complete;
        }

        /// <summary>Tek boş durum, IK geçişi açık: OnAnimatorIK çağrılsın, poz tamamen koddan.</summary>
        static RuntimeAnimatorController GetController()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller != null) return controller;

            Directory.CreateDirectory(AnimationFolder);
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            AnimatorControllerLayer[] layers = controller.layers;
            layers[0].iKPass = true;
            controller.layers = layers;

            AnimatorState idle = controller.layers[0].stateMachine.AddState("Idle");
            controller.layers[0].stateMachine.defaultState = idle;

            AssetDatabase.SaveAssets();
            return controller;
        }
    }
}
