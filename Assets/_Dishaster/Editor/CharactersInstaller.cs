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
        const string Version = "2";
        const string VersionFile = "ProjectSettings/DishasterCharacters.version";
        const string PrefabPath = "Assets/_Dishaster/Prefabs/CharacterVisuals.prefab";
        const string AnimationFolder = "Assets/_Dishaster/Animation";
        const string ControllerPath = AnimationFolder + "/AC_Character.controller";
        const string OldControllerPath = AnimationFolder + "/AC_CharacterIK.controller";
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
            AssetDatabase.DeleteAsset(OldControllerPath);
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

        /// <summary>
        /// Locomotion: Speed'e göre Idle ↔ Walk (koşarken Walk hızlı). Klipler art'ta yok; Humanoid kas eğrileriyle
        /// burada üretiliyor (Animator kas adları, HumanTrait). IK geçişi açık: eller ve bakış koddan üstüne.
        /// Her kurulumda yeniden yapılıyor.
        /// </summary>
        static RuntimeAnimatorController GetController()
        {
            Directory.CreateDirectory(AnimationFolder);
            AnimationClip idle = SaveClip(BuildIdle(), AnimationFolder + "/A_Idle.anim");
            AnimationClip walk = SaveClip(BuildWalk(), AnimationFolder + "/A_Walk.anim");

            AssetDatabase.DeleteAsset(ControllerPath);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

            AnimatorControllerLayer[] layers = controller.layers;
            layers[0].iKPass = true;
            controller.layers = layers;

            AnimatorState state = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, 4f);
            tree.AddChild(walk, 6.5f);
            ChildMotion[] children = tree.children;
            children[2].timeScale = 1.6f;
            tree.children = children;
            controller.layers[0].stateMachine.defaultState = state;

            AssetDatabase.SaveAssets();
            return controller;
        }

        static AnimationClip SaveClip(AnimationClip clip, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(clip, path);
                return clip;
            }

            EditorUtility.CopySerialized(clip, existing);
            Object.DestroyImmediate(clip);
            return existing;
        }

        // Kas değerleri -1..1 (0: Unity'nin varsayılan insan pozu, bacaklar düz, kollar yarı açık).
        static readonly (string muscle, float value)[] IdlePose =
        {
            ("Left Arm Down-Up", -0.55f), ("Right Arm Down-Up", -0.55f),
            ("Left Arm Front-Back", 0.15f), ("Right Arm Front-Back", 0.15f),
            ("Left Forearm Stretch", 0.75f), ("Right Forearm Stretch", 0.75f),
            ("Left Upper Leg In-Out", 0.05f), ("Right Upper Leg In-Out", 0.05f),
            ("Left Lower Leg Stretch", 0.95f), ("Right Lower Leg Stretch", 0.95f),
            ("Left Upper Leg Front-Back", 0.6f), ("Right Upper Leg Front-Back", 0.6f),
        };

        static AnimationClip BuildIdle()
        {
            var clip = new AnimationClip { name = "A_Idle" };
            foreach ((string muscle, float value) in IdlePose)
                SetCurve(clip, muscle, Constant(value, 2f));

            // Nefes: göğüs ve kafa çok hafif.
            SetCurve(clip, "Chest Front-Back", Wave(0f, 0.025f, 2f, 0f));
            SetCurve(clip, "Head Nod Down-Up", Wave(0f, 0.02f, 2f, 0.5f));
            Loop(clip);
            return clip;
        }

        static AnimationClip BuildWalk()
        {
            const float length = 1f;
            var clip = new AnimationClip { name = "A_Walk" };

            foreach ((string muscle, float value) in IdlePose)
            {
                if (muscle.Contains("Upper Leg Front-Back") || muscle.Contains("Lower Leg Stretch") || muscle.Contains("Arm Front-Back")) continue;
                SetCurve(clip, muscle, Constant(value, length));
            }

            // Bacaklar zıt, kollar bacaklara zıt sallanıyor; öne giden bacağın dizi bükülüyor.
            SetCurve(clip, "Left Upper Leg Front-Back", Wave(0.6f, 0.35f, length, 0f));
            SetCurve(clip, "Right Upper Leg Front-Back", Wave(0.6f, 0.35f, length, 0.5f));
            SetCurve(clip, "Left Lower Leg Stretch", Knee(length, 0f));
            SetCurve(clip, "Right Lower Leg Stretch", Knee(length, 0.5f));
            SetCurve(clip, "Left Arm Front-Back", Wave(0.15f, 0.3f, length, 0.5f));
            SetCurve(clip, "Right Arm Front-Back", Wave(0.15f, 0.3f, length, 0f));
            SetCurve(clip, "Spine Twist Left-Right", Wave(0f, 0.06f, length, 0f));
            Loop(clip);
            return clip;
        }

        static void SetCurve(AnimationClip clip, string muscle, AnimationCurve curve)
        {
            if (System.Array.IndexOf(HumanTrait.MuscleName, muscle) < 0)
            {
                Debug.LogWarning($"[Dishaster] Kas bulunamadı: {muscle}");
                return;
            }

            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), muscle), curve);
        }

        static AnimationCurve Constant(float value, float length) => new AnimationCurve(new Keyframe(0f, value), new Keyframe(length, value));

        /// <summary>Sinüs: phase 0..1 (döngünün kesri).</summary>
        static AnimationCurve Wave(float center, float amplitude, float length, float phase)
        {
            var curve = new AnimationCurve();
            const int steps = 16;
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                curve.AddKey(t * length, center + amplitude * Mathf.Sin((t + phase) * Mathf.PI * 2f));
            }

            for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
            return curve;
        }

        /// <summary>Diz: bacak öne salınırken bükülüyor, basarken düz.</summary>
        static AnimationCurve Knee(float length, float phase)
        {
            var curve = new AnimationCurve();
            const int steps = 16;
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                float swing = Mathf.Max(0f, Mathf.Cos((t + phase) * Mathf.PI * 2f));
                curve.AddKey(t * length, 0.95f - 0.75f * swing);
            }

            for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
            return curve;
        }

        static void Loop(AnimationClip clip)
        {
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
        }
    }
}
