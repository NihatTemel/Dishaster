using System.Collections.Generic;
using UnityEngine;

namespace Dishaster
{
    /// <summary>
    /// Oyuncunun üstündeki karakter modeli (CharacterVisuals giydiriyor). Animator idle/yürüme oynatıyor, kafa
    /// oyuncunun baktığı yere dönüyor (Humanoid look-at). Eller rolün işine Animator'dan sonra burada uzanıyor
    /// (HandGoals: FPS elleriyle aynı hedef, aynı tutuş): uzaktaki hedefe karakter önce yarım adım yaklaşıyor
    /// (kapsül modelden kalın, önünde boşluk var), sonra belden eğiliyor, sonra omuz-dirsek-bilek çözülüyor.
    /// Bilek istenen yöne dönüyor, parmaklar art'ın FP_Poses pozlarıyla kapanıyor. İşi olmayan kol animasyonda.
    /// LateUpdate'te göz kırpma ve sesle ağız (KARAKTERLER.md: kapalı ağız MBP=100, MBP+OO+EE en çok 100;
    /// Çizer'in ağzı bantlı, hep kapalı).
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

        // Uzanma: kol boyunun %97'si; yetmezse en çok bu kadar öne adım (metre) ve belden eğilme (derece).
        const float ReachFraction = 0.97f;
        const float MaxStep = 0.3f;
        // Yandaki hedefe gövde o yana dönüyor (hedef açısının yarısı, en çok bu kadar).
        const float MaxTwist = 35f;
        const float MaxLean = 30f;
        // Önce adım bu eğilmeye kadar deneniyor (az eğilip yaklaşmak doğal), olmazsa tam eğilme.
        const float ComfortLean = 16f;
        // Eğilince kafa da öne düşmesin: eğilmenin bu kadarı kafada geri alınıyor.
        const float HeadKeep = 0.6f;

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
            public string[] fingerNames;
            public Quaternion[] open;
            public Quaternion[] grip;
            public Quaternion[] fist;
            public float weight;
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
        Vector3 _baseLocal;

        float _nextBlink;
        float _blinkTime = -1f;
        float _openness;
        Vector3 _lastPosition;
        float _speed;
        float _lean;
        float _step;
        float _twist;
        Arm _leftArm;
        Arm _rightArm;

        // Uzaktakilerde eşya avuca oturtuluyor; bir sonraki karenin başında geri alınıyor ki hedef hep eşyanın
        // asıl yerinden okunsun (yoksa el ile eşya birbirini kovalıyor, kol dönüp duruyordu).
        Transform _snapped;
        Transform _snappedParent;
        Vector3 _snappedLocal;

        public DishasterPlayer Player => _player;

        /// <summary>Test için: uzaktakilerde IK'nın eşyayı/defteri tutan avuca ne kadar uzak kaldığı (metre).</summary>
        public float LastSnapDistance { get; private set; }

        /// <summary>Test için: adım, eğilme, kol ağırlıkları, hedef avuç ve gerçek avuç (karakterin uzayında).</summary>
        public string Describe()
        {
            if (_leftArm == null) return "";
            string Arm(Arm arm) => $"w {arm.weight:0.00} kol {arm.length:0.00} hedef {ToBody(arm.Work.palm).ToString("F2")} avuç {ToBody(Palm(arm)).ToString("F2")}";
            return $"adım {_step:0.00} eğilme {_lean:0}° dönme {_twist:0}° | sol {Arm(_leftArm)} | sağ {Arm(_rightArm)}";
        }

