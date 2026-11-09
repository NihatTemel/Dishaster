using UnityEngine;

namespace Dishaster
{
    /// <summary>
    /// Kendi kameranda görünen FPS elleri (art'ın SK_*_FPHands'i, bkz. KARAKTERLER.md). Kameraya (0, -1.60, -0.06)
    /// ile bağlı; her kare FP_Poses klibinden poz alınıyor (8 tutma, 1 açık; iki el ayrı kare alabiliyor), sonra el
    /// rolün işine taşınıyor. Elin yönü açıkça veriliyor: parmakların gittiği yön ve avucun baktığı yön kemiklerden
    /// ölçülüp istenene çevriliyor (kemik eksenlerine bağlı değil). Ön kol avucun etrafında dönüp avuç hedefe kayıyor;
    /// kol (ceket) bilekten omza uzanıp bilek kesiğini örtüyor.
    /// Bilmiyorum: sol el defteri alttan kavrıyor (çevirirken defterle döner), sağ el sadece çizerken kalemi tutuyor.
    /// Görmedim: sağ el dokunduğu yerde/eşyayı tutuyor, sol el solda boşta. Duymadım: iki el boşta. İncelerken gizli.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class FirstPersonHands : MonoBehaviour
    {
        const float FrameRate = 24f;
        public const int FrameOpen = 1;
        public const int FrameFist = 2;
        public const int FrameGrip = 8;

        // Parmakların kapanma/açılma hızı (saniyede): tutunca ~0.15 sn'de kapanıyor.
        const float CurlSpeed = 7f;

        static readonly Vector3 CameraOffset = new Vector3(0f, -1.60f, -0.06f);

        // Kameraya göre omuzlar: kol buradan bileğe uzanıyor; yön verilmeyen el de buradan hedefe bakıyor.
        static readonly Vector3 LeftShoulder = new Vector3(-0.55f, -0.65f, 0.1f);
        static readonly Vector3 RightShoulder = new Vector3(0.55f, -0.65f, 0.1f);

        // Kol: bilekten omza doğru, karakterin ceketinden. Bilek kesiğindeki kapağı kapatıyor, eli gövdeye bağlıyor.
        const float SleeveLength = 0.22f;
        const float SleeveRadius = 0.05f;
        const float SleeveOverlap = 0.03f;

        /// <summary>Elin istenen hali: avucun yeri, parmakların yönü, avucun baktığı yön (yönler boşsa omuzdan bakar).</summary>
        struct Pose
        {
            public bool visible;
            public bool placed;
            public Vector3 palm;
            public Vector3 fingers;
            public Vector3 palmFacing;
            // Parmaklar: açık kare ile kapalı kare arasında, curl kadar (0 açık, 1 kapalı).
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
            sleeve.transform.localScale = new Vector3(SleeveRadius * 2f, SleeveLength / 2f, SleeveRadius * 2f);

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
            var left = new Pose { visible = true, closedFrame = FrameGrip, curl = 0.45f };
            var right = new Pose { visible = true, closedFrame = FrameGrip, curl = 0.45f };

            NotebookRole notebook = NotebookRole.Instance;
            if (notebook != null && notebook.Owner == _player)
            {
                // Sol el defteri alttan kavrıyor: avuç yukarı (alt kenara), parmaklar kapağın arkasına kapanmış.
                Transform book = notebook.notebook;
                left.placed = true;
                left.palm = notebook.LeftGripPoint;
                left.fingers = book.forward * 0.75f + book.up * 0.45f;
                left.palmFacing = book.up;
                left.curl = 1f;

                // Sağ el sadece çizerken: kalemi yumrukla kavrıyor, parmaklar kalemin ucuna doğru.
                right.visible = notebook.IsDrawing;
                if (notebook.IsDrawing)
                {
                    right.placed = true;
                    right.palm = notebook.PenGripPoint;
                    right.fingers = notebook.PenTip - notebook.PenGripPoint;
                    right.palmFacing = -book.right;
                    right.closedFrame = FrameFist;
                    right.curl = 0.8f;
                }
            }
            else if (_player.role == MonkeyRoles.LowVision)
            {
                KitchenCounter counter = KitchenCounter.Instance;
                right.placed = true;

                if (counter != null && counter.Held != null)
                {
                    Grip(counter.Held, ref right);
                }
                else
                {
                    // Açık el, el sırtı yukarıda, dokunduğu yere uzanıyor: parmaklar açılıyor.
                    Transform reach = LowVisionRole.Hand;
                    right.palm = reach != null ? reach.position : view.TransformPoint(new Vector3(0.15f, -0.25f, 0.45f));
                    right.fingers = view.forward - view.up * 0.35f;
                    right.palmFacing = -view.up;
                    right.curl = 0f;
                }
            }

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
            _poses.SampleAnimation(gameObject, FrameOpen / FrameRate);
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
        /// Tutulan eşyaya göre kavrayış: bıçak sapından yumrukla (bıçak ileri, parmaklar sapın etrafında),
        /// kap/kutu yandan sarılarak, küçük şeyler (dilim, yumurta, meyve) üstten tutularak. Eşya eğilirse el de.
        /// </summary>
        static void Grip(KitchenItem item, ref Pose pose)
        {
            Transform t = item.transform;
            if (item.kind == KitchenItemKind.Knife)
            {
                Transform handle = t.Find("Handle");
                pose.palm = handle != null ? handle.position : t.TransformPoint(new Vector3(0f, 0.015f, -0.08f));
                // Doğrama tutuşu: sap ön kolun devamı, parmaklar sapın altına kapanıyor; bilek omza bakıyor.
                pose.fingers = t.forward - t.up * 0.25f;
                pose.palmFacing = -t.right;
                pose.closedFrame = FrameFist;
                pose.curl = 1f;
                return;
            }

            Bounds bounds = LocalBounds(item);
            if (item.Container != null)
            {
                // Yandan sarıyor: avuç kabın sağ yüzüne, parmaklar ileri.
                Vector3 side = new Vector3(bounds.max.x + 0.012f, bounds.center.y, bounds.center.z);
                pose.palm = t.TransformPoint(side);
                pose.fingers = t.forward;
                pose.palmFacing = -t.right;
                pose.closedFrame = FrameGrip;
                pose.curl = 0.85f;
                return;
            }

            // Küçük şey: avuç aşağı, üstünden kavrıyor.
            pose.palm = t.TransformPoint(new Vector3(bounds.center.x, bounds.max.y + 0.025f, bounds.center.z));
            pose.fingers = t.forward;
            pose.palmFacing = -t.up;
            pose.closedFrame = FrameGrip;
            pose.curl = 0.6f;
        }

        /// <summary>Eşyanın kendi eksenlerinde kutusu (tetik collider'ı ondan kuruluyor).</summary>
        static Bounds LocalBounds(KitchenItem item)
        {
            return item.TryGetComponent(out BoxCollider box) ? new Bounds(box.center, box.size) : new Bounds(Vector3.up * item.height / 2f, new Vector3(0.06f, item.height, 0.06f));
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

            if (pose.placed)
            {
                Vector3 palm = hand.Palm;
                Quaternion turn = pose.fingers.sqrMagnitude > 1e-6f && pose.palmFacing.sqrMagnitude > 1e-6f
                    ? Orient(hand, pose.fingers, pose.palmFacing)
                    : Aim(hand, pose.palm - shoulder);

                // Avucun etrafında dön, sonra avucu hedefe taşı.
                Transform foreArm = hand.foreArm;
                foreArm.SetPositionAndRotation(palm + turn * (foreArm.position - palm), turn * foreArm.rotation);
                foreArm.position += pose.palm - palm;
            }

            PlaceSleeve(hand, shoulder);
        }

        /// <summary>
        /// Şimdiki parmak ve avuç yönünü (kemiklerden) istenene çeviren dönüş. Avucun baktığı yön: işaret→serçe
        /// ekseni ile parmak yönünün çarpımı (sol elde ters), yani kemik eksenlerinden bağımsız.
        /// </summary>
        static Quaternion Orient(Hand hand, Vector3 fingers, Vector3 palmFacing)
        {
            if (hand.knuckle == null || hand.index == null || hand.pinky == null) return Quaternion.identity;

            Vector3 currentFingers = hand.knuckle.position - hand.wrist.position;
            Vector3 thumbSide = hand.index.position - hand.pinky.position;
            Vector3 currentPalm = Vector3.Cross(currentFingers, thumbSide);
            if (hand.left) currentPalm = -currentPalm;
            if (currentFingers.sqrMagnitude < 1e-8f || currentPalm.sqrMagnitude < 1e-8f) return Quaternion.identity;

            Quaternion now = Quaternion.LookRotation(currentFingers, currentPalm);
            Quaternion wanted = Quaternion.LookRotation(fingers, palmFacing);
            return wanted * Quaternion.Inverse(now);
        }

        static Quaternion Aim(Hand hand, Vector3 reach)
        {
            if (hand.knuckle == null) return Quaternion.identity;

            Vector3 along = hand.knuckle.position - hand.wrist.position;
            return along.sqrMagnitude > 1e-6f && reach.sqrMagnitude > 1e-6f ? Quaternion.FromToRotation(along, reach) : Quaternion.identity;
        }

        /// <summary>Kol, bilekten omza doğru uzanıyor (ekranın dışına); bileğe biraz biniyor ki kapak görünmesin.</summary>
        static void PlaceSleeve(Hand hand, Vector3 shoulder)
        {
            if (hand.sleeve == null) return;

            Vector3 along = hand.wrist.position - shoulder;
            if (along.sqrMagnitude < 1e-6f) return;
            along.Normalize();

            hand.sleeve.SetPositionAndRotation(
                hand.wrist.position - along * (SleeveLength / 2f - SleeveOverlap),
                Quaternion.FromToRotation(Vector3.up, along));
        }
    }
}
