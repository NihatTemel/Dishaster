using UnityEngine;

namespace Dishaster
{
    /// <summary>
    /// Mutfak efektleri ve sesleri (prefab). KitchenCounter'ın olaylarını dinliyor; olaylar herkeste aynı geldiği için
    /// efektler de herkeste aynı yerde. Sesler kodla üretiliyor (dosya yok): kesme tok sesi, dökme akıntısı (kap
    /// doldukça incelen, görmeyen kulağıyla takip edebilsin), kaseye düşme, kapak, tarif bitince zil ya da bozulunca uğultu.
    /// Parçacıklar: kesmede meyve suyu, dökmede sıçrama, tarif bitince konfeti.
    /// </summary>
    public class KitchenFx : MonoBehaviour
    {
        const int Rate = 44100;

        [Header("Parçacıklar")]
        public ParticleSystem juice;
        public ParticleSystem splash;
        public ParticleSystem confetti;

        [Header("Kamera")]
        [Tooltip("Görenlerin kamerasına sıcak ton ve vinyet (Hidden/Dishaster/Grade).")]
        public Material grade;

        [Header("Ses")]
        [Range(0f, 1f)] public float volume = 0.8f;

        AudioClip _chop;
        AudioClip _tick;
        AudioClip _plop;
        AudioClip _door;
        AudioClip _success;
        AudioClip _fail;
        AudioSource _pourSource;

        int _recipeVersion = -1;
        bool _roleResolved;
        bool _deaf;
        bool _completed;
        bool _failed;

        void Awake()
        {
            _chop = Make("Chop", 0.12f, (t, n) => Envelope(t, 0.003f, 0.1f) * (0.6f * Mathf.Sin(2f * Mathf.PI * 140f * t * (1f - t * 3f)) + 0.5f * n));
            _tick = Make("Tick", 0.05f, (t, n) => Envelope(t, 0.002f, 0.04f) * 0.5f * Mathf.Sin(2f * Mathf.PI * 900f * t));
            _plop = Make("Plop", 0.18f, (t, n) => Envelope(t, 0.004f, 0.16f) * 0.7f * Mathf.Sin(2f * Mathf.PI * (320f - 900f * t) * t));
            _door = Make("Door", 0.35f, (t, n) => Envelope(t, 0.01f, 0.3f) * (0.5f * Mathf.Sin(2f * Mathf.PI * 80f * t) + 0.25f * n));
            _success = Make("Success", 1.1f, (t, n) => Arpeggio(t, new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.12f));
            _fail = Make("Fail", 0.6f, (t, n) => Envelope(t, 0.01f, 0.55f) * 0.5f * Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 110f * t)) * 0.4f);

            // Dökme: döngüdeki filtrelenmiş gürültü; perdesi kap doldukça yükseliyor.
            _pourSource = gameObject.AddComponent<AudioSource>();
            _pourSource.clip = Make("Pour", 1f, (t, n) => 0.35f * n);
            _pourSource.loop = true;
            _pourSource.spatialBlend = 1f;
            _pourSource.minDistance = 2f;
            _pourSource.volume = 0f;
            _pourSource.playOnAwake = false;
            gameObject.AddComponent<AudioLowPassFilter>().cutoffFrequency = 1800f;
            _pourSource.Play();

