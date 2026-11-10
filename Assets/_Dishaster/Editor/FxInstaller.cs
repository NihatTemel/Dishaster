using System.IO;
using UnityEditor;
using UnityEngine;

namespace Dishaster.EditorTools
{
    /// <summary>
    /// Süs ve efekt: KitchenFx prefab'ı (meyve suyu, sıçrama, konfeti parçacıkları + kodla üretilen sesler + kamera
    /// renk efekti malzemesi) Game sahnesine. Oda art'ın parçalarıyla (KitchenInstaller); eski karo shader'ı kalktı.
    /// Version değişince kendiliğinden yeniden kurar.
    /// </summary>
    [InitializeOnLoad]
    public static class FxInstaller
    {
        const string Version = "1";
        const string VersionFile = "ProjectSettings/DishasterFx.version";
        const string PrefabPath = "Assets/_Dishaster/Prefabs/KitchenFx.prefab";
        const string MaterialFolder = "Assets/_Dishaster/Materials";
        const string ParticleMaterialPath = MaterialFolder + "/M_Particle.mat";
        const string GradeMaterialPath = MaterialFolder + "/M_Grade.mat";
        const string GradeShaderPath = "Assets/_Dishaster/Shaders/Grade.shader";

        static FxInstaller()
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

        [MenuItem("Tools/Dishaster/13 Install Fx", priority = 13)]
        static void Install()
        {
            BuildPrefab();
            if (GameScenePrefabs.EnsurePlaced(PrefabPath, Vector3.zero, Quaternion.identity, true))
                File.WriteAllText(VersionFile, Version);
        }

        /// <summary>Game sahnesinde yoksa ekler. Sahne kurucu Game'i yeniden yaratınca da çağırıyor.</summary>
        public static bool PlaceInGameScene()
        {
            if (!File.Exists(PrefabPath)) BuildPrefab();
            return GameScenePrefabs.EnsurePlaced(PrefabPath, Vector3.zero, Quaternion.identity);
        }

        // ── EFEKT ───────────────────────────────────────────────────────────

        static void BuildPrefab()
        {
            var root = new GameObject("KitchenFx");
            var fx = root.AddComponent<KitchenFx>();
            fx.grade = GetMaterial(GradeMaterialPath, AssetDatabase.LoadAssetAtPath<Shader>(GradeShaderPath), null);

            Material particle = GetMaterial(ParticleMaterialPath, Shader.Find("Particles/Standard Unlit"),
                AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd"));

            fx.juice = AddParticles(root.transform, "Juice", particle, 0.45f, 1.3f, 0.012f, 1.8f, 0.01f, false);
            fx.splash = AddParticles(root.transform, "Splash", particle, 0.35f, 0.9f, 0.009f, 1.4f, 0.03f, false);
            fx.confetti = AddParticles(root.transform, "Confetti", particle, 2.6f, 2.6f, 0.028f, 0.35f, 0.08f, true);

            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
        }

        static Material GetMaterial(string path, Shader shader, Texture texture)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (shader != null)
            {
                material.shader = shader;
            }

            if (texture != null) material.mainTexture = texture;
            EditorUtility.SetDirty(material);
            return material;
        }

        static ParticleSystem AddParticles(Transform parent, string name, Material material, float lifetime, float speed, float size, float gravity, float radius, bool confetti)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = confetti ? 400 : 300;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            main.gravityModifier = gravity;

            if (confetti)
            {
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[]
                    {
                        new GradientColorKey(new Color(0.95f, 0.3f, 0.35f), 0f),
                        new GradientColorKey(new Color(0.98f, 0.8f, 0.25f), 0.33f),
                        new GradientColorKey(new Color(0.35f, 0.8f, 0.45f), 0.66f),
                        new GradientColorKey(new Color(0.3f, 0.6f, 0.95f), 1f),
                    },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
                main.startColor = new ParticleSystem.MinMaxGradient(gradient) { mode = ParticleSystemGradientMode.RandomColor };
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

                ParticleSystem.RotationOverLifetimeModule spin = system.rotationOverLifetime;
                spin.enabled = true;
                spin.z = new ParticleSystem.MinMaxCurve(-6f, 6f);

                ParticleSystem.NoiseModule noise = system.noise;
                noise.enabled = true;
                noise.strength = 0.6f;
                noise.frequency = 0.8f;
            }
            else
            {
                main.startColor = Color.white;
            }

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = confetti ? ParticleSystemShapeType.Sphere : ParticleSystemShapeType.Hemisphere;
            shape.radius = radius;
            // Yarım küre yukarı baksın (varsayılan +Z).
            if (!confetti) shape.rotation = new Vector3(-90f, 0f, 0f);

            // Sonlara doğru solsun.
            ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
            fade.enabled = true;
            var alpha = new Gradient();
            alpha.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = alpha;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            return system;
        }
    }
}
