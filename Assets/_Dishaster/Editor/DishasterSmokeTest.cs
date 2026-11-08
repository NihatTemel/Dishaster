using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Dishaster.EditorTools
{
    /// <summary>
    /// Duman testi: bütün kurulumları çalıştırır, sahnelerde kayıp script/prefab arar, karakter ve animasyon
    /// varlıklarını denetler, sonunda Windows build'i alır. Menüden ya da batchmode'da:
    /// Unity.exe -batchmode -projectPath ... -executeMethod Dishaster.EditorTools.DishasterSmokeTest.RunBatch -logFile ...
    /// Sonuçlar "[SMOKE]" önekiyle log'a yazılır.
    /// </summary>
    public static class DishasterSmokeTest
    {
        static readonly List<string> Failures = new List<string>();

        [MenuItem("Tools/Dishaster/Test/Smoke Test (build dahil)", priority = 100)]
        static void RunFromMenu()
        {
            bool ok = Run(true);
            EditorUtility.DisplayDialog("Dishaster duman testi", ok ? "Her şey yolunda." : $"{Failures.Count} sorun: Console'a bak ([SMOKE]).", "Tamam");
        }

        public static void RunBatch()
        {
            bool ok;
            try
            {
                ok = Run(true);
            }
            catch (Exception exception)
            {
                Debug.LogError("[SMOKE] FAIL istisna: " + exception);
                ok = false;
            }

            EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool Run(bool build)
        {
            Failures.Clear();

            RunInstallers();
            CheckScenes();
            CheckCharacters();
            CheckFoods();
            CheckRecipe();
            if (build) Build();

            foreach (string failure in Failures)
                Debug.LogError("[SMOKE] FAIL " + failure);
            Debug.Log(Failures.Count == 0 ? "[SMOKE] RESULT OK" : $"[SMOKE] RESULT {Failures.Count} FAIL");
            return Failures.Count == 0;
        }

        static void Fail(string message) => Failures.Add(message);

        static void Info(string message) => Debug.Log("[SMOKE] " + message);

        // Kurulumlar normalde açılışta delayCall ile çalışıyor; batchmode'da beklemeden hepsi burada.
        static void RunInstallers()
        {
            Type[] types = typeof(DishasterSmokeTest).Assembly.GetTypes()
                .Where(type => type.Namespace == "Dishaster.EditorTools" && type.Name.EndsWith("Installer"))
                .OrderBy(type => type.Name)
                .ToArray();

            foreach (Type type in types)
            {
                MethodInfo install = type.GetMethod("Install", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (install == null) continue;

                try
                {
                    install.Invoke(null, null);
                    Info("installer ok: " + type.Name);
                }
                catch (Exception exception)
                {
                    Fail($"installer {type.Name}: {exception.InnerException ?? exception}");
                }
            }

            AssetDatabase.SaveAssets();
        }

        static void CheckScenes()
        {
            foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes)
            {
                if (!buildScene.enabled) continue;

                Scene scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Single);
                int objects = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    {
                        objects++;
                        GameObject go = child.gameObject;
                        int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                        if (missing > 0) Fail($"{scene.name}: '{Path(child)}' {missing} kayıp script");
                        if (PrefabUtility.IsPrefabAssetMissing(go)) Fail($"{scene.name}: '{Path(child)}' kayıp prefab");
                    }
                }

                Info($"scene ok: {scene.name} ({objects} obje)");
            }

            // Game sahnesinde olması gereken prefab'lar.
            Scene game = EditorSceneManager.OpenScene("Assets/_Dishaster/Scenes/Game.unity", OpenSceneMode.Single);
            foreach (Type required in new[] { typeof(KitchenCounter), typeof(NotebookRole), typeof(LowVisionRole), typeof(RecipeHud),
                         typeof(InspectView), typeof(VoiceChat), typeof(CharacterVisuals), typeof(KitchenFx) })
            {
                if (UnityEngine.Object.FindObjectOfType(required, true) == null) Fail($"Game sahnesinde {required.Name} yok");
            }

            Info("game prefabs checked: " + game.name);
        }

        static void CheckCharacters()
        {
            var visuals = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Dishaster/Prefabs/CharacterVisuals.prefab");
            CharacterVisuals component = visuals != null ? visuals.GetComponent<CharacterVisuals>() : null;
            if (component == null)
            {
                Fail("CharacterVisuals prefab yok");
                return;
            }

            if (component.controller == null) Fail("CharacterVisuals: controller yok");

            for (int i = 0; i < component.characters.Length; i++)
            {
                CharacterVisuals.Character character = component.characters[i];
                if (character == null || character.body == null || character.hands == null || character.handPoses == null)
                {
                    Fail($"karakter {i} eksik");
                    continue;
                }

                Animator animator = character.body.GetComponent<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman || !animator.avatar.isValid)
                    Fail($"{character.name}: geçerli Humanoid avatar yok");

                foreach (string bone in new[] { "Hips", "LeftFoot", "RightFoot", "Lid_L", "Lid_R" })
                    if (CharacterBody.FindBone(character.body.transform, bone) == null) Fail($"{character.name}: kemik yok {bone}");

                foreach (string bone in new[] { "LeftForeArm", "LeftHand", "LeftHandMiddle1", "RightForeArm", "RightHand", "RightHandMiddle1" })
                    if (CharacterBody.FindBone(character.hands.transform, bone) == null) Fail($"{character.name} FPS eli: kemik yok {bone}");

                bool viseme = character.body.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Any(renderer => renderer.sharedMesh != null && renderer.sharedMesh.GetBlendShapeIndex("Viseme_MBP") >= 0);
                if (!viseme) Fail($"{character.name}: Viseme_MBP yok");

                Info($"character ok: {character.name} ({character.handPoses.length:0.00} sn el klibi)");
            }

            foreach (string path in new[] { "Assets/_Dishaster/Animation/A_Idle.anim", "Assets/_Dishaster/Animation/A_Walk.anim" })
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null) Fail("klip yok: " + path);
                else if (!clip.humanMotion) Fail("klip Humanoid değil: " + path);
                else Info($"clip ok: {clip.name} ({AnimationUtility.GetCurveBindings(clip).Length} eğri)");
            }
        }

        static void CheckFoods()
        {
            var showcase = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Dishaster/Prefabs/FoodShowcase.prefab");
            if (showcase == null)
            {
                Fail("FoodShowcase prefab yok");
                return;
            }

            Transform foods = showcase.transform.Find("Foods");
            int count = foods != null ? foods.childCount : 0;
            if (count != 8) Fail($"FoodShowcase'de {count} yiyecek (8 bekleniyor)");
            else Info("foods ok: 8");
        }

        // Çilekli süt baştan sona: doğra, bardağa dök, kaseye dök, dilimleri ekle → tamamlanmalı.
        // Ayrıca: süt kutudan doğrudan kaseye → bozulmalı; fazla dilim → bozulmalı.
        static void CheckRecipe()
        {
            var created = new List<GameObject>();
            KitchenContainer Make(string name, float capacity, bool pour, bool receive, bool bowl)
            {
                var go = new GameObject(name);
                created.Add(go);
                var container = go.AddComponent<KitchenContainer>();
                container.capacity = capacity;
                container.canPour = pour;
                container.canReceive = receive;
                container.isMixingBowl = bowl;
                return container;
            }

            try
            {
                var targets = new int[Ingredients.Count];
                targets[(int)Ingredient.Milk] = 300;
                targets[(int)Ingredient.Strawberry] = 3;

                // Doğru yol.
                var recipe = new Recipe();
                recipe.SetTargets(targets);
                KitchenContainer cup = Make("Cup", 1000f, true, true, false);
                KitchenContainer bowl = Make("Bowl", 2500f, false, true, true);

                for (int i = 0; i < 4; i++) recipe.NoteSlice(Ingredient.Strawberry);
                recipe.UpdateSteps(cup, bowl);
                Expect(recipe.StepState(0) == RecipeStepState.Done, "adım 1 (doğra) tamam olmalı");

                cup.Add(Ingredient.Milk, 310f);
                recipe.UpdateSteps(cup, bowl);
                Expect(recipe.StepState(1) == RecipeStepState.Done, "adım 2 (bardak 310/300 ml) tamam olmalı");

                float[] poured = cup.Take(cup.Volume);
                recipe.CheckPour(cup, poured);
                bowl.AddMix(poured);
                recipe.Check(bowl);
                recipe.UpdateSteps(cup, bowl);
                Expect(!recipe.Failed, "bardaktan kaseye dökmek bozmamalı: " + recipe.FailReason);
                Expect(recipe.StepState(2) == RecipeStepState.Done, "adım 3 (kase sütü) tamam olmalı");
                Expect(!recipe.Completed, "dilimler eklenmeden tamamlanmamalı");

                for (int i = 0; i < 3; i++)
                {
                    bowl.Add(Ingredient.Strawberry, 1f);
                    recipe.Check(bowl);
                }

                recipe.UpdateSteps(cup, bowl);
                Expect(recipe.StepState(3) == RecipeStepState.Done, "adım 4 (3 dilim) tamam olmalı");
                Expect(recipe.Completed && !recipe.Failed, "tarif tamamlanmalı");

                // Süt kutudan doğrudan kaseye.
                var direct = new Recipe();
                direct.SetTargets(targets);
                KitchenContainer carton = Make("Carton", 1000f, true, false, false);
                KitchenContainer bowl2 = Make("Bowl2", 2500f, false, true, true);
                carton.Add(Ingredient.Milk, 1000f);
                float[] fromCarton = carton.Take(300f);
                direct.CheckPour(carton, fromCarton);
                Expect(direct.Failed, "kutudan doğrudan kaseye dökmek bozmalı");

                // Fazla dilim.
                var extra = new Recipe();
                extra.SetTargets(targets);
                KitchenContainer bowl3 = Make("Bowl3", 2500f, false, true, true);
                for (int i = 0; i < 4; i++)
                {
                    bowl3.Add(Ingredient.Strawberry, 1f);
                    extra.Check(bowl3);
                }

                Expect(extra.Failed, "4 dilim (tarif 3) bozmalı");
                Info("recipe ok: çilekli süt akışı");
            }
            catch (Exception exception)
            {
                Fail("tarif testi istisna: " + exception);
            }
            finally
            {
                foreach (GameObject go in created) UnityEngine.Object.DestroyImmediate(go);
            }
        }

        static void Expect(bool condition, string message)
        {
            if (!condition) Fail("tarif: " + message);
        }

        static void Build()
        {
            string output = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DishasterSmokeBuild", "Dishaster.exe");
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Fail($"build {report.summary.result}: {report.summary.totalErrors} hata");
                foreach (BuildStep step in report.steps)
                    foreach (BuildStepMessage message in step.messages)
                        if (message.type == LogType.Error || message.type == LogType.Exception)
                            Fail("build: " + message.content);
                return;
            }

            Info($"build ok: {output} ({report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalTime.TotalSeconds:0} sn)");
        }

        static string Path(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }

            return path;
        }
    }
}