        public void Setup(DishasterPlayer player, RuntimeAnimatorController controller, float lidAngle, bool mouthTied, GameObject hands, AnimationClip handPoses)
        {
            _player = player;
            _lidAngle = lidAngle;
            _mouthTied = mouthTied;
            _baseLocal = transform.localPosition;

            // Klip olmadan Humanoid kalçayı köke (ayak tabanına) koyuyor, karakter yere gömülüyordu:
            // dinlenme pozundaki kalça yüksekliği saklanıp her kare gövde oraya konuyor.
            Transform hips = FindBone(transform, "Hips");
            _bodyHeight = hips != null ? transform.InverseTransformPoint(hips.position).y : 0.95f;

            // Gövde merkezi kalçadan biraz farklı: ayaklar dinlenme pozundaki yüksekliğe gelene kadar düzeltiliyor.
            _footL = FindBone(transform, "LeftFoot");
            _footR = FindBone(transform, "RightFoot");
            if (_footL != null && _footR != null) _footHeight = Mathf.Min(FootY(_footL), FootY(_footR));

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
            arm.fingerNames = fingers.ConvertAll(bone => bone.name).ToArray();
            arm.open = open.ToArray();
            arm.grip = grip.ToArray();
            arm.fist = fist.ToArray();
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

            // Kafa ve biraz da gövde bakışı izliyor.
            Transform head = _player.headPivot;
            _animator.SetLookAtWeight(1f, 0.25f, 0.9f, 0f, 0.6f);
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

        /// <summary>Adım, dönme ve eğilmeden sonra omuzun yeri (karakterin adımsız uzayında).</summary>
        Vector3 Shoulder(Arm arm, float step, float lean, float twist)
        {
            Vector3 shoulder = _spinePivotLocal + Quaternion.Euler(0f, twist, 0f) * Quaternion.Euler(lean, 0f, 0f) * (arm.shoulderLocal - _spinePivotLocal);
            return shoulder + Vector3.forward * step;
        }

        /// <summary>Hedef yanda kaldıkça gövde o yana dönüyor.</summary>
        float TwistFor(Vector3 target)
        {
            Vector3 flat = target - _spinePivotLocal;
            float angle = Mathf.Atan2(flat.x, Mathf.Max(flat.z, 0.05f)) * Mathf.Rad2Deg;
            return Mathf.Clamp(angle * 0.5f, -MaxTwist, MaxTwist);
        }

        /// <summary>
        /// Bu kolun hedefine yetişmek için adım ve eğilme: önce rahat eğilmeyle adım adım yaklaşıyor, olmazsa tam
        /// adım ve gereken eğilme; hiç yetişmiyorsa en yakına getiren.
        /// </summary>
        void ReachFor(Arm arm, Vector3 target, out float step, out float lean, out float twist)
        {
            float reach = arm.length * ReachFraction;
            step = 0f;
            lean = 0f;
            twist = TwistFor(target);

            float bestDistance = float.MaxValue;
            for (int pass = 0; pass < 2; pass++)
            {
                float maxLean = pass == 0 ? ComfortLean : MaxLean;
                for (int s = 0; s <= 6; s++)
                {
                    float trialStep = MaxStep * s / 6f;
                    for (int l = 0; l <= 10; l++)
                    {
                        float trialLean = maxLean * l / 10f;
                        float distance = Vector3.Distance(Shoulder(arm, trialStep, trialLean, twist), target);
                        if (distance <= reach)
                        {
                            step = trialStep;
                            lean = trialLean;
                            return;
                        }

                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            step = trialStep;
                            lean = trialLean;
                        }
                    }
                }
            }
        }

