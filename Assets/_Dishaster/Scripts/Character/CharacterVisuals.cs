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
    /// Kendi karakterin sadece gölge olarak çiziliyor; onun yerine kameraya kendi FPS ellerin bağlanıyor.
    /// Her makinede ayrı ve sadece görüntü: ağa bir şey gitmiyor.
    /// </summary>
    public class CharacterVisuals : MonoBehaviour
    {
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

            body.AddComponent<CharacterBody>().Setup(player, controller, character.lidAngle, MonkeyRoles.IsMouthTied(player.role));

            // Kapsül ve vizör artık karakterin kendisi.
            player.bodyRenderer.enabled = false;
            player.visorRenderer.enabled = false;

            if (!player.isLocalPlayer) return;

            // Kendi gövdemiz görünmesin ama gölgesi kalsın.
            foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
                renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;

            if (character.hands == null || character.handPoses == null) return;

            GameObject hands = Instantiate(character.hands);
            hands.name = "FirstPersonHands";
            hands.AddComponent<FirstPersonHands>().Setup(player, character.handPoses, SleeveMaterial(body));
        }

        /// <summary>Karakterin ceket malzemesi (FPS kolları için); yoksa gövdenin ilk malzemesi.</summary>
        static Material SleeveMaterial(GameObject body)
        {
            Renderer source = null;
            foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name.Contains("Jacket")) { source = renderer; break; }
                if (source == null) source = renderer;
            }

            if (source == null) return null;

            // Ceket dokusu silindirin UV'sinde karışık durur: aynı malzemenin düz krem (aşçı ceketi) hali.
            var material = new Material(source.sharedMaterial) { mainTexture = null };
            material.SetTexture("_BumpMap", null);
            material.DisableKeyword("_NORMALMAP");
            material.color = new Color(0.92f, 0.87f, 0.77f);
            return material;
        }
    }
}
