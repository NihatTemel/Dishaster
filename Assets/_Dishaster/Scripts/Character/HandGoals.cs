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

            // Görmedim kendi ekranında iki eli de boştayken onları göğüs önünde görüyor (yoklar gibi öne uzanmış); sağ
            // el bir şeye dokunur ya da tutarken sol el yana iniyor (ekranı kapatmasın). Başkaları bunu görmüyor
            // (onlarda kollar yanda sallanıyor). Diğer rollerde boştaki kollar yanda, aşağı bakınca görünüyor.
            if (player.isLocalPlayer && !player.ControlBlocked && player.role == MonkeyRoles.LowVision && !right.active)
            {
                left = Ready(player, true);
                right = Ready(player, false);
            }
        }

        /// <summary>
        /// Sol el defteri arkadan taşıyor (yer ve yön NotebookRole'dan, deftere sabit): avuç arka kapağa yaslı,
        /// parmaklar kapak boyunca yukarı, başparmak sol kenardan önde. Defter dönünce el de aynen dönüyor.
        /// </summary>
        static HandGoal NotebookGrip(NotebookRole notebook)
        {
            Quaternion grip = notebook.LeftGripRotation;
            return new HandGoal
            {
                active = true,
                palm = notebook.LeftGripPoint,
                fingers = grip * Vector3.forward,
                palmFacing = grip * Vector3.up,
                closedFrame = FrameGrip,
                curl = notebook.LeftGripCurl,
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

        // Bıçak: parmaklar sapın çevresinde ağız tarafına doğru kıvrık (ağız çizgisinden bu kadar aşağı), sap yumruğun
        // içinden çapraz geçiyor; avucun ortası sapın dışında (elin sırtı dışa) ve biraz gerisinde (bilek tarafı).
        const float KnifeFingerAngle = 55f;
        const float KnifePalmOut = 0.03f;
        const float KnifePalmBack = 0.05f;
        const float KnifeCurl = 0.85f;
        // Yandan: avucun ortası eşyanın yan yüzünden bu kadar dışarıda (avuç derisi yüzeye değsin), eşyanın ortasından
        // biraz geride (parmaklar önünü sarsın).
        const float SideGap = 0.018f;
        const float SideBack = 0.025f;
        // Saplı kapta (ölçü bardağı) yumruk dikey sapı kavrıyor: avucun ortası sapın dışında ve gerisinde.
        const float HandlePalmOut = 0.028f;
        const float HandlePalmBack = 0.045f;
        // Avuçta: avucun ortası eşyanın tabanının bu kadar altında, biraz da gerisinde (eşya parmaklara yakın oturuyor).
        const float PalmUnder = 0.02f;
        const float PalmBack = 0.015f;

        /// <summary>
        /// Tutulan eşyaya göre kavrayış (KitchenItem.Grip): bıçak sapından yumrukla, ağız aşağı; kap ve uzun şeyler
        /// (kavanoz) yandan sarılarak; küçük şeyler (meyve, sebze, dilim) avucun içinde, el altında, parmaklar
        /// etrafına kıvrık. Eşya eğilince (dökerken) ya da bıçak inince el de onunla gidiyor.
        /// </summary>
        public static HandGoal Grip(KitchenItem item)
        {
            Transform t = item.transform;
            Bounds bounds = item.LocalBox;

            switch (item.Grip)
            {
                case KitchenGrip.Knife:
                {
                    Transform handle = t.Find("Handle");
                    Vector3 handlePoint = handle != null ? handle.position : t.TransformPoint(bounds.center);
                    Vector3 along = t.forward;
                    Vector3 edge = t.TransformDirection(item.EdgeLocal).normalized;
                    Vector3 outward = Vector3.Cross(-edge, along).normalized;
                    float angle = KnifeFingerAngle * Mathf.Deg2Rad;
                    Vector3 fingers = along * Mathf.Cos(angle) + edge * Mathf.Sin(angle);
                    return new HandGoal
                    {
                        active = true,
                        palm = handlePoint + outward * KnifePalmOut - fingers * KnifePalmBack,
                        fingers = fingers,
                        palmFacing = -outward,
                        closedFrame = FrameFist,
                        curl = KnifeCurl,
                    };
                }

                case KitchenGrip.Side:
                {
                    // Saplıysa yumruk sapta: parmaklar ileri, avuç içe, yumruğun deliği dikey (sap gibi).
                    Transform handle = t.Find("Handle");
                    if (handle != null)
                    {
                        return new HandGoal
                        {
                            active = true,
                            palm = handle.position + t.right * HandlePalmOut - t.forward * HandlePalmBack,
                            fingers = t.forward,
                            palmFacing = -t.right,
                            closedFrame = FrameFist,
                            curl = 0.8f,
                        };
                    }

                    // Avuç eşyanın sağ yüzünde, parmaklar ileri ve önünü sarıyor, başparmak üstte; ince eşyada daha
                    // çok kapanıyor.
                    float width = Mathf.Max(bounds.size.x, bounds.size.z);
                    return new HandGoal
                    {
                        active = true,
                        palm = t.TransformPoint(new Vector3(bounds.max.x + SideGap, bounds.center.y, bounds.center.z - SideBack)),
                        fingers = t.forward,
                        palmFacing = -t.right,
                        closedFrame = FrameFist,
                        curl = Mathf.Clamp(0.78f - width * 2.5f, 0.3f, 0.65f),
                    };
                }

                default:
                {
                    // Avuç yukarı ve biraz içe, parmaklar ileri-içe; geniş eşyada parmaklar daha az kapanıyor.
                    Vector3 facing = (t.up - t.right * 0.3f).normalized;
                    Vector3 fingers = (t.forward - t.right * 0.25f + t.up * 0.1f).normalized;
                    Vector3 bottom = t.TransformPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
                    float width = Mathf.Max(bounds.size.x, bounds.size.z);
                    return new HandGoal
                    {
                        active = true,
                        palm = bottom - facing * PalmUnder - fingers * PalmBack,
                        fingers = fingers,
                        palmFacing = facing,
                        closedFrame = FrameGrip,
                        curl = Mathf.Clamp(0.78f - width * 1.5f, 0.3f, 0.68f),
                    };
                }
            }
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

        /// <summary>Bir parmak kemiğinin dönüşü: açık ile kapalı arasında, curl kadar.</summary>
        public static Quaternion Finger(Quaternion open, Quaternion closed, HandGoal goal)
        {
            return Quaternion.Slerp(open, closed, goal.curl);
        }
    }
}