        void UpdateArms()
        {
            HandGoals.For(_player, out _leftArm.goal, out _rightArm.goal);

            // Adımsız karakter uzayı: hedefler bu uzayda ölçülüyor (adım da eğilme de buna göre).
            Transform parent = transform.parent;
            Vector3 basePosition = parent != null ? parent.TransformPoint(_baseLocal) : _baseLocal;
            Quaternion inverse = Quaternion.Inverse(transform.rotation);

            float step = 0f;
            float lean = 0f;
            float twist = 0f;
            float blend = 1f - Mathf.Exp(-10f * Time.deltaTime);
            foreach (Arm arm in new[] { _leftArm, _rightArm })
            {
                if (arm.goal.active) arm.last = arm.goal;
                arm.weight = Mathf.Lerp(arm.weight, arm.goal.active ? 1f : 0f, blend);
                if (!arm.goal.active || !arm.Valid) continue;

                ReachFor(arm, inverse * (WristTarget(arm, arm.goal) - basePosition), out float armStep, out float armLean, out float armTwist);
                step = Mathf.Max(step, armStep);
                lean = Mathf.Max(lean, armLean);
                if (Mathf.Abs(armTwist) > Mathf.Abs(twist)) twist = armTwist;
            }

            float settle = 1f - Mathf.Exp(-9f * Time.deltaTime);
            _step = Mathf.Lerp(_step, step, settle);
            _lean = Mathf.Lerp(_lean, lean, settle);
            _twist = Mathf.Lerp(_twist, twist, settle);

            // Yarım adım: model kapsülün içinde öne kayıyor (ayaklar onunla).
            transform.localPosition = _baseLocal + Vector3.forward * _step;


            // Belden dönme ve eğilme: omurgada paylaşılıyor, kafa bakışı korusun diye biraz geri.
            if ((_lean > 0.05f || Mathf.Abs(_twist) > 0.05f) && _spine.Length > 0)
            {
                Quaternion total = transform.rotation * Quaternion.Euler(0f, _twist, 0f) * Quaternion.Euler(_lean, 0f, 0f) * Quaternion.Inverse(transform.rotation);
                Quaternion part = Quaternion.Slerp(Quaternion.identity, total, 1f / _spine.Length);
                foreach (Transform bone in _spine) bone.rotation = part * bone.rotation;
                if (_head != null) _head.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.Inverse(total), HeadKeep) * _head.rotation;
            }

            // Kendi ekranında kamera karakterin gözlerinde: uzanıp eğilince öne kayıyor, aşağı bakınca kafa öne
            // eğiliyor (göğsün içinden bakmıyor). Bakış ışını kafa pivotundan, bundan etkilenmiyor.
            if (_player.isLocalPlayer && _head != null && _eyeInHead != Vector3.zero)
                _player.CameraOffset = _player.headPivot.InverseTransformPoint(_head.TransformPoint(_eyeInHead));

            foreach (Arm arm in new[] { _leftArm, _rightArm })
            {
                if (!arm.Valid || arm.weight < 0.001f || !arm.Work.active) continue;

                HandGoal work = arm.Work;
                Vector3 wristTarget = WristTarget(arm, work);
                Vector3 target = Vector3.Lerp(arm.wrist.position, wristTarget, arm.weight);

                // Dirsek: ön kol elin devamı olsun (bilek düz kalsın), biraz aşağıda; kolun doğal yanı (dışarı-aşağı)
                // ile karışık, ters dönmesin.
                float side = arm.left ? -1f : 1f;
                Vector3 natural = arm.upper.position + transform.TransformDirection(new Vector3(0.3f * side, -0.4f, -0.15f));
                Vector3 along = work.fingers.sqrMagnitude > 1e-8f ? work.fingers.normalized : transform.forward;
                Vector3 behind = wristTarget - along * 0.26f - transform.up * 0.12f;
                Vector3 hint = Vector3.Lerp(natural, behind, 0.7f);
                SolveArm(arm.upper, arm.fore, arm.wrist, target, hint);
                ShapeHand(arm, work);
            }
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
        /// IK'dan sonra: bilek istenen yöne dönüyor, parmaklar art'ın pozlarıyla kapanıyor. Elin ön kol ekseni
        /// etrafındaki burulmasının yarısını ön kol alıyor (bilekte tek noktada burulup kopuk görünmesin).
        /// </summary>
        static void ShapeHand(Arm arm, HandGoal work)
        {
            if (HandGoals.TryMeasure(arm.wrist, arm.knuckle, arm.index, arm.pinky, arm.left, out Quaternion now))
            {
                Quaternion turn = Quaternion.Slerp(Quaternion.identity, HandGoals.Wanted(work) * Quaternion.Inverse(now), arm.weight);
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
            }

            Quaternion[] closed = work.closedFrame == HandGoals.FrameFist ? arm.fist : arm.grip;
            for (int i = 0; i < arm.fingers.Length; i++)
            {
                Quaternion pose = HandGoals.Finger(arm.fingerNames[i], arm.open[i], closed[i], work);
                arm.fingers[i].localRotation = Quaternion.Slerp(arm.fingers[i].localRotation, pose, arm.weight);
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
                Vector3 offset = Palm(_leftArm) - _leftArm.goal.palm;
                LastSnapDistance = offset.magnitude;
                notebook.notebook.position += offset;
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
