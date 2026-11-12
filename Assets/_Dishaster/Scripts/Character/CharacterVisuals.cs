using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.Rendering;

namespace Dishaster
{
    /// <summary>
    /// Karakter giydirme (prefab). Oyun oyuncusu doğdukça rolüne göre art'ın karakterini (Assets/ART/3D/Karakterler,
    /// bkz. KARAKTERLER.md) kapsülün yerine koyuyor: Görmedim = Aşçı, Duymadım = Yorumcu, Bilmiyorum = Çizer.
    /// Kamera karakterin göz hizasında (herkeste aynı). Kendi ekranında da kendi gövden görünüyor: başkalarının
    /// gördüğü kollar ve eller (defter, kalem, eşya iki tarafta aynı yerde); sadece kafa ve takıları gölge olarak.
    /// Her makinede ayrı ve sadece görüntü: ağa bir şey gitmiyor.
    /// </summary>
    public class CharacterVisuals : MonoBehaviour
    {
        // Kendi ekranında gizlenen parçalar (kameranın içinde kalıyorlar): kafa, gözler, saç, şapka, gözlük, kulaklık, bant.
        static readonly string[] HeadParts = { "Head", "Eyes", "Hair", "Hat", "Glasses", "Bandana", "MouthBand" };

        [Serializable]
        public class Character
        {
            public string name;
            public GameObject body;
            public GameObject hands;
            public AnimationClip handPoses;
            [Tooltip("Göz kapağının tam kapanma açısı (KARAKTERLER.md).")]
            public float lidAngle = -90f;
        }

        [Tooltip("MonkeyRoles sırasıyla: Görmedim, Duymadım, Bilmiyorum.")]
        public Character[] characters = new Character[MonkeyRoles.Count];
        [Tooltip("IK geçişi açık, boş durumlu controller: eller ve bakış koddan.")]
        public RuntimeAnimatorController controller;
        [Tooltip("Model ayak tabanından; oyuncunun kökü kapsülün ortasında.")]
        public float feetOffset = -1f;

        readonly HashSet<uint> _dressed = new HashSet<uint>();

        void Update()
        {
            foreach (NetworkIdentity identity in NetworkClient.spawned.Values)
            {
                if (identity == null || _dressed.Contains(identity.netId)) continue;
                if (!identity.TryGetComponent(out DishasterPlayer player)) continue;

                _dressed.Add(identity.netId);
                Dress(player);
            }
        }

        void Dress(DishasterPlayer player)
        {
            if (player.role < 0 || player.role >= characters.Length) return;

            Character character = characters[player.role];
            if (character == null || character.body == null) return;

            GameObject body = Instantiate(character.body, player.transform);
            body.name = "Character";
            body.transform.localPosition = new Vector3(0f, feetOffset, 0f);
            body.transform.localRotation = Quaternion.identity;

            // Kamera (ve kafa pivotu) karakterin gözlerinin hizasında: her makinede aynı, rolden belli.
            AlignEyes(player, body.transform);

            body.AddComponent<CharacterBody>().Setup(player, controller, character.lidAngle, MonkeyRoles.IsMouthTied(player.role), character.hands, character.handPoses);

            // Kapsül ve vizör artık karakterin kendisi.
            player.bodyRenderer.enabled = false;
            player.visorRenderer.enabled = false;

            if (!player.isLocalPlayer) return;

            foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
            {
                if (IsHeadPart(renderer.name)) renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;

                // Kollar ve eller kendi ekranında defterin/kafanın gölgesinde kararmasın.
                if (renderer.name.Contains("Arms")) renderer.receiveShadows = false;

                // Kollar kameranın önüne kalkınca gövdenin sınır kutusundan çıkıyor: sınırlar her kare pozdan.
                if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
            }
        }

        static bool IsHeadPart(string name)
        {
            foreach (string part in HeadParts)
                if (name.Contains(part))
                    return true;
            return false;
        }

        /// <summary>Kafa pivotu gözlerin (Eye_L/Eye_R, yoksa göz kapakları) ortasında: yükseklik ve önde.</summary>
        static void AlignEyes(DishasterPlayer player, Transform body)
        {
            Transform left = CharacterBody.FindBone(body, "Eye_L") ?? CharacterBody.FindBone(body, "Lid_L");
            Transform right = CharacterBody.FindBone(body, "Eye_R") ?? CharacterBody.FindBone(body, "Lid_R");
            if (left == null || right == null) return;

            Vector3 eyes = player.transform.InverseTransformPoint((left.position + right.position) * 0.5f);
            player.headPivot.localPosition = new Vector3(player.headPivot.localPosition.x, eyes.y, eyes.z);
            // Çömelince bundan iniyor.
            player.StandEyeHeight = eyes.y;
        }
    }
}
