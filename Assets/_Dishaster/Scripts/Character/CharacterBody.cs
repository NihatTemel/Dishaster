using System.Collections.Generic;
using UnityEngine;

namespace Dishaster
{
    /// <summary>
    /// Oyuncunun üstündeki karakter modeli (CharacterVisuals giydiriyor). Animator idle/yürüme oynatıyor, kafa
    /// oyuncunun baktığı yere dönüyor (Humanoid look-at). Eller rolün işine Animator'dan sonra burada uzanıyor
    /// (HandGoals: herkeste aynı hedef, aynı tutuş): uzaktaki hedefe karakter belden biraz eğilip o yana dönüyor,
    /// sonra omuz-dirsek-bilek çözülüyor. Bilek istenen yöne dönüyor ama ön koldan en çok MaxWristBend kadar
    /// bükülüyor (kırık görünmesin); parmaklar art'ın FP_Poses pozlarıyla kapanıyor. İşi olmayan kol animasyonda.
    /// Kendi ekranında kamera gözlerde; eğilince sadece bakış yönünde öne kayıyor (nişangah aynı yeri gösteriyor).
    /// LateUpdate'te göz kırpma ve sesle ağız (KARAKTERLER.md: kapalı ağız MBP=100, MBP+OO+EE en çok 100;
    /// Çizer'in ağzı bantlı, hep kapalı).
    /// Çömelme animasyonun üstüne: kalça iniyor, bacaklar (iki kemik IK) ayakları animasyonun koyduğu yerde ve
    /// yönde tutuyor, dizler öne bükülüyor (yürürken de: çömelerek yürüme). Ctrl ile (DishasterPlayer, kamera da
    /// iniyor) ya da el alçaktaki bir yere (yerdeki kasa) eğilmekle yetişmiyorsa kendiliğinden.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    // Mutfak (bıçak inişi, kap eğimi) ve defter yerleştikten sonra: hedefler oradan okunuyor.
    [DefaultExecutionOrder(200)]
    public class CharacterBody : MonoBehaviour
    {
        const string VisemeClosed = "Viseme_MBP";
        const string VisemeOpen = "Viseme_OO";
        const string VisemeWide = "Viseme_EE";

        const float HandFrameRate = 24f;

        // Uzanma: kol boyunun %97'si; yetmezse belden en çok bu kadar eğilme (derece). Adım yok: kapsül artık
        // gövde kalınlığında, tezgaha yaklaşılabiliyor.
        const float ReachFraction = 0.97f;
        const float MaxLean = 20f;
        // Kendi gövden (sadece kendi ekranında görünüyor) az eğiliyor: kamera gözlerde, gözler çok kaymasın.
        // Başkalarının makinesinde aynı oyuncu MaxLean'e kadar eğiliyor.
        const float LocalMaxLean = 8f;
        // Yandaki hedefe gövde o yana dönüyor (hedef açısının yarısı, en çok bu kadar).
        const float MaxTwist = 35f;
        // Eğilince kafa da öne düşmesin: eğilmenin bu kadarı kafada geri alınıyor.
        const float HeadKeep = 0.6f;
        // El ön kolun devamından en çok bu kadar bükülüyor (derece): fazlası kırık bilek gibi görünüyordu.
        const float MaxWristBend = 35f;
        // Çömelme: kalça en çok bacak boyunun bu kadarı iner (diz ~125°); Ctrl ile bunun bu kadarı.
        const float MaxSquatFraction = 0.55f;
        const float CrouchSquatFraction = 0.9f;
        // Çömelmek de yetmezse (yerdeki kasa) belden bu kadara kadar öne kapanıyor, uzanan omzu da öne alıyor.
        const float DeepLean = 80f;
        const float ReachTwist = 40f;
        // Bunlar sadece omurganın dibinden bu kadar aşağıdaki ve yatayda bu kadar yakındaki hedefe.
        const float LowTarget = 0.15f;
        const float NearTarget = 1.1f;

        static readonly int SpeedParameter = Animator.StringToHash("Speed");
        static readonly string[] SpineBones = { "Spine", "Spine1", "Spine2" };

        struct Face
        {
            public SkinnedMeshRenderer renderer;
            public int closed;
            public int open;
            public int wide;
        }

        /// <summary>Bir kol: kemikler, dinlenme pozundaki ölçüler, parmak pozları, o anki iş.</summary>
        class Arm
        {
            public bool left;
            public Transform upper;
            public Transform fore;
            public Transform wrist;
            public Transform knuckle;
            public Transform index;
            public Transform pinky;
            public Vector3 shoulderLocal;
            public float length;
            public float halfHand;
            public Transform[] fingers;
            public Quaternion[] open;
            public Quaternion[] grip;
            public Quaternion[] fist;
            // O karede animasyonun pozu (IK'dan önce): IK sonucu bununla ağırlık kadar karıştırılıyor.
            public Quaternion[] animated;
            public float weight;
            // Son karede elin ön koldan bükülmesi (derece, test için).
            public float bend;
            public HandGoal goal;
            // İş bitince kol bu hedeften yavaşça animasyona dönüyor.
            public HandGoal last;

