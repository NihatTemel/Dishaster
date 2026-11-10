using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Dishaster.EditorTools
{
    /// <summary>
    /// Mutfağın yerleşimi tek yerde (oda, tezgah sıraları, ada, buzdolabı, kiler) ve art parçalarını yerleştirme
    /// yardımcıları. Art prefab'larının pivotu (ODA.md, MOBILYA.md): odaya/öne bakan yüzün sol-arka-alt köşesi;
    /// ön +Z, parça pivottan -X'e uzanıyor. Uzanma yönü kurulumda bounds'la doğrulanıyor (ters çıkarsa yine doğru
    /// yere oturuyor). Yükseklikler (tezgah üstü, raf, ocak gözü) tahmin edilmiyor, mesh'e ışın atılarak ölçülüyor.
    /// </summary>
    internal static class KitchenLayout
    {
        // ── ODA ─────────────────────────────────────────────────────────────

        // İç yüzler: x -5..5, z -3..3. Kuzey arka duvar (pencere, tezgahlar), güney ön duvar (kapı), ön yarı doğma alanı.
        public const float HalfWidth = 5f;
        public const float HalfDepth = 3f;
        public const float WallHeight = 2.8f;

        // Arka sıra (kuzey duvarı, soldan sağa metre): buzdolabı 0-1, dolap 1-2, ocak 2-3, dolap 3-4, evye 4-5,
        // dolap 5-6, yarım dolap 6-6.5. Pencere evyenin tam üstünde (3.5-5.5).
        public const float FridgeCursor = 0f;
        public const float WindowCursor = 3.5f;

        // Ada (iş tezgahı, KitchenCounter): evyenin önünde, arada 1 m'den geniş geçit; önü oyunculara bakıyor.
        public static readonly Vector3 IslandCenter = new Vector3(-0.5f, 0f, 0.35f);

        // Doğma yerleri: ön yarı, adaya bakıyor.
        public static readonly Vector3[] Spawns = { new Vector3(-1.6f, 1.05f, -1.9f), new Vector3(0f, 1.05f, -1.9f), new Vector3(1.6f, 1.05f, -1.9f) };

        // ── ART ─────────────────────────────────────────────────────────────

        public const string RoomArt = "Assets/ART/3D/Room/Prefabs/";
        public const string FurnitureArt = "Assets/ART/3D/Furniture/Prefabs/";
        public const string ToolsArt = "Assets/ART/3D/Tools/Prefabs/";
        public const string PantryArt = "Assets/ART/3D/Pantry/Prefabs/";

        public static GameObject Art(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path + ".prefab");
            if (prefab == null) Debug.LogWarning($"[Dishaster] Art bulunamadı: {path}.prefab (art menüsündeki kurulum çalıştırıldı mı?)");
            return prefab;
        }

        public static bool HasArt(params string[] paths)
        {
            foreach (string path in paths)
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path + ".prefab") == null) return false;
            return true;
        }

        // ── DUVAR ÇERÇEVESİ ─────────────────────────────────────────────────

        public enum Side { North, East, South, West }

        /// <summary>
        /// Odadan duvara bakınca: start sol uç (köşe), along sağa doğru, into odaya doğru; rotation art parçasının
        /// önünü (+Z) odaya, uzandığı yönü (-X) sağa çeviriyor.
        /// </summary>
        public struct WallFrame
        {
            public Vector3 start;
            public Vector3 along;
            public Vector3 into;
            public Quaternion rotation;
            public float length;
        }

        public static WallFrame Frame(Side side)
        {
            switch (side)
            {
                case Side.North:
                    return new WallFrame { start = new Vector3(-HalfWidth, 0f, HalfDepth), along = Vector3.right, into = Vector3.back, rotation = Quaternion.Euler(0f, 180f, 0f), length = HalfWidth * 2f };
                case Side.East:
                    return new WallFrame { start = new Vector3(HalfWidth, 0f, HalfDepth), along = Vector3.back, into = Vector3.left, rotation = Quaternion.Euler(0f, -90f, 0f), length = HalfDepth * 2f };
                case Side.South:
                    return new WallFrame { start = new Vector3(HalfWidth, 0f, -HalfDepth), along = Vector3.left, into = Vector3.forward, rotation = Quaternion.identity, length = HalfWidth * 2f };
                default:
                    return new WallFrame { start = new Vector3(-HalfWidth, 0f, -HalfDepth), along = Vector3.forward, into = Vector3.right, rotation = Quaternion.Euler(0f, 90f, 0f), length = HalfDepth * 2f };
            }
        }

        /// <summary>Çerçevede bir nokta: duvar boyunca, yerden yukarı, duvardan odaya doğru.</summary>
        public static Vector3 Point(WallFrame frame, float cursor, float height = 0f, float inset = 0f)
        {
            return frame.start + frame.along * cursor + Vector3.up * height + frame.into * inset;
        }

        /// <summary>
        /// Art prefab'ını çerçevede [cursor, cursor+width] aralığına, sırtı duvara yaslı koyar (iç içe prefab).
        /// </summary>
        public static GameObject PlaceAlong(Transform parent, GameObject prefab, WallFrame frame, float cursor, float width, float inset = 0f)
        {
            GameObject instance = Instantiate(prefab, parent);
            if (instance == null) return null;

            // Parça pivottan hangi yöne uzanıyor (belgede -X): ölçüp ona göre pivotu sol ya da sağ uca koy.
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Bounds bounds = MeshBounds(instance.transform, instance.transform);
            bool extendsMinusX = bounds.center.x <= 0f;

            Vector3 pivot = Point(frame, extendsMinusX ? cursor : cursor + width, 0f, inset);
            instance.transform.SetPositionAndRotation(pivot, frame.rotation);
            return instance;
        }

        public static GameObject Instantiate(GameObject prefab, Transform parent)
        {
            if (prefab == null) return null;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            return instance;
        }

        // ── ÖLÇÜ ────────────────────────────────────────────────────────────

        /// <summary>Alt mesh'lerin (aktif olanlar) kutusu, 'space' dönüşümünün uzayında.</summary>
        public static Bounds MeshBounds(Transform root, Transform space, bool includeInactive = false)
        {
            bool any = false;
            var bounds = new Bounds();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(includeInactive))
            {
                if (filter.sharedMesh == null) continue;
                if (filter.TryGetComponent(out MeshRenderer meshRenderer) && !meshRenderer.enabled) continue;

                Bounds local = filter.sharedMesh.bounds;
                Vector3 min = local.min;
                Vector3 max = local.max;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(i % 2 == 0 ? min.x : max.x, (i / 2) % 2 == 0 ? min.y : max.y, i / 4 == 0 ? min.z : max.z);
                    Vector3 point = space.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (!any)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }

            return any ? bounds : new Bounds(Vector3.zero, Vector3.one * 0.1f);
        }

        /// <summary>
        /// Yukarıdan aşağı ışın: hedefin mesh'lerine ilk değdiği nokta (geçici MeshCollider'larla). Yükseklikleri
        /// tahmin etmeden ölçmek için (tezgah üstü, raf, ocak gözü, kasa tabanı).
        /// </summary>
        public static bool SurfaceBelow(GameObject target, Vector3 from, float maxDistance, out Vector3 point)
        {
            var temp = new List<MeshCollider>();
            foreach (MeshFilter filter in target.GetComponentsInChildren<MeshFilter>(false))
            {
                if (filter.sharedMesh == null) continue;
                var meshCollider = filter.gameObject.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = filter.sharedMesh;
                temp.Add(meshCollider);
            }

            Physics.SyncTransforms();
            var ray = new Ray(from, Vector3.down);
            float best = float.PositiveInfinity;
            point = from;
            foreach (MeshCollider meshCollider in temp)
            {
                if (meshCollider.Raycast(ray, out RaycastHit hit, maxDistance) && hit.distance < best)
                {
                    best = hit.distance;
                    point = hit.point;
                }
            }

            foreach (MeshCollider meshCollider in temp)
                Object.DestroyImmediate(meshCollider);

            return !float.IsPositiveInfinity(best);
        }

        // ── ÇARPIŞMA VE GÖRMEYENİN HATLARI ──────────────────────────────────

        /// <summary>Objenin kendi uzayında, mesh'lerini saran tek kutu (oyuncu içinden geçmesin, el değsin).</summary>
        public static BoxCollider AddBox(GameObject target)
        {
            Bounds bounds = MeshBounds(target.transform, target.transform);
            var box = target.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size;
            return box;
        }

        /// <summary>Her mesh'e kendi şekliyle çarpışma (buzdolabının içi gibi oyuk yerler).</summary>
        public static void AddMeshColliders(GameObject target)
        {
            foreach (MeshFilter filter in target.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null) continue;
                filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
            }
        }

        /// <summary>Görmeyen bu objenin hatlarını el değmeden, uzaktan da görüyor.</summary>
        public static void Outline(GameObject target, LowVisionOutlineType type)
        {
            if (!target.TryGetComponent(out LowVisionOutline outline)) outline = target.AddComponent<LowVisionOutline>();
            outline.type = type;
        }

        /// <summary>Art'ın renk temasını uygular (kökte MobilyaRengi varsa).</summary>
        public static void Paint(GameObject instance, MobilyaRengi.Renk renk)
        {
            if (instance == null || !instance.TryGetComponent(out MobilyaRengi color)) return;
            color.renk = renk;
            color.Uygula();
        }

        /// <summary>Düz dolap/evye seçenekleri: tezgahın uçları ve ön yüz.</summary>
        public static void Module(GameObject instance, MutfakModulu.TezgahTipi top, MutfakModulu.OnYuzTipi front)
        {
            if (instance == null || !instance.TryGetComponent(out MutfakModulu module)) return;
            module.tezgah = top;
            module.onYuz = front;
            module.Uygula();
        }

        /// <summary>
        /// Art parçasını sarmalayan kök: art kökün altında, tabanının ortası kökün orijininde (eşya prefab'ları için:
        /// KitchenItem'ın kökü tabanında olmalı).
        /// </summary>
        public static GameObject WrapOnBase(string name, GameObject artPrefab, Quaternion rotation)
        {
            var root = new GameObject(name);
            GameObject art = Instantiate(artPrefab, root.transform);
            if (art == null) return root;

            art.transform.localRotation = rotation;
            Bounds bounds = MeshBounds(art.transform, root.transform);
            art.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
            return root;
        }
    }
}
