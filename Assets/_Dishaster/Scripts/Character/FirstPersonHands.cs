using UnityEngine;

namespace Dishaster
{
    /// <summary>
    /// Kendi kameranda görünen FPS elleri (art'ın SK_*_FPHands'i, bkz. KARAKTERLER.md). Kameraya (0, -1.60, -0.06)
    /// ile bağlı; her kare FP_Poses klibinden parmak pozu alınıyor (açık kare ile tutma/yumruk karesi arasında, kemik
    /// kemik). Elin işi HandGoals'tan: başkalarının gördüğü karakter de aynı hedefe gidiyor, iki taraf eşleşiyor.
    /// Elin yönü açıkça veriliyor (parmakların ve avucun baktığı yön kemiklerden ölçülüp istenene çevriliyor).
    /// İşi olmayan el ekranın alt köşesinde duruyor: sağ el sağda, sol el solda. İşe girerken/çıkarken oraya kayıyor.
    /// Kol (ceket) bilekten ekranın dışındaki omza uzanıyor; uzağa uzanınca o da uzuyor.
    /// Bilmiyorum: sol el defterde, sağ el sadece çizerken. Görmedim: sağ el dokunduğu yerde ya da eşyayı tutuyor.
    /// İncelerken gizli.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class FirstPersonHands : MonoBehaviour
    {
        const float FrameRate = 24f;

        // Parmakların kapanma/açılma hızı (saniyede): tutunca ~0.15 sn'de kapanıyor.
        const float CurlSpeed = 7f;
        // Boştan işe geçiş (saniyede): el köşeden hedefe ~0.2 sn'de kayıyor.
        const float ActiveSpeed = 6f;

        static readonly Vector3 CameraOffset = new Vector3(0f, -1.60f, -0.06f);

        // Kameraya göre omuzlar: aşağıda ve biraz geride (ekranın dışında). Kol bilekten buraya uzanıyor, ekrana
        // alttan giriyor; uzağa uzanınca görüşü kesen kalın bir boru olmuyor.
        static readonly Vector3 LeftShoulder = new Vector3(-0.3f, -0.6f, -0.15f);
        static readonly Vector3 RightShoulder = new Vector3(0.3f, -0.6f, -0.15f);

        // Boştaki el (sağ el; sol el aynası): ekranın alt köşesinde, parmaklar ileri ve biraz içe, avuç aşağı-içe.
        static readonly Vector3 IdlePalm = new Vector3(0.21f, -0.3f, 0.4f);

        // Kol: bilekten omza doğru, karakterin ceketinden. Bilek kesiğindeki kapağı kapatıyor, eli gövdeye bağlıyor.
        const float SleeveRadius = 0.042f;
        const float SleeveOverlap = 0.03f;
        const float MaxSleeveLength = 1.4f;

        /// <summary>Elin bu karedeki hali (dünya uzayında).</summary>
        struct Pose
        {
            public bool visible;
            public Vector3 palm;
            public Quaternion rotation;
            public int closedFrame;
            public float curl;
        }

        class Hand
        {
            public bool left;
            public Transform foreArm;
            public Transform wrist;
            public Transform knuckle;
            public Transform index;
            public Transform pinky;
            public Transform[] bones;
            public Vector3[] bonePositions;
            public Quaternion[] boneRotations;
            public Vector3[] openPositions;
            public Quaternion[] openRotations;
            public float curl;
            public float active;
            public HandGoal lastGoal;
            public Vector3 restPosition;
            public Quaternion restRotation;
            public Transform sleeve;

            public Vector3 Palm => knuckle != null ? (wrist.position + knuckle.position) * 0.5f : wrist.position;
        }

        DishasterPlayer _player;
        AnimationClip _poses;
        Hand _left;
        Hand _right;

        public void Setup(DishasterPlayer player, AnimationClip poses, Material sleeve)
        {
            _player = player;
            _poses = poses;

            // Pozu klipten biz veriyoruz; Animator (kontrolcüsüz) karışmasın.
            if (TryGetComponent(out Animator animator)) animator.enabled = false;

            transform.SetParent(player.playerCamera.transform, false);
            transform.localRotation = Quaternion.identity;
            transform.localPosition = CameraOffset;

            // Kemikler kameraya taşınıyor ama sınır kutusu dinlenme pozunda (kameranın ~1 m altında) kalıyordu:
            // Unity elleri görüş dışı sanıp hiç çizmiyordu. Sınırlar her kare pozdan hesaplansın (az üçgen, ucuz).
            foreach (SkinnedMeshRenderer skin in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skin.updateWhenOffscreen = true;
                skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // Defterin ve kafanın gölgesinde kararmasın.
                skin.receiveShadows = false;
            }

            // Bilek kesiğinin kapağı (rol rengi) kolun ucunda halka gibi görünüyordu: ceketle aynı renk, manşet gibi.
            if (sleeve != null)
            {
                foreach (SkinnedMeshRenderer skin in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Material[] materials = skin.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                        if (materials[i] != null && materials[i].name.Contains("FPCap")) materials[i] = sleeve;
                    skin.sharedMaterials = materials;
                }
            }

            _left = FindHand("Left");
            _right = FindHand("Right");
            _left.sleeve = MakeSleeve("LeftSleeve", sleeve);
            _right.sleeve = MakeSleeve("RightSleeve", sleeve);
        }

        Transform MakeSleeve(string name, Material material)
        {
            if (material == null) return null;

            GameObject sleeve = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            sleeve.name = name;
            Destroy(sleeve.GetComponent<Collider>());
            sleeve.transform.SetParent(transform, false);

            var renderer = sleeve.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return sleeve.transform;
        }

        Hand FindHand(string side)
        {
            var hand = new Hand
            {
                left = side == "Left",
                foreArm = CharacterBody.FindBone(transform, side + "ForeArm"),
                wrist = CharacterBody.FindBone(transform, side + "Hand"),
                knuckle = CharacterBody.FindBone(transform, side + "HandMiddle1"),
                index = CharacterBody.FindBone(transform, side + "HandIndex1"),
                pinky = CharacterBody.FindBone(transform, side + "HandPinky1"),
            };

            if (hand.foreArm != null)
            {
                // Klip ön kolu anahtarlamıyorsa taşıma birikmesin: her kare buradan başlanıyor.
                hand.restPosition = hand.foreArm.localPosition;
                hand.restRotation = hand.foreArm.localRotation;
                hand.bones = hand.foreArm.GetComponentsInChildren<Transform>(true);
                hand.bonePositions = new Vector3[hand.bones.Length];
                hand.boneRotations = new Quaternion[hand.bones.Length];
                hand.openPositions = new Vector3[hand.bones.Length];
                hand.openRotations = new Quaternion[hand.bones.Length];
            }

            return hand;
        }

        // ── KARE ────────────────────────────────────────────────────────────

        void LateUpdate()
        {
            if (_player == null || _left.foreArm == null || _right.foreArm == null) return;

            Transform view = _player.playerCamera.transform;
            HandGoals.For(_player, out HandGoal leftGoal, out HandGoal rightGoal);

            Pose left = Resolve(_left, leftGoal, view);
            Pose right = Resolve(_right, rightGoal, view);

            // Bilmiyorum defteri tek elle tutuyor: sağ el sadece çizerken (kalemde) görünüyor.
            NotebookRole notebook = NotebookRole.Instance;
            if (notebook != null && notebook.Owner == _player && !rightGoal.active && _right.active <= 0f) right.visible = false;

            if (_player.ControlBlocked)
            {
                left.visible = false;
                right.visible = false;
            }

            // Parmaklar yumuşakça kapanıp açılıyor.
            float step = CurlSpeed * Time.deltaTime;
            _left.curl = Mathf.MoveTowards(_left.curl, left.curl, step);
            _right.curl = Mathf.MoveTowards(_right.curl, right.curl, step);

            // Her el: açık kareyi ve kendi kapalı karesini örnekle, kemik kemik curl kadar karıştır.
            Reset(_left);
            Reset(_right);
            _poses.SampleAnimation(gameObject, HandGoals.FrameOpen / FrameRate);
            SaveOpen(_left);
            SaveOpen(_right);

            _poses.SampleAnimation(gameObject, right.closedFrame / FrameRate);
            Blend(_right);
            Save(_right);

            if (left.closedFrame != right.closedFrame) _poses.SampleAnimation(gameObject, left.closedFrame / FrameRate);
            Restore(_right);
            Blend(_left);

            Place(_left, left, view.TransformPoint(LeftShoulder));
            Place(_right, right, view.TransformPoint(RightShoulder));
        }

        /// <summary>
        /// Elin bu karedeki hali: işi varsa hedefi, yoksa ekranın alt köşesi. Arada yumuşak geçiş; iş bitince el
        /// son tuttuğu yerden köşeye dönüyor.
        /// </summary>
        Pose Resolve(Hand hand, HandGoal goal, Transform view)
        {
            if (goal.active) hand.lastGoal = goal;
            hand.active = Mathf.MoveTowards(hand.active, goal.active ? 1f : 0f, ActiveSpeed * Time.deltaTime);

            float side = hand.left ? -1f : 1f;
            Vector3 idlePalm = view.TransformPoint(new Vector3(IdlePalm.x * side, IdlePalm.y, IdlePalm.z));
            Quaternion idleRotation = Quaternion.LookRotation(
                view.forward + view.up * 0.2f - view.right * (0.25f * side),
                -view.up - view.right * (0.6f * side));

            var pose = new Pose { visible = true, palm = idlePalm, rotation = idleRotation, closedFrame = HandGoals.FrameGrip, curl = 0.3f };
            if (hand.active <= 0f) return pose;

            HandGoal work = hand.lastGoal;
            Quaternion workRotation = HandGoals.Wanted(work);
            if (hand.active >= 1f)
            {
                pose.palm = work.palm;
                pose.rotation = workRotation;
            }
            else
            {
                float t = Mathf.SmoothStep(0f, 1f, hand.active);
                pose.palm = Vector3.Lerp(idlePalm, work.palm, t);
                pose.rotation = Quaternion.Slerp(idleRotation, workRotation, t);
            }

            pose.closedFrame = work.closedFrame;
            pose.curl = Mathf.Lerp(pose.curl, work.curl, hand.active);
            return pose;
        }

        static void Reset(Hand hand)
        {
            hand.foreArm.localPosition = hand.restPosition;
            hand.foreArm.localRotation = hand.restRotation;
        }

        static void Save(Hand hand)
        {
            for (int i = 0; i < hand.bones.Length; i++)
            {
                hand.bonePositions[i] = hand.bones[i].localPosition;
                hand.boneRotations[i] = hand.bones[i].localRotation;
            }
        }

        static void SaveOpen(Hand hand)
        {
            for (int i = 0; i < hand.bones.Length; i++)
            {
                hand.openPositions[i] = hand.bones[i].localPosition;
                hand.openRotations[i] = hand.bones[i].localRotation;
            }
        }

        /// <summary>Şimdiki (kapalı) poz ile kayıtlı açık poz arasında, elin curl'ü kadar (kemik kemik).</summary>
        static void Blend(Hand hand)
        {
            for (int i = 0; i < hand.bones.Length; i++)
            {
                Transform bone = hand.bones[i];
                bone.SetLocalPositionAndRotation(
                    Vector3.Lerp(hand.openPositions[i], bone.localPosition, hand.curl),
                    Quaternion.Slerp(hand.openRotations[i], bone.localRotation, hand.curl));
            }
        }

        static void Restore(Hand hand)
        {
            for (int i = 0; i < hand.bones.Length; i++)
                hand.bones[i].SetLocalPositionAndRotation(hand.bonePositions[i], hand.boneRotations[i]);
        }

        static void Place(Hand hand, Pose pose, Vector3 shoulder)
        {
            // Gizlemek: ön kol (ve altındaki el) sıfıra ölçekleniyor.
            hand.foreArm.localScale = pose.visible ? Vector3.one : Vector3.zero;
            if (hand.sleeve != null) hand.sleeve.gameObject.SetActive(pose.visible);
            if (!pose.visible) return;

            // Avucun etrafında dön (parmaklar ve avuç istenen yöne), sonra avucu hedefe taşı.
            if (HandGoals.TryMeasure(hand.wrist, hand.knuckle, hand.index, hand.pinky, hand.left, out Quaternion now))
            {
                Vector3 palm = hand.Palm;
                Quaternion turn = pose.rotation * Quaternion.Inverse(now);
                Transform foreArm = hand.foreArm;
                foreArm.SetPositionAndRotation(palm + turn * (foreArm.position - palm), turn * foreArm.rotation);
                foreArm.position += pose.palm - palm;
            }

            PlaceSleeve(hand, shoulder);
        }

        /// <summary>Kol, bilekten ekranın dışındaki omza uzanıyor; bileğe biraz biniyor ki kesik görünmesin.</summary>
        static void PlaceSleeve(Hand hand, Vector3 shoulder)
        {
            if (hand.sleeve == null) return;

            Vector3 wrist = hand.wrist.position;
            Vector3 along = wrist - shoulder;
            float length = along.magnitude;
            if (length < 1e-3f) return;
            along /= length;

            Vector3 start = wrist + along * SleeveOverlap;
            length = Mathf.Min(length + SleeveOverlap, MaxSleeveLength);
            hand.sleeve.SetPositionAndRotation(start - along * (length / 2f), Quaternion.FromToRotation(Vector3.up, along));

            // Silindir 2 birim boyunda: yarısı kadar ölçek. Ebeveyn (eller) ölçeksiz.
            hand.sleeve.localScale = new Vector3(SleeveRadius * 2f, length / 2f, SleeveRadius * 2f);
        }
    }
}