            KitchenCounter.Applied += OnApplied;
        }

        void OnDestroy()
        {
            KitchenCounter.Applied -= OnApplied;
        }

        void Update()
        {
            // Duymadım hiçbir şey duymuyor: efekt sesleri de ona çalınmıyor.
            if (!_roleResolved && Mirror.NetworkClient.localPlayer != null
                && Mirror.NetworkClient.localPlayer.TryGetComponent(out DishasterPlayer local))
            {
                _roleResolved = true;
                _deaf = local.role == MonkeyRoles.Deaf;

                // Görmeyenin kendi efekti var; ona eklenmiyor (test anahtarıyla normal görürken ekleniyor).
                bool blind = local.role == MonkeyRoles.LowVision && !DishasterTest.LowVisionSeesEverything;
                if (grade != null && !blind && !local.playerCamera.TryGetComponent(out CameraGrade _))
                    local.playerCamera.gameObject.AddComponent<CameraGrade>().material = grade;
            }

            KitchenCounter counter = KitchenCounter.Instance;
            if (counter == null) return;

            UpdatePour(counter);
            UpdateRecipe(counter);
        }

        // ── OLAYLAR ─────────────────────────────────────────────────────────

        void OnApplied(KitchenEventMessage message, KitchenItem item)
        {
            KitchenCounter counter = KitchenCounter.Instance;
            switch (message.type)
            {
                case KitchenEventType.Cut:
                    Play(_chop, message.position, 1f);
                    Burst(juice, message.position + Vector3.up * 0.02f, ColorOf(item), 18);
                    break;

                case KitchenEventType.AddToBowl:
                    Vector3 bowl = item != null && counter != null ? BowlPosition(counter, message.targetId) : message.position;
                    Play(_plop, bowl, 1f);
                    Burst(splash, bowl, new Color(1f, 1f, 1f, 0.8f), 10);
                    break;

                case KitchenEventType.Grab:
                case KitchenEventType.Drop:
                    if (item != null) Play(_tick, item.transform.position, 0.6f);
                    break;

                case KitchenEventType.Door:
                    if (counter != null) Play(_door, counter.DoorPosition(message.itemId), 0.8f);
                    break;
            }
        }

        static Vector3 BowlPosition(KitchenCounter counter, int id)
        {
            return id >= 0 && id < counter.Items.Count ? counter.Items[id].Center + Vector3.up * 0.05f : counter.transform.position;
        }

        void UpdatePour(KitchenCounter counter)
        {
            bool pouring = Time.time - counter.PourTime < 0.25f && counter.PourSource != null;
            float target = pouring && !_deaf ? volume * 0.7f : 0f;
            _pourSource.volume = Mathf.MoveTowards(_pourSource.volume, target, Time.deltaTime * 4f);
            if (!pouring) return;

            KitchenContainer into = counter.PourTarget;
            Vector3 at = into != null ? into.Item.Center + Vector3.up * 0.05f : counter.PourSource.Item.Center + Vector3.down * 0.3f;
            _pourSource.transform.position = at;

            // Kap doldukça ses inceliyor: gerçek hayattaki gibi, görmeyen de kulağıyla takip edebiliyor.
            float fill = into != null && into.capacity > 0f ? into.Volume / into.capacity : 0f;
            _pourSource.pitch = Mathf.Lerp(0.8f, 1.9f, fill);

            if (splash != null && Random.value < Time.deltaTime * 20f)
                Burst(splash, at, Ingredients.ColorOf(counter.PourIngredient), 2);
        }

        void UpdateRecipe(KitchenCounter counter)
        {
            Recipe recipe = counter.Recipe;
            if (recipe.Version == _recipeVersion) return;
            _recipeVersion = recipe.Version;

            if (recipe.Completed && !_completed)
            {
                _completed = true;
                Vector3 at = BowlCenter(counter);
                Play(_success, at, 1f);
                Burst(confetti, at + Vector3.up * 0.3f, Color.white, 80);
            }

            if (recipe.Failed && !_failed)
            {
                _failed = true;
                Play(_fail, BowlCenter(counter), 1f);
            }
        }

        static Vector3 BowlCenter(KitchenCounter counter)
        {
            foreach (KitchenItem item in counter.Items)
                if (item != null && item.Container != null && item.Container.isMixingBowl)
                    return item.Center;

            return counter.transform.position + Vector3.up;
        }

        // ── YARDIMCILAR ─────────────────────────────────────────────────────

        void Play(AudioClip clip, Vector3 position, float scale)
        {
            if (clip != null && !_deaf) AudioSource.PlayClipAtPoint(clip, position, volume * scale);
        }

        static void Burst(ParticleSystem system, Vector3 position, Color color, int count)
        {
            if (system == null) return;

            var emit = new ParticleSystem.EmitParams
            {
                position = position,
                applyShapeToPosition = true,
            };

            // Konfeti kendi renklerinde; diğerleri verilen renkte.
            if (system.main.startColor.mode == ParticleSystemGradientMode.Color) emit.startColor = color;
            system.Emit(emit, count);
        }

        /// <summary>Kesilen ürünün iç rengi: dilimin malzemesinden (yoksa kırmızımsı).</summary>
        static Color ColorOf(KitchenItem item)
        {
            // Art yiyeceği: malzemenin rengi.
            if (item != null && item.slicePrefabs != null && item.slicePrefabs.Length > 0 && item.slicePrefabs[0] != null
                && item.slicePrefabs[0].TryGetComponent(out KitchenIngredientItem art))
                return Ingredients.ColorOf(art.ingredient);

            if (item != null && item.slicePrefab != null)
            {
                Renderer renderer = item.slicePrefab.GetComponentInChildren<Renderer>();
                if (renderer != null && renderer.sharedMaterials.Length > 1) return renderer.sharedMaterials[1].color;
                if (renderer != null) return renderer.sharedMaterial.color;
            }

            return new Color(0.9f, 0.3f, 0.3f);
        }

        delegate float Wave(float t, float noise);

        static AudioClip Make(string name, float seconds, Wave wave)
        {
            int length = Mathf.CeilToInt(seconds * Rate);
            var data = new float[length];
            var random = new System.Random(name.GetHashCode());
            float smooth = 0f;
            for (int i = 0; i < length; i++)
            {
                // Hafif yumuşatılmış gürültü (pembe'ye yakın).
                smooth = smooth * 0.85f + (float)(random.NextDouble() * 2.0 - 1.0) * 0.15f;
                data[i] = Mathf.Clamp(wave((float)i / Rate, smooth * 3f), -1f, 1f);
            }

            AudioClip clip = AudioClip.Create(name, length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Envelope(float t, float attack, float decay)
        {
            if (t < attack) return t / attack;
            return Mathf.Exp(-(t - attack) / Mathf.Max(0.001f, decay) * 3f);
        }

        static float Arpeggio(float t, float[] notes, float step)
        {
            float sum = 0f;
            for (int i = 0; i < notes.Length; i++)
            {
                float local = t - i * step;
                if (local < 0f) continue;
                sum += Envelope(local, 0.005f, 0.6f) * 0.22f * (Mathf.Sin(2f * Mathf.PI * notes[i] * local) + 0.3f * Mathf.Sin(4f * Mathf.PI * notes[i] * local));
            }

            return sum;
        }
    }
}