            public HandGoal Work => goal.active ? goal : last;
            public bool Valid => upper != null && fore != null && wrist != null;
        }

        readonly List<Face> _faces = new List<Face>();

        DishasterPlayer _player;
        Animator _animator;
        Transform _lidL;
        Transform _lidR;
        Quaternion _lidRestL;
        Quaternion _lidRestR;
        float _lidAngle;
        bool _mouthTied;
        float _bodyHeight;
        Transform _footL;
        Transform _footR;
        float _footHeight;
        Transform[] _spine;
        Transform _head;
        Vector3 _eyeInHead;
        Vector3 _spinePivotLocal;

        float _nextBlink;
        float _blinkTime = -1f;
        float _openness;
        Vector3 _lastPosition;
        float _speed;
        float _lean;
        float _twist;
        float _reachSquat;
        float _squat;
        float _maxSquat;
        Arm _leftArm;
        Arm _rightArm;
        Leg _leftLeg;
        Leg _rightLeg;
        Transform _hips;

        /// <summary>Bir bacak: kalça-diz-ayak bileği.</summary>
        class Leg
        {
            public Transform upper;
            public Transform knee;
            public Transform foot;
            public bool Valid => upper != null && knee != null && foot != null;
        }

        // Uzaktakilerde eşya avuca oturtuluyor; bir sonraki karenin başında geri alınıyor ki hedef hep eşyanın
        // asıl yerinden okunsun (yoksa el ile eşya birbirini kovalıyor, kol dönüp duruyordu).
        Transform _snapped;
        Transform _snappedParent;
        Vector3 _snappedLocal;

        public DishasterPlayer Player => _player;

        /// <summary>Test için: uzaktakilerde IK'nın eşyayı/defteri tutan avuca ne kadar uzak kaldığı (metre).</summary>
        public float LastSnapDistance { get; private set; }

        /// <summary>Belden eğilme (derece).</summary>
        public float Lean => _lean;

        /// <summary>Çömelme: kalçanın indiği yükseklik (metre).</summary>
        public float Squat => _squat;

        /// <summary>Test için: sağ el işteyken avucun hedefe uzaklığı (metre); yetişiyorsa birkaç cm.</summary>
        public float ReachGap { get; private set; }

        /// <summary>Test için: sağ elin hedefi eğilip çömelerek yetişilebilecek yerde mi (değilse kol en yakına uzanıyor).</summary>
        public bool RightReachable { get; private set; }

        /// <summary>Test için: elin ön koldan bükülmesi (derece), kol işteyken.</summary>
        public float WristBend(bool left) => left ? _leftArm?.bend ?? 0f : _rightArm?.bend ?? 0f;

        /// <summary>Test için: eğilme, kol ağırlıkları, omuz, hedef avuç, gerçek avuç (karakterin uzayında), bilek bükülmesi.</summary>
        public string Describe()
        {
            if (_leftArm == null) return "";
            string Arm(Arm arm) => $"w {arm.weight:0.00} kol {arm.length:0.00} omuz {(arm.Valid ? ToBody(arm.upper.position) : Vector3.zero).ToString("F2")} hedef {ToBody(arm.Work.palm).ToString("F2")} avuç {ToBody(Palm(arm)).ToString("F2")} bilek {arm.bend:0}°";
            return $"eğilme {_lean:0}° dönme {_twist:0}° çömelme {_squat * 100f:0} cm | sol {Arm(_leftArm)} | sağ {Arm(_rightArm)}";
        }

