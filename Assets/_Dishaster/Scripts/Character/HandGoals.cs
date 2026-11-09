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
        // Kanca: parmaklar sadece köklerinden bükülüyor, boğumlar düz (ince bir kenarı arkadan kavramak için:
        // yumruk gibi kapanınca parmaklar defterin içinden geçiyordu).
        public bool hook;
    }

    /// <summary>
    /// Rolün o anki işine göre iki elin hedefi. Karakterin kolları bunu kullanıyor; kendi ekranında da aynı kollar
    /// göründüğü için defter, kalem, tutulan eşya ve dokunulan yer iki tarafta aynı yerde, aynı tutuşla.
    /// İşi olmayan el: kendi ekranında göğüs önünde hazır (ekranın alt köşesinde görünüyor), başkalarında yanda.
    /// </summary>
    public static class HandGoals
    {
        public const int FrameOpen = 1;
        public const int FrameFist = 2;
        public const int FrameGrip = 8;

        // Görmedim'in kendi ekranında boştaki el (sağ el; sol el aynası): göğüs önünde, öne uzanmış (yoklar gibi),
        // bakışın eğimini yarı yarıya izliyor. Gözlere göre.
        static readonly Vector3 ReadyPalm = new Vector3(0.19f, -0.3f, 0.33f);
        const float ReadyPitchFollow = 0.5f;

        public static void For(DishasterPlayer player, out HandGoal left, out HandGoal right)
        {
            left = default;
            right = default;
            if (player == null) return;

            NotebookRole notebook = NotebookRole.Instance;
            bool notebookOwner = notebook != null && notebook.Owner == player;
            if (notebookOwner)
            {
                left = NotebookGrip(notebook);

                // Sağ el sadece çizerken: kalemi yumrukla kavrıyor, parmaklar kalemin ucuna doğru.
                if (notebook.IsDrawing)
                {
                    Transform book = notebook.notebook;
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
            }
            else if (player.role == MonkeyRoles.LowVision)
            {
                KitchenCounter counter = KitchenCounter.Instance;
                if (counter != null && counter.Held != null)
                {
                    right = Grip(counter.Held);
                }
                else
                {
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
            }

            // Görmedim kendi ekranında boştaki ellerini göğüs önünde görüyor (yoklar gibi öne uzanmış). Başkaları bunu
            // görmüyor (onlarda kollar yanda sallanıyor). Diğer rollerde boştaki kollar yanda, aşağı bakınca görünüyor.
            if (player.isLocalPlayer && !player.ControlBlocked && player.role == MonkeyRoles.LowVision)
            {
                if (!left.active) left = Ready(player, true);
                if (!right.active) right = Ready(player, false);
            }
        }

        /// <summary>
        /// Sol el defteri alttan tutuyor: avuç yukarı, alt kenar avucun üstünde, parmaklar kökten bükülüp kapağın
        /// arkasına yaslanıyor (kanca), başparmak önde. Parmaklar ileri-sağa (ön kol gövdenin önüne doğru, bilek düz).
        /// Gösterirken defter avucun üstünde saat yönünün tersine (üstten) 180° dönüyor, el 90°: ön kol dirsekten
        /// dışarı açılıyor (doğal sınır), parmaklar sola bakıyor, el tepsi gibi açılıyor.
        /// </summary>
        static HandGoal NotebookGrip(NotebookRole notebook)
        {
            Transform book = notebook.notebook;
            float turn = notebook.TurnFraction;
            return new HandGoal
            {
                active = true,
                palm = notebook.LeftGripPoint,
                fingers = Quaternion.AngleAxis(Mathf.Lerp(30f, 120f, turn), book.up) * book.forward,
                palmFacing = book.up,
                closedFrame = FrameFist,
                curl = Mathf.Lerp(0.9f, 0.15f, turn),
                hook = true,
            };
        }

        static HandGoal Ready(DishasterPlayer player, bool left)
        {
            Transform head = player.headPivot;
            float pitch = Mathf.DeltaAngle(0f, head.eulerAngles.x);
            Quaternion frame = Quaternion.Euler(pitch * ReadyPitchFollow, player.transform.eulerAngles.y, 0f);
            float side = left ? -1f : 1f;

            Vector3 forward = frame * Vector3.forward;
            Vector3 up = frame * Vector3.up;
            Vector3 right = frame * Vector3.right;
            return new HandGoal
            {
                active = true,
                palm = head.position + frame * new Vector3(ReadyPalm.x * side, ReadyPalm.y, ReadyPalm.z),
                fingers = forward + up * 0.1f - right * (0.25f * side),
                palmFacing = -up - right * (0.5f * side),
                closedFrame = FrameGrip,
                curl = 0.45f,
            };
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
        /// avuç bu yön ile işaret→serçe ekseninin çarpımı (sol elde ters).
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

        /// <summary>
        /// Bir parmak kemiğinin dönüşü: açık ile kapalı arasında. Kancada kök kemik (1) çok, boğumlar (2, 3) az
        /// bükülüyor; başparmak kancada az kapanıyor (önde, kenarın altında).
        /// </summary>
        public static Quaternion Finger(string bone, Quaternion open, Quaternion closed, HandGoal goal)
        {
            float amount = goal.curl;
            if (goal.hook)
            {
                char last = bone[bone.Length - 1];
                float segment = bone.Contains("Thumb") ? 0.25f : last == '1' ? 1f : last == '2' ? 0.18f : 0.1f;
                amount *= segment;
            }

            return Quaternion.Slerp(open, closed, amount);
        }
    }
}
