using System.Collections.Generic;
using UnityEngine;

namespace Dishaster
{
    /// <summary>
    /// Oyuncunun üstündeki karakter modeli (CharacterVisuals giydiriyor). Humanoid IK ile: kafa oyuncunun baktığı
    /// yere dönüyor, eller rolün işine uzanıyor (Bilmiyorum defteri sol eliyle tutuyor, çizerken sağ eli kalemde;
    /// Görmedim'in sağ eli dokunduğu yerde), boşta eller kalça yanında. LateUpdate'te göz kırpma ve sesle ağız
    /// (KARAKTERLER.md: kapalı ağız MBP=100, MBP+OO+EE en çok 100; Çizer'in ağzı bantlı, hep kapalı).
    /// </summary>
    [RequireComponent(typeof(Animator))]
    // Mutfak (bıçak inişi, kap eğimi) ve defter yerleştikten sonra: uzaktakilerde eşyayı/defteri gerçek ele oturtuyor.
    [DefaultExecutionOrder(200)]
    public class CharacterBody : MonoBehaviour
    {
        const string VisemeClosed = "Viseme_MBP";
        const string VisemeOpen = "Viseme_OO";
        const string VisemeWide = "Viseme_EE";

        static readonly int SpeedParameter = Animator.StringToHash("Speed");

        struct Face
        {
            public SkinnedMeshRenderer renderer;
            public int closed;
            public int open;
            public int wide;
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

        float _nextBlink;
        float _blinkTime = -1f;
        float _openness;
        Vector3 _lastPosition;
        float _speed;
        float _leftWeight;
        float _rightWeight;
        Vector3 _leftTarget;
        Vector3 _rightTarget;

        public DishasterPlayer Player => _player;

        public void Setup(DishasterPlayer player, RuntimeAnimatorController controller, float lidAngle, bool mouthTied)
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

            _animator = GetComponent<Animator>();
            _animator.runtimeAnimatorController = controller;
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

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

        // ── IK ──────────────────────────────────────────────────────────────

        void OnAnimatorIK(int layerIndex)
        {
            if (_player == null) return;

            _animator.bodyPosition = transform.TransformPoint(new Vector3(0f, _bodyHeight, 0f));
            _animator.bodyRotation = transform.rotation;

            // Kafa ve biraz da gövde bakışı izliyor.
            Transform head = _player.headPivot;
            _animator.SetLookAtWeight(1f, 0.25f, 0.9f, 0f, 0.6f);
            _animator.SetLookAtPosition(head.position + head.forward * 3f);

            // İş yoksa kol animasyonda (idle/yürüme); iş varsa yumuşakça ona uzanıyor.
            GetHandTargets(out Vector3? left, out Vector3? right);
            float blend = 1f - Mathf.Exp(-10f * Time.deltaTime);
            _leftWeight = Mathf.Lerp(_leftWeight, left.HasValue ? 1f : 0f, blend);
            _rightWeight = Mathf.Lerp(_rightWeight, right.HasValue ? 1f : 0f, blend);
            if (left.HasValue) _leftTarget = left.Value;
            if (right.HasValue) _rightTarget = right.Value;

            SetHand(AvatarIKGoal.LeftHand, _leftWeight, _leftTarget);
            SetHand(AvatarIKGoal.RightHand, _rightWeight, _rightTarget);
        }

        void SetHand(AvatarIKGoal goal, float weight, Vector3 position)
        {
            _animator.SetIKPositionWeight(goal, weight);
            _animator.SetIKPosition(goal, position);
        }

        // Yürüme hızı: konum farkından (uzaktaki oyuncular NetworkTransform'la geliyor, onlarda da çalışıyor).
        void Update()
        {
            if (_player == null) return;

            Vector3 delta = _player.transform.position - _lastPosition;
            _lastPosition = _player.transform.position;
            delta.y = 0f;

            float speed = Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;
            _speed = Mathf.Lerp(_speed, Mathf.Min(speed, 8f), 1f - Mathf.Exp(-8f * Time.deltaTime));
            _animator.SetFloat(SpeedParameter, _speed < 0.15f ? 0f : _speed);
        }

        /// <summary>Rolün o anki işi: elin gitmesi gereken yer (yoksa boşta).</summary>
        void GetHandTargets(out Vector3? left, out Vector3? right)
        {
            left = null;
            right = null;

            NotebookRole notebook = NotebookRole.Instance;
            if (notebook != null && notebook.Owner == _player)
            {
                left = notebook.LeftGripPoint;
                if (notebook.IsDrawing) right = notebook.PenGripPoint;
                return;
            }

            if (_player.role == MonkeyRoles.LowVision)
            {
                // El dokunduğu yere uzanıyor; tuttuğu eşya (uzaktakilerde) LateUpdate'te gerçek avuca oturtuluyor.
                Transform hand = LowVisionRole.Hand;
                if (hand != null && hand.gameObject.activeInHierarchy) right = hand.position;
            }
        }

        // ── YÜZ ─────────────────────────────────────────────────────────────

        // LateUpdate: Animator kapakları ve ağzı ezmesin.
        float FootY(Transform foot) => transform.InverseTransformPoint(foot.position).y;

        /// <summary>İnsan iskeletinde avucun ortası (bilek ile orta parmak kökü arası).</summary>
        Vector3? Palm(bool left)
        {
            Transform wrist = _animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            Transform knuckle = _animator.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            if (wrist == null) return null;
            return knuckle != null ? (wrist.position + knuckle.position) * 0.5f : wrist.position;
        }

        /// <summary>
        /// Uzaktaki oyuncunun elinde duran şey gerçekten elinde görünsün: IK kol boyunu aşan hedefe tam yetişemiyor,
        /// eşya/defter havada kalıyordu. Kol yerleştikten sonra eşya (eğimi ve bıçak inişiyle) avuca, defter sol avuca
        /// taşınıyor. Sadece görüntü; kendi ekranında FPS elleri var, orada dokunulmuyor.
        /// </summary>
        void AttachHeldToHands()
        {
            if (_player.isLocalPlayer) return;

            NotebookRole notebook = NotebookRole.Instance;
            if (notebook != null && notebook.Owner == _player)
            {
                Vector3? palm = Palm(true);
                if (palm.HasValue) notebook.notebook.position += palm.Value - notebook.LeftGripPoint;
                return;
            }

            if (_player.role != MonkeyRoles.LowVision) return;

            KitchenCounter counter = KitchenCounter.Instance;
            if (counter == null || counter.Held == null || !counter.TryGetHeldGrip(out Vector3 grip, out _)) return;

            Vector3? hand = Palm(false);
            if (hand.HasValue) counter.Held.transform.position += hand.Value - grip;
        }

        void LateUpdate()
        {
            if (_player != null && _animator != null) AttachHeldToHands();

            if (_footL != null && _footR != null)
                _bodyHeight += (_footHeight - Mathf.Min(FootY(_footL), FootY(_footR))) * 0.5f;

            UpdateBlink();

            float level = _mouthTied ? 0f : VoiceChat.LevelOf(_player);
            float target = Mathf.Clamp01((level - 0.02f) * 5f);
            // Açılış hızlı, kapanış biraz yavaş: kelimeler arası titremesin.
            _openness = Mathf.MoveTowards(_openness, target, Time.deltaTime * (target > _openness ? 14f : 7f));
            ApplyMouth(_openness);
        }

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