        public void Setup(DishasterPlayer player, RuntimeAnimatorController controller, float lidAngle, bool mouthTied, GameObject hands, AnimationClip handPoses)
        {
            _player = player;
            _lidAngle = lidAngle;
            _mouthTied = mouthTied;

            // Klip olmadan Humanoid kalçayı köke (ayak tabanına) koyuyor, karakter yere gömülüyordu:
            // dinlenme pozundaki kalça yüksekliği saklanıp her kare gövde oraya konuyor.
            Transform hips = FindBone(transform, "Hips");
            _bodyHeight = hips != null ? transform.InverseTransformPoint(hips.position).y : 0.95f;

            // Gövde merkezi kalçadan biraz farklı: ayaklar dinlenme pozundaki yüksekliğe gelene kadar düzeltiliyor.
            _footL = FindBone(transform, "LeftFoot");
            _footR = FindBone(transform, "RightFoot");
            if (_footL != null && _footR != null) _footHeight = Mathf.Min(FootY(_footL), FootY(_footR));

            // Çömelme: bacak boyundan; Ctrl'deki iniş kameraya da (oyuncunun kafa pivotu o kadar iniyor).
            _hips = hips;
            _leftLeg = new Leg { upper = FindBone(transform, "LeftUpLeg"), knee = FindBone(transform, "LeftLeg"), foot = _footL };
            _rightLeg = new Leg { upper = FindBone(transform, "RightUpLeg"), knee = FindBone(transform, "RightLeg"), foot = _footR };
            if (_leftLeg.Valid)
            {
                float legLength = Vector3.Distance(_leftLeg.upper.position, _leftLeg.knee.position) + Vector3.Distance(_leftLeg.knee.position, _leftLeg.foot.position);
                _maxSquat = legLength * MaxSquatFraction;
                player.CrouchDrop = _maxSquat * CrouchSquatFraction;
            }

            // Belden eğilme: omurga kemikleri (eğilme aralarında paylaşılıyor), dönme noktası en alttaki.
            var spine = new List<Transform>();
            foreach (string name in SpineBones)
            {
                Transform bone = FindBone(transform, name);
                if (bone != null) spine.Add(bone);
            }

            _spine = spine.ToArray();
            _spinePivotLocal = _spine.Length > 0 ? ToBody(_spine[0].position) : new Vector3(0f, _bodyHeight, 0f);
            _head = FindBone(transform, "Head");
            // Gözlerin kafa kemiğine göre yeri: kendi ekranında kamera gerçek gözlerde (eğilince, başını çevirince onunla).
            Transform eyeL = FindBone(transform, "Eye_L") ?? FindBone(transform, "Lid_L");
            Transform eyeR = FindBone(transform, "Eye_R") ?? FindBone(transform, "Lid_R");
            if (_head != null && eyeL != null && eyeR != null) _eyeInHead = _head.InverseTransformPoint((eyeL.position + eyeR.position) * 0.5f);

            _animator = GetComponent<Animator>();
            _animator.runtimeAnimatorController = controller;
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // Kollar dinlenme pozunda (Animator henüz oynamadı) ölçülüyor.
            Dictionary<string, Quaternion>[] poses = FingerPoses(hands, handPoses);
            _leftArm = MakeArm("Left", poses);
            _rightArm = MakeArm("Right", poses);

            _lidL = FindBone(transform, "Lid_L");
            _lidR = FindBone(transform, "Lid_R");
            if (_lidL != null) _lidRestL = _lidL.localRotation;
            if (_lidR != null) _lidRestR = _lidR.localRotation;

            // Ağız şekilleri birden çok parçada olabiliyor (Aşçı'da kafa ve kollar): ada göre hepsine.
            foreach (SkinnedMeshRenderer renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = renderer.sharedMesh;
                if (mesh == null) continue;

                int closed = mesh.GetBlendShapeIndex(VisemeClosed);
                if (closed < 0) continue;

                _faces.Add(new Face
                {
                    renderer = renderer,
                    closed = closed,
                    open = mesh.GetBlendShapeIndex(VisemeOpen),
                    wide = mesh.GetBlendShapeIndex(VisemeWide),
                });
            }

            _lastPosition = player.transform.position;
            _nextBlink = Time.time + Random.Range(1f, 4f);
            ApplyMouth(0f);
        }

        /// <summary>Karakterin uzayı, metre cinsinden (modelin kök ölçeği ne olursa olsun).</summary>
        Vector3 ToBody(Vector3 world) => Quaternion.Inverse(transform.rotation) * (world - transform.position);

        public static Transform FindBone(Transform root, string name)
        {
            if (root.name == name) return root;

            foreach (Transform child in root)
            {
                Transform found = FindBone(child, name);
                if (found != null) return found;
            }

            return null;
        }

        Arm MakeArm(string side, Dictionary<string, Quaternion>[] poses)
        {
            var arm = new Arm
            {
                left = side == "Left",
                upper = FindBone(transform, side + "Arm"),
                fore = FindBone(transform, side + "ForeArm"),
                wrist = FindBone(transform, side + "Hand"),
                knuckle = FindBone(transform, side + "HandMiddle1"),
                index = FindBone(transform, side + "HandIndex1"),
                pinky = FindBone(transform, side + "HandPinky1"),
            };

            if (arm.Valid)
            {
                arm.shoulderLocal = ToBody(arm.upper.position);
                arm.length = Vector3.Distance(arm.upper.position, arm.fore.position) + Vector3.Distance(arm.fore.position, arm.wrist.position);
            }

            if (arm.wrist != null && arm.knuckle != null) arm.halfHand = Vector3.Distance(arm.wrist.position, arm.knuckle.position) * 0.5f;

            // Parmaklar: FPS elinin kemikleri karakterle aynı ad ve eksende (KARAKTERLER.md), pozlar aynen geçiyor.
            var fingers = new List<Transform>();
            var open = new List<Quaternion>();
            var grip = new List<Quaternion>();
            var fist = new List<Quaternion>();
            if (poses != null && arm.wrist != null)
            {
                foreach (Transform bone in arm.wrist.GetComponentsInChildren<Transform>(true))
                {
                    if (!IsFinger(bone.name)) continue;
                    if (!poses[0].TryGetValue(bone.name, out Quaternion o) || !poses[1].TryGetValue(bone.name, out Quaternion g) || !poses[2].TryGetValue(bone.name, out Quaternion f)) continue;

                    fingers.Add(bone);
                    open.Add(o);
                    grip.Add(g);
                    fist.Add(f);
                }
            }

            arm.fingers = fingers.ToArray();
            arm.open = open.ToArray();
            arm.grip = grip.ToArray();
            arm.fist = fist.ToArray();
            arm.animated = new Quaternion[arm.fingers.Length + 3];
            return arm;
        }

        static bool IsFinger(string name)
        {
            return name.Contains("HandThumb") || name.Contains("HandIndex") || name.Contains("HandMiddle") || name.Contains("HandRing") || name.Contains("HandPinky");
        }

