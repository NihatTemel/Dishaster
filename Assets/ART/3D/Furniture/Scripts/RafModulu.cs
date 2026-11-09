// Katli duvar rafi (Ozgur / art). PF_Duvar_Rafi_150 prefabinin kokunde durur.
// Her kat: 150 cm ahsap raf + iki celik tutacak (duvardaki direk ve rafin altindaki kol).
// Direkler kat arasi 40 cm iken ust uste birlesip tek dikme gibi gorunur.
// Pivot: rafin sol-arka ucu, yerde (duvar cizgisi). Raf duvardan 10 cm onde durur (direklerin onunde).
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class RafModulu : MonoBehaviour
{
    [Header("Secenekler")]
    [Range(1, 4)] public int katSayisi = 2;
    [Tooltip("En alttaki rafin alt yuzunun yerden yuksekligi (m)")]
    public float ilkKatYuksekligi = 1.4f;
    [Tooltip("Iki raf arasi (m). 0,40'ta direkler ust uste tam birlesir.")]
    [Range(0.3f, 0.6f)] public float katArasi = 0.4f;

    [HideInInspector] public GameObject[] katlar = new GameObject[4];   // kurulum doldurur

    public void Uygula()
    {
        if (katlar == null) return;
        for (int i = 0; i < katlar.Length; i++)
        {
            var k = katlar[i];
            if (!k) continue;
            bool acik = i < katSayisi;
            var pos = new Vector3(0f, ilkKatYuksekligi + i * katArasi, 0f);
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Undo.RecordObject(k, "Raf modulu");
                Undo.RecordObject(k.transform, "Raf modulu");
            }
#endif
            if (k.activeSelf != acik) k.SetActive(acik);
            k.transform.localPosition = pos;
#if UNITY_EDITOR
            if (!Application.isPlaying && PrefabUtility.IsPartOfPrefabInstance(k))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(k);
                PrefabUtility.RecordPrefabInstancePropertyModifications(k.transform);
            }
#endif
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        EditorApplication.delayCall -= GecikmeliUygula;
        EditorApplication.delayCall += GecikmeliUygula;
    }

    void GecikmeliUygula()
    {
        if (this && !Application.isPlaying) Uygula();
    }
#endif
}
