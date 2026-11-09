using UnityEngine;

namespace Dishaster
{
    /// <summary>Bir elin işi: avucun yeri, parmakların gittiği yön, avucun baktığı yön ve parmakların kapanışı.</summary>
    public struct HandGoal
    {
        public bool active;
        public Vector3 palm;
        public Vector3 fingers;
        public Vector3 palmFacing;
        // FP_Poses'ta kapalı kare (8 tutma, 2 yumruk); parmaklar açık kare (1) ile bu kare arasında, curl kadar.
        public int closedFrame;
        public float curl;
    }

    /// <summary>
    /// Rolün o anki işine göre iki elin hedefi. Kendi ekranındaki FPS elleri ve başkalarının gördüğü karakter aynı
    /// hedefi kullanıyor: defter, kalem, tutulan eşya ve dokunulan yer iki tarafta da aynı yerde, aynı tutuşla.
    /// İşi olmayan el boşta (FPS'te ekranın alt köşesinde, karakterde animasyonla yanda sallanıyor).
    /// </summary>
    public static class HandGoals
    {
        public const int FrameOpen = 1;
        public const int FrameFist = 2;
        public const int FrameGrip = 8;

        public static void For(DishasterPlayer player, out HandGoal left, out HandGoal right)
        {
            left = default;
            right = default;
            if (player == null) return;

            NotebookRole notebook = NotebookRole.Instance;
            if (notebook != null && notebook.Owner == player)
            {
                // Sol el defteri alttan taşıyor: avuç yukarı, parmaklar arkaya uzanıp kapağın arkasını sarıyor,
                // başparmak önde. Gösterirken defter 180° dönüyor ama el sadece çeyrek (bilek bundan fazlasını doğal
                // yapamıyor): parmaklar dışarı (sola) bakıyor, tepsi tutar gibi, uçları kapağın arkasına yaslı.
                Transform book = notebook.notebook;
                float turn = notebook.TurnFraction;
                left = new HandGoal
                {
                    active = true,
                    palm = notebook.LeftGripPoint,
                    fingers = Quaternion.AngleAxis(90f * turn, book.up) * book.forward,
                    palmFacing = book.up,
                    // İnce kenara sıkı sarılsın: yumruk karesine doğru; gösterirken gevşiyor (tepsi gibi taşıyor).
                    closedFrame = FrameFist,
                    curl = Mathf.Lerp(0.65f, 0.3f, turn),
                };

                // Sağ el sadece çizerken: kalemi yumrukla kavrıyor, parmaklar kalemin ucuna doğru.
                if (notebook.IsDrawing)
                {
                    right = new HandGoal
                    {
                        active = true,
                        palm = notebook.PenGripPoint,
                        fingers = notebook.PenTip - notebook.PenGripPoint,
                        palmFacing = -book.right,
                        closedFrame = FrameFist,
                        curl = 0.8f,
                    };
                }

                return;
            }

            if (player.role != MonkeyRoles.LowVision) return;

            KitchenCounter counter = KitchenCounter.Instance;
            if (counter != null && counter.Held != null)
            {
                right = Grip(counter.Held);
                return;
            }

            // Bir yere dokunuyor: el açık, avuç yüzeye, parmaklar ileri-aşağı (yokluyor).
            Transform hand = LowVisionRole.Hand;
            if (hand != null && hand.gameObject.activeInHierarchy && LowVisionRole.Touching)
            {
                Vector3 forward = hand.forward;
                right = new HandGoal
                {
                    active = true,
                    palm = hand.position,
                    fingers = forward - Vector3.up * 0.3f,
                    palmFacing = -Vector3.up + forward * 0.15f,
                    closedFrame = FrameGrip,
                    curl = 0.15f,
                };
            }
        }

        /// <summary>
        /// Tutulan eşyaya göre kavrayış: bıçak sapından yumrukla (sap ön kolun devamı), kap/kutu yandan sarılarak,
        /// küçük şeyler (dilim, meyve) üstten. Eşya eğilince (dökerken) ya da bıçak inince el de onunla gidiyor.
        /// </summary>
        public static HandGoal Grip(KitchenItem item)
        {
            Transform t = item.transform;
            if (item.kind == KitchenItemKind.Knife)
            {
                Transform handle = t.Find("Handle");
                return new HandGoal
                {
                    active = true,
                    palm = handle != null ? handle.position : t.TransformPoint(new Vector3(0f, 0.015f, -0.08f)),
                    fingers = t.forward - t.up * 0.25f,
                    palmFacing = -t.right,
                    closedFrame = FrameFist,
                    curl = 1f,
                };
            }

            Bounds bounds = LocalBounds(item);
            if (item.Container != null)
            {
                // Yandan sarıyor: avuç kabın sağ yüzüne, parmaklar ileri, başparmak üstte.
                return new HandGoal
                {
                    active = true,
                    palm = t.TransformPoint(new Vector3(bounds.max.x + 0.012f, bounds.center.y, bounds.center.z)),
                    fingers = t.forward,
                    palmFacing = -t.right,
                    closedFrame = FrameGrip,
                    curl = 0.85f,
                };
            }

            // Küçük şey: avuç aşağı, üstünden kavrıyor.
            return new HandGoal
            {
                active = true,
                palm = t.TransformPoint(new Vector3(bounds.center.x, bounds.max.y + 0.025f, bounds.center.z)),
                fingers = t.forward,
                palmFacing = -t.up,
                closedFrame = FrameGrip,
                curl = 0.6f,
            };
        }

        /// <summary>Eşyanın kendi eksenlerinde kutusu (tetik collider'ı ondan kuruluyor).</summary>
        static Bounds LocalBounds(KitchenItem item)
        {
            return item.TryGetComponent(out BoxCollider box)
                ? new Bounds(box.center, box.size)
                : new Bounds(Vector3.up * item.height / 2f, new Vector3(0.06f, item.height, 0.06f));
        }

        /// <summary>
        /// Elin şimdiki yönü (kemik eksenlerinden bağımsız, konumlardan): parmaklar bilekten orta parmak köküne,
        /// avuç bu yön ile işaret→serçe ekseninin çarpımı (sol elde ters). FPS elinde ve karakterde aynı hesap.
        /// </summary>
        public static bool TryMeasure(Transform wrist, Transform knuckle, Transform index, Transform pinky, bool left, out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            if (wrist == null || knuckle == null || index == null || pinky == null) return false;

            Vector3 fingers = knuckle.position - wrist.position;
            Vector3 palm = Vector3.Cross(fingers, index.position - pinky.position);
            if (left) palm = -palm;
            if (fingers.sqrMagnitude < 1e-8f || palm.sqrMagnitude < 1e-8f) return false;

            rotation = Quaternion.LookRotation(fingers, palm);
            return true;
        }

        /// <summary>İstenen yön (parmaklar ileri, avuç yukarı ekseni).</summary>
        public static Quaternion Wanted(HandGoal goal)
        {
            return goal.fingers.sqrMagnitude > 1e-8f && goal.palmFacing.sqrMagnitude > 1e-8f
                ? Quaternion.LookRotation(goal.fingers, goal.palmFacing)
                : Quaternion.identity;
        }
    }
}
