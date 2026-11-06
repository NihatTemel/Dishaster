using UnityEngine;

namespace Dishaster
{
    /// <summary>
    /// Kendi kameranda görünen FPS elleri (art'ın SK_*_FPHands'i, bkz. KARAKTERLER.md). Kameraya (0, -1.60, -0.06)
    /// ile bağlı; her kare FP_Poses klibinden bir poz alınıyor (8 tutma, 1 açık), sonra el rolün işine taşınıyor:
    /// ön kol kemiği, avucun ortası hedefe gelecek kadar kaydırılıp gerekirse avucun etrafında döndürülüyor.
    /// Bilmiyorum: sol el defteri tutuyor, sağ el sadece çizerken kalemde. Görmedim: sağ el dokunduğu yerde, sol el yok.
    /// Duymadım: iki el boşta. İncelerken eller gizli.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class FirstPersonHands : MonoBehaviour
    {
        const float FrameRate = 24f;
        public const int FrameOpen = 1;
        public const int FrameGrip = 8;

        static readonly Vector3 CameraOffset = new Vector3(0f, -1.60f, -0.06f);

        class Hand
        {
            public Transform foreArm;
            public Transform wrist;
            public Transform knuckle;
            public Vector3 restPosition;
            public Quaternion restRotation;

            public Vector3 Palm => knuckle != null ? (wrist.position + knuckle.position) * 0.5f : wrist.position;
        }

        DishasterPlayer _player;
        AnimationClip _poses;
        Hand _left;
        Hand _right;

        public void Setup(DishasterPlayer player, AnimationClip poses)
        {
            _player = player;
            _poses = poses;

            // Pozu klipten biz veriyoruz; Animator (kontrolcüsüz) karışmasın.
            if (TryGetComponent(out Animator animator)) animator.enabled = false;

            transform.SetParent(player.playerCamera.transform, false);
            transform.localRotation = Quaternion.identity;
            transform.localPosition = CameraOffset;

            _left = FindHand("Left");
            _right = FindHand("Right");
        }

        Hand FindHand(string side)
        {
            var hand = new Hand
            {
                foreArm = CharacterBody.FindBone(transform, side + "ForeArm"),
                wrist = CharacterBody.FindBone(transform, side + "Hand"),
                knuckle = CharacterBody.FindBone(transform, side + "HandMiddle1"),
            };

            // Klip ön kolu anahtarlamıyorsa taşıma birikmesin: her kare buradan başlanıyor.
            if (hand.foreArm != null)
            {
                hand.restPosition = hand.foreArm.localPosition;
                hand.restRotation = hand.foreArm.localRotation;
            }

            return hand;
        }

        void LateUpdate()
        {
            if (_player == null || _left.foreArm == null || _right.foreArm == null) return;

            bool hidden = _player.ControlBlocked;
            int frame = FrameGrip;
            Vector3? left = null;
            Vector3? right = null;
            Quaternion leftTurn = Quaternion.identity;
            bool showLeft = !hidden;
            bool showRight = !hidden;

            NotebookRole notebook = NotebookRole.Instance;
            if (notebook != null && notebook.Owner == _player)
            {
                left = notebook.LeftGripPoint;
                leftTurn = notebook.LeftHandTurn;
                showRight &= notebook.IsDrawing;
                if (notebook.IsDrawing) right = notebook.PenGripPoint;
            }
            else if (_player.role == MonkeyRoles.LowVision)
            {
                showLeft = false;
                Transform reach = LowVisionRole.Hand;
                if (reach != null) right = reach.position;

                KitchenCounter counter = KitchenCounter.Instance;
                frame = counter != null && counter.Held != null ? FrameGrip : FrameOpen;
            }

            // Klip kemikleri kameraya göre pozluyor; taşıma bunun üstüne.
            Reset(_left);
            Reset(_right);
            _poses.SampleAnimation(gameObject, frame / FrameRate);

            Place(_left, showLeft, left, leftTurn);
            Place(_right, showRight, right, Quaternion.identity);
        }

        static void Reset(Hand hand)
        {
            hand.foreArm.localPosition = hand.restPosition;
            hand.foreArm.localRotation = hand.restRotation;
        }

        static void Place(Hand hand, bool visible, Vector3? target, Quaternion turn)
        {
            // Gizlemek: ön kol (ve altındaki el) sıfıra ölçekleniyor.
            hand.foreArm.localScale = visible ? Vector3.one : Vector3.zero;
            if (!visible || !target.HasValue) return;

            Vector3 palm = hand.Palm;
            Transform foreArm = hand.foreArm;

            // Avucun etrafında dön, sonra avucu hedefe taşı.
            foreArm.SetPositionAndRotation(palm + turn * (foreArm.position - palm), turn * foreArm.rotation);
            foreArm.position += target.Value - palm;
        }
    }
}