        static readonly Dictionary<AnimationClip, Dictionary<string, Quaternion>[]> s_fingerPoses = new Dictionary<AnimationClip, Dictionary<string, Quaternion>[]>();

        /// <summary>FP_Poses'tan açık, tutma ve yumruk karelerindeki parmak dönüşleri (klip başına bir kez).</summary>
        static Dictionary<string, Quaternion>[] FingerPoses(GameObject hands, AnimationClip clip)
        {
            if (hands == null || clip == null) return null;
            if (s_fingerPoses.TryGetValue(clip, out Dictionary<string, Quaternion>[] cached)) return cached;

            GameObject sample = Instantiate(hands);
            if (sample.TryGetComponent(out Animator animator)) animator.enabled = false;

            int[] frames = { HandGoals.FrameOpen, HandGoals.FrameGrip, HandGoals.FrameFist };
            var poses = new Dictionary<string, Quaternion>[frames.Length];
            for (int i = 0; i < frames.Length; i++)
            {
                clip.SampleAnimation(sample, frames[i] / HandFrameRate);
                poses[i] = new Dictionary<string, Quaternion>();
                foreach (Transform bone in sample.GetComponentsInChildren<Transform>(true))
                    if (IsFinger(bone.name)) poses[i][bone.name] = bone.localRotation;
            }

            DestroyImmediate(sample);
            s_fingerPoses[clip] = poses;
            return poses;
        }

        // ── ANIMATOR ────────────────────────────────────────────────────────

        void OnAnimatorIK(int layerIndex)
        {
            if (_player == null) return;

            _animator.bodyPosition = transform.TransformPoint(new Vector3(0f, _bodyHeight, 0f));
            _animator.bodyRotation = transform.rotation;

            // Kafa ve biraz da gövde bakışı izliyor. Kendi gövdende gövde bakışla eğilmiyor: aşağı bakınca gözler
            // (kamera) öne dalmasın; kafa yine eğiliyor.
            Transform head = _player.headPivot;
            _animator.SetLookAtWeight(1f, _player.isLocalPlayer ? 0f : 0.25f, 0.9f, 0f, 0.6f);
            _animator.SetLookAtPosition(head.position + head.forward * 3f);
        }

        // Yürüme hızı: konum farkından (uzaktaki oyuncular NetworkTransform'la geliyor, onlarda da çalışıyor).
        void Update()
        {
            if (_player == null) return;

            // Geçen kare avuca oturtulan eşya asıl yerine dönüyor (hedef ondan okunacak).
            if (_snapped != null)
            {
                if (_snapped.parent == _snappedParent) _snapped.localPosition = _snappedLocal;
                _snapped = null;
            }

            Vector3 delta = _player.transform.position - _lastPosition;
            _lastPosition = _player.transform.position;
            delta.y = 0f;

            float speed = Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;
            _speed = Mathf.Lerp(_speed, Mathf.Min(speed, 8f), 1f - Mathf.Exp(-8f * Time.deltaTime));
            _animator.SetFloat(SpeedParameter, _speed < 0.15f ? 0f : _speed);
        }

        // ── KOLLAR ──────────────────────────────────────────────────────────

        /// <summary>Bilek hedefi: avucun yeri, parmak yönünde yarım el geride.</summary>
        static Vector3 WristTarget(Arm arm, HandGoal goal)
        {
            Vector3 fingers = goal.fingers.sqrMagnitude > 1e-8f ? goal.fingers.normalized : Vector3.zero;
            return goal.palm - fingers * arm.halfHand;
        }

        /// <summary>Dönme ve eğilmeden sonra omuzun yeri (karakterin uzayında).</summary>
        Vector3 Shoulder(Arm arm, float lean, float twist)
        {
            return _spinePivotLocal + Quaternion.Euler(0f, twist, 0f) * Quaternion.Euler(lean, 0f, 0f) * (arm.shoulderLocal - _spinePivotLocal);
        }

        /// <summary>Hedef yanda kaldıkça gövde o yana dönüyor.</summary>
        float TwistFor(Vector3 target)
        {
            Vector3 flat = target - _spinePivotLocal;
            float angle = Mathf.Atan2(flat.x, Mathf.Max(flat.z, 0.05f)) * Mathf.Rad2Deg;
            return Mathf.Clamp(angle * 0.5f, -MaxTwist, MaxTwist);
        }

