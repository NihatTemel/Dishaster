using System.Collections.Generic;
using UnityEngine;

namespace Dishaster
{
    /// <summary>Görmeyenin sürekli hattını gördüğü obje tipleri. Yeni tip eklenince LowVisionRole.OutlineLevel'a da ekle.</summary>
    public enum LowVisionOutlineType
    {
        Wall,
        Table,
    }

    /// <summary>
    /// Görmeyen oyuncu bu objenin hatlarını el değmeden, uzaktan da sürekli görüyor (bkz. LowVisionEffect).
    /// Altındaki mesh'ler sayılıyor; üstündeki eşyalar (KitchenItem) sayılmıyor, onlar yine karanlıkta.
    /// </summary>
    [DisallowMultipleComponent]
    public class LowVisionOutline : MonoBehaviour
    {
        static readonly List<LowVisionOutline> s_all = new List<LowVisionOutline>();

        public static IReadOnlyList<LowVisionOutline> All => s_all;

        public LowVisionOutlineType type;

        readonly List<MeshRenderer> _renderers = new List<MeshRenderer>();

        public IReadOnlyList<MeshRenderer> Renderers => _renderers;

        void Awake()
        {
            foreach (MeshRenderer meshRenderer in GetComponentsInChildren<MeshRenderer>(true))
                if (meshRenderer.GetComponentInParent<KitchenItem>() == null)
                    _renderers.Add(meshRenderer);
        }

        void OnEnable()
        {
            s_all.Add(this);
        }

        void OnDisable()
        {
            s_all.Remove(this);
        }
    }
}