        /// <summary>
        /// Bu kolun hedefine yetişmek için gereken en az eğilme ve çömelme, sırayla: önce biraz eğilme (en çok
        /// MaxLean; tezgaha uzanmak böyle), yetmezse çömelme, o da yetmezse belden derin eğilme (yerdeki kasa: kollar
        /// ve gövde kısa, çömelip öne kapanarak yetişiyor). Hiç yetişmiyorsa en yakına getiren. Ctrl'le çömelmişse omuz
        /// zaten o kadar aşağıda (crouchDrop).
        /// </summary>
        bool ReachFor(Arm arm, Vector3 target, float crouchDrop, out float lean, out float twist, out float squat)
        {
            float reach = arm.length * ReachFraction;
            float maxLean = _player.isLocalPlayer ? LocalMaxLean : MaxLean;
            float deepest = Mathf.Max(crouchDrop, _maxSquat);
            float baseTwist = TwistFor(target);
            twist = baseTwist;
            lean = 0f;
            squat = crouchDrop;

            // Çömelip öne kapanmak sadece yakındaki alçak hedefe (yerdeki kasa, alt raf): göğüs hizasında ya da uzakta
            // bir yere dokunurken gövde eğilmekle kalıyor (yetişmese de çömelip kıvrılmıyor).
            Vector3 fromSpine = target - _spinePivotLocal;
            bool lowAndNear = fromSpine.y < -LowTarget && new Vector2(fromSpine.x, fromSpine.z).magnitude < NearTarget;
            int phases = lowAndNear ? 3 : 1;

            float bestDistance = float.MaxValue;
            const int Steps = 20;
            for (int phase = 0; phase < phases; phase++)
            {
                for (int i = 0; i <= Steps; i++)
                {
                    float t = (float)i / Steps;
                    float trialLean = phase == 0 ? maxLean * t : phase == 1 ? maxLean : Mathf.Lerp(maxLean, DeepLean, t);
                    float trialSquat = phase == 0 ? crouchDrop : phase == 1 ? Mathf.Lerp(crouchDrop, deepest, t) : deepest;
                    // Derin uzanırken uzanan kolun omzu öne: gövde o yana dönüyor.
                    float trialTwist = phase == 2 ? baseTwist + (arm.left ? 1f : -1f) * ReachTwist * t : baseTwist;

                    // Derin uzanmada omuzun yeri tahmin (omurga eğrilerek bükülüyor): biraz pay bırakılıyor.
                    float distance = Vector3.Distance(Shoulder(arm, trialLean, trialTwist) + Vector3.down * trialSquat, target);
                    if (distance <= (phase == 2 ? reach - 0.03f : reach))
                    {
                        lean = trialLean;
                        squat = trialSquat;
                        twist = trialTwist;
                        return true;
                    }

                    if (distance < bestDistance - 0.005f)
                    {
                        bestDistance = distance;
                        lean = trialLean;
                        squat = trialSquat;
                        twist = trialTwist;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Kalça iniyor, bacaklar ayakları animasyonun koyduğu yerde ve yönde tutuyor (iki kemik IK, dizler öne ve
        /// biraz dışa). Ayakların yeri değişmediği için yere oturtma (ayak yüksekliği düzeltmesi) etkilenmiyor.
        /// </summary>
        void ApplySquat(float drop)
        {
            if (drop < 0.002f || _hips == null || !_leftLeg.Valid || !_rightLeg.Valid) return;

            Vector3 footL = _leftLeg.foot.position;
            Vector3 footR = _rightLeg.foot.position;
            Quaternion turnL = _leftLeg.foot.rotation;
            Quaternion turnR = _rightLeg.foot.rotation;

            _hips.position += -transform.up * drop;
            SolveLeg(_leftLeg, footL, turnL, -1f);
            SolveLeg(_rightLeg, footR, turnR, 1f);
        }

        void SolveLeg(Leg leg, Vector3 foot, Quaternion turn, float side)
        {
            Vector3 hint = leg.knee.position + transform.forward * 0.5f + transform.right * (0.12f * side);
            SolveArm(leg.upper, leg.knee, leg.foot, foot, hint);
            leg.foot.rotation = turn;
        }

        void UpdateArms()
        {
            HandGoals.For(_player, out _leftArm.goal, out _rightArm.goal);

            // Hedefler karakterin uzayında ölçülüyor (eğilme de buna göre).
            Quaternion inverse = Quaternion.Inverse(transform.rotation);

            // Ctrl'le çömelme: oyuncunun kafa pivotu da bu kadar iniyor (kamera onunla).
            float crouchDrop = _player.CrouchAmount * _player.CrouchDrop;

            float lean = 0f;
            float twist = 0f;
            float squat = crouchDrop;
            float blend = 1f - Mathf.Exp(-10f * Time.deltaTime);
            foreach (Arm arm in new[] { _leftArm, _rightArm })
            {
                if (arm.goal.active) arm.last = arm.goal;
                arm.weight = Mathf.Lerp(arm.weight, arm.goal.active ? 1f : 0f, blend);
                if (!arm.goal.active || !arm.Valid) continue;

                bool reachable = ReachFor(arm, inverse * (WristTarget(arm, arm.goal) - transform.position), crouchDrop, out float armLean, out float armTwist, out float armSquat);
                if (arm == _rightArm) RightReachable = reachable;
                lean = Mathf.Max(lean, armLean);
                squat = Mathf.Max(squat, armSquat);
                if (Mathf.Abs(armTwist) > Mathf.Abs(twist)) twist = armTwist;
            }

            float settle = 1f - Mathf.Exp(-9f * Time.deltaTime);
            _lean = Mathf.Lerp(_lean, lean, settle);
            _twist = Mathf.Lerp(_twist, twist, settle);
            // Uzanırken çömelme yumuşak; Ctrl'deki kısmı kamerayla birebir (o zaten yumuşak iniyor).
            _reachSquat = Mathf.Lerp(_reachSquat, Mathf.Max(0f, squat - crouchDrop), settle);
            _squat = crouchDrop + _reachSquat;

            // Gözler çömelip eğilmeden önce (animasyon ve bakışla): kendi ekranında kamera eğilme ve uzanırken
            // çömelme payı kadar kayıyor (Ctrl'le çömelmede kafa pivotu zaten iniyor, o kadarı sayılmıyor).
            bool localEyes = _player.isLocalPlayer && _head != null && _eyeInHead != Vector3.zero;
            Vector3 restEye = localEyes ? _head.TransformPoint(_eyeInHead) - transform.up * crouchDrop : Vector3.zero;

            ApplySquat(_squat);

            // Belden dönme ve eğilme: omurgada paylaşılıyor, kafa bakışı korusun diye biraz geri.
            if ((_lean > 0.05f || Mathf.Abs(_twist) > 0.05f) && _spine.Length > 0)
            {
                Quaternion total = transform.rotation * Quaternion.Euler(0f, _twist, 0f) * Quaternion.Euler(_lean, 0f, 0f) * Quaternion.Inverse(transform.rotation);
                Quaternion part = Quaternion.Slerp(Quaternion.identity, total, 1f / _spine.Length);
                foreach (Transform bone in _spine) bone.rotation = part * bone.rotation;
                if (_head != null) _head.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.Inverse(total), HeadKeep) * _head.rotation;
            }

            // Kendi ekranında kamera gözlerin dik durduğu yerde (kafa pivotu): aşağı bakınca kafa eğilse de dalmıyor.
            // Uzanıp eğilince gözlerle sadece bakış yönünde öne kayıyor: nişangahın gösterdiği yer değişmiyor
            // (tıklanan şey nişangahtaki). Görmedim'in eli bu ışının değdiği yere uzanıyor; ışın kameradan.
            if (localEyes)
            {
                Transform pivot = _player.headPivot;
                Vector3 shift = Vector3.Project(_head.TransformPoint(_eyeInHead) - restEye, pivot.forward);
                _player.CameraOffset = pivot.InverseTransformDirection(shift);
            }

            foreach (Arm arm in new[] { _leftArm, _rightArm })
            {
                if (!arm.Valid || arm.weight < 0.001f || !arm.Work.active) continue;

                HandGoal work = arm.Work;
                Vector3 wristTarget = WristTarget(arm, work);
                bool partial = arm.weight < 0.999f;
                if (partial) RememberAnimated(arm);

                // Dirsek: ön kol elin devamı olsun (bilek düz kalsın), biraz aşağıda; kolun doğal yanı (dışarı-aşağı)
                // ile karışık, ters dönmesin.
                float side = arm.left ? -1f : 1f;
                Vector3 natural = arm.upper.position + transform.TransformDirection(new Vector3(0.3f * side, -0.4f, -0.15f));
                Vector3 along = work.fingers.sqrMagnitude > 1e-8f ? work.fingers.normalized : transform.forward;
                Vector3 behind = wristTarget - along * 0.26f - transform.up * 0.12f;
                Vector3 hint = Vector3.Lerp(natural, behind, 0.7f);
                SolveArm(arm.upper, arm.fore, arm.wrist, wristTarget, hint);
                ShapeHand(arm, work);

                // Uzanırken ve geri inerken: kol hedefe tam çözülüyor, sonra her eklem animasyonun pozuyla ağırlık kadar
                // karışıyor. Kol omuzdan dönerek yerine geliyor, avuç yavaşça bacağa dönüyor; ağırlık bitince sıçramıyor.
                // (Eskiden sadece hedef noktası karışıyordu: dirsek ve el yönü son ana kadar tam çevrildiği için iniş
                // sırasında kol burulup avuç dışarı bakıyor, ağırlık sıfırlanınca birden düzeliyordu.)
                if (partial) BlendWithAnimated(arm, arm.weight);
            }

            ReachGap = _rightArm.Valid && _rightArm.goal.active ? Vector3.Distance(Palm(_rightArm), _rightArm.goal.palm) : 0f;
        }

        static void RememberAnimated(Arm arm)
        {
            arm.animated[0] = arm.upper.localRotation;
            arm.animated[1] = arm.fore.localRotation;
            arm.animated[2] = arm.wrist.localRotation;
            for (int i = 0; i < arm.fingers.Length; i++) arm.animated[i + 3] = arm.fingers[i].localRotation;
        }

        static void BlendWithAnimated(Arm arm, float weight)
        {
            arm.upper.localRotation = Quaternion.Slerp(arm.animated[0], arm.upper.localRotation, weight);
            arm.fore.localRotation = Quaternion.Slerp(arm.animated[1], arm.fore.localRotation, weight);
            arm.wrist.localRotation = Quaternion.Slerp(arm.animated[2], arm.wrist.localRotation, weight);
            for (int i = 0; i < arm.fingers.Length; i++)
                arm.fingers[i].localRotation = Quaternion.Slerp(arm.animated[i + 3], arm.fingers[i].localRotation, weight);
        }

        /// <summary>
        /// İki kemik IK (omuz-dirsek-bilek): dirsek açısı hedef uzaklığına göre, sonra kol hedefe döndürülüyor,
        /// en son dirsek ipucu tarafına çevriliyor.
        /// </summary>
        static void SolveArm(Transform root, Transform mid, Transform tip, Vector3 target, Vector3 hint)
        {
            Vector3 a = root.position;
            Vector3 b = mid.position;
            Vector3 c = tip.position;
            float ab = Vector3.Distance(a, b);
            float bc = Vector3.Distance(b, c);
            if (ab < 1e-4f || bc < 1e-4f) return;

            Vector3 at = target - a;
            float atLength = Mathf.Clamp(at.magnitude, Mathf.Abs(ab - bc) + 1e-3f, ab + bc - 1e-3f);

            float oldAngle = TriangleAngle((c - a).magnitude, ab, bc);
            float newAngle = TriangleAngle(atLength, ab, bc);
            Vector3 axis = Vector3.Cross(b - a, c - b);
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(hint - a, c - b);
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(at, c - b);
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.up;
            axis.Normalize();

            mid.rotation = Quaternion.AngleAxis((oldAngle - newAngle) * Mathf.Rad2Deg, axis) * mid.rotation;

            c = tip.position;
            root.rotation = Quaternion.FromToRotation(c - a, at) * root.rotation;

            // Dirseği ipucuna çevir (omuz-bilek ekseni etrafında).
            b = mid.position;
            c = tip.position;
            Vector3 ac = c - a;
            if (ac.sqrMagnitude < 1e-8f) return;

            Vector3 acNormal = ac.normalized;
            Vector3 abProjected = Vector3.ProjectOnPlane(b - a, acNormal);
            Vector3 hintProjected = Vector3.ProjectOnPlane(hint - a, acNormal);
            if (abProjected.sqrMagnitude > 1e-6f && hintProjected.sqrMagnitude > 1e-6f)
                root.rotation = Quaternion.FromToRotation(abProjected, hintProjected) * root.rotation;
        }

        /// <summary>Kenarları bilinen üçgende 'opposite' kenarın karşısındaki açı (radyan).</summary>
        static float TriangleAngle(float opposite, float side1, float side2)
        {
            float cos = (side1 * side1 + side2 * side2 - opposite * opposite) / (2f * side1 * side2);
            return Mathf.Acos(Mathf.Clamp(cos, -1f, 1f));
        }

        /// <summary>
        /// IK'dan sonra: bilek istenen yöne dönüyor, parmaklar art'ın pozlarıyla kapanıyor. El ön kolun devamından
        /// en çok MaxWristBend kadar bükülüyor (fazlası kırık bilek gibi). Elin ön kol ekseni etrafındaki burulmasının
        /// yarısını ön kol alıyor (bilekte tek noktada burulup kopuk görünmesin).
        /// </summary>
        static void ShapeHand(Arm arm, HandGoal work)
        {
            if (HandGoals.TryMeasure(arm.wrist, arm.knuckle, arm.index, arm.pinky, arm.left, out Quaternion now))
            {
                Quaternion goal = HandGoals.Wanted(work);
                Vector3 forearm = arm.wrist.position - arm.fore.position;
                Vector3 pointing = goal * Vector3.forward;
                if (forearm.sqrMagnitude > 1e-8f && Vector3.Angle(forearm, pointing) > MaxWristBend)
                {
                    Vector3 limited = Vector3.RotateTowards(forearm.normalized, pointing, MaxWristBend * Mathf.Deg2Rad, 0f);
                    goal = Quaternion.FromToRotation(pointing, limited) * goal;
                }

                // Tam dönüş: ağırlıkla karışım kolun tamamında, eklem eklem (BlendWithAnimated).
                Quaternion turn = goal * Quaternion.Inverse(now);
                Quaternion wanted = turn * arm.wrist.rotation;

                Vector3 axis = arm.wrist.position - arm.fore.position;
                if (axis.sqrMagnitude > 1e-8f)
                {
                    Vector3 projected = Vector3.Project(new Vector3(turn.x, turn.y, turn.z), axis);
                    var twist = new Quaternion(projected.x, projected.y, projected.z, turn.w);
                    float length = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
                    if (length > 1e-6f)
                    {
                        twist = new Quaternion(twist.x / length, twist.y / length, twist.z / length, twist.w / length);
                        arm.fore.rotation = Quaternion.Slerp(Quaternion.identity, twist, 0.5f) * arm.fore.rotation;
                    }
                }

                arm.wrist.rotation = wanted;
                if (HandGoals.TryMeasure(arm.wrist, arm.knuckle, arm.index, arm.pinky, arm.left, out Quaternion final))
                    arm.bend = Vector3.Angle(arm.wrist.position - arm.fore.position, final * Vector3.forward);
            }

            Quaternion[] closed = work.closedFrame == HandGoals.FrameFist ? arm.fist : arm.grip;
            for (int i = 0; i < arm.fingers.Length; i++)
            {
                arm.fingers[i].localRotation = HandGoals.Finger(arm.open[i], closed[i], work);
            }
        }

        /// <summary>İnsan iskeletinde avucun ortası (bilek ile orta parmak kökü arası).</summary>
        static Vector3 Palm(Arm arm)
        {
            return arm.knuckle != null ? (arm.wrist.position + arm.knuckle.position) * 0.5f : arm.wrist.position;
        }

        /// <summary>
        /// Elindeki şey gerçekten elinde görünsün: kol hedefe tam yetişemezse eşya/defter avuca taşınıyor (eğimi ve
        /// bıçak inişiyle). Sadece görüntü (konumlar sunucuda bakış noktasından); kendi ekranında da aynı.
        /// </summary>
        void AttachHeldToHands()
        {
            LastSnapDistance = 0f;

            NotebookRole notebook = NotebookRole.Instance;
            if (notebook != null && notebook.Owner == _player)
            {
                // Defter her kare baştan yerleşiyor (NotebookRole), geri almaya gerek yok.
                if (!_leftArm.goal.active || _leftArm.weight < 0.95f) return;

                // Defter elle tek parça: bilek sınırı yüzünden el hedeften dönmüşse defter de avucun etrafında onunla
                // dönüyor (parmak defterin içinden geçmesin), sonra avuca taşınıyor.
                Transform book = notebook.notebook;
                if (HandGoals.TryMeasure(_leftArm.wrist, _leftArm.knuckle, _leftArm.index, _leftArm.pinky, true, out Quaternion actual))
                {
                    Quaternion turn = actual * Quaternion.Inverse(HandGoals.Wanted(_leftArm.goal));
                    Vector3 pivot = _leftArm.goal.palm;
                    book.SetPositionAndRotation(pivot + turn * (book.position - pivot), turn * book.rotation);
                }

                Vector3 offset = Palm(_leftArm) - _leftArm.goal.palm;
                LastSnapDistance = offset.magnitude;
                book.position += offset;
                return;
            }

            // Kol hedefe giderken (ağırlık dolmadan) eşya yerinde kalıyor, el ona geliyor; varınca kalan fark kapanıyor.
            if (_player.role != MonkeyRoles.LowVision || !_rightArm.goal.active || _rightArm.weight < 0.95f) return;

            KitchenCounter counter = KitchenCounter.Instance;
            if (counter == null || counter.Held == null) return;

            Transform item = counter.Held.transform;
            Vector3 shift = Palm(_rightArm) - _rightArm.goal.palm;
            LastSnapDistance = shift.magnitude;

            _snapped = item;
            _snappedParent = item.parent;
            _snappedLocal = item.localPosition;
            item.position += shift;
        }

        void LateUpdate()
        {
            if (_player != null && _animator != null && _leftArm != null)
            {
                // Hedefler eşya/defter kendi yerindeyken okunuyor (oturtmadan önce), aynı karede çözülüyor.
                UpdateArms();
                AttachHeldToHands();
            }

            if (_footL != null && _footR != null)
                _bodyHeight += (_footHeight - Mathf.Min(FootY(_footL), FootY(_footR))) * 0.5f;

            UpdateBlink();

            float level = _mouthTied ? 0f : VoiceChat.LevelOf(_player);
            float target = Mathf.Clamp01((level - 0.02f) * 5f);
            // Açılış hızlı, kapanış biraz yavaş: kelimeler arası titremesin.
            _openness = Mathf.MoveTowards(_openness, target, Time.deltaTime * (target > _openness ? 14f : 7f));
            ApplyMouth(_openness);
        }

        // ── YÜZ ─────────────────────────────────────────────────────────────

        float FootY(Transform foot) => transform.InverseTransformPoint(foot.position).y;

        // LateUpdate: Animator kapakları ve ağzı ezmesin.
        void UpdateBlink()
        {
            if (_lidL == null && _lidR == null) return;

            float blink = 0f;
            if (_blinkTime < 0f && Time.time >= _nextBlink) _blinkTime = 0f;

            if (_blinkTime >= 0f)
            {
                // Kapanma 0.06 sn, kapalı 0.04 sn, açılma 0.09 sn.
                _blinkTime += Time.deltaTime;
                float t = _blinkTime;
                blink = t < 0.06f ? t / 0.06f : t < 0.1f ? 1f : 1f - (t - 0.1f) / 0.09f;

                if (t >= 0.19f)
                {
                    blink = 0f;
                    _blinkTime = -1f;
                    _nextBlink = Time.time + Random.Range(2f, 6f);
                }
            }

            blink = Mathf.Clamp01(blink);
            if (_lidL != null) _lidL.localRotation = _lidRestL * Quaternion.AngleAxis(_lidAngle * blink, Vector3.right);
            if (_lidR != null) _lidR.localRotation = _lidRestR * Quaternion.AngleAxis(_lidAngle * blink, Vector3.right);
        }

        void ApplyMouth(float openness)
        {
            float oo = Mathf.Clamp01(openness);
            foreach (Face face in _faces)
            {
                if (face.renderer == null) continue;

                face.renderer.SetBlendShapeWeight(face.closed, (1f - oo) * 100f);
                if (face.open >= 0) face.renderer.SetBlendShapeWeight(face.open, oo * 100f);
                if (face.wide >= 0) face.renderer.SetBlendShapeWeight(face.wide, 0f);
            }
        }
    }
}
