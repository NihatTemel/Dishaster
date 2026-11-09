// Mobilya renk temasi (Ozgur / art). Dolap, L kose ve duvar dolabi prefablarinin kokunde durur.
// Yalnizca krem boyali yerler (govde, kapak, cekmece govdesi) degisir; tezgah ahsabi, bakir ve celik ayni kalir.
// Renkler oyunun paletinden: Ahsap #9C643B (tezgahla ayni), Adacayi #9AA06A, TozluMavi #A1BBD1,
// Hardal #E4B355, Terrakota #C8553A. Her renk ayri malzeme (M_Furniture_Trim_<Renk>), kurulum olusturur.
// Ic ice dolaplar (L kosedeki dolaplar) da ayni renge gecer.
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class MobilyaRengi : MonoBehaviour
{
    public enum Renk { Krem, Ahsap, Adacayi, TozluMavi, Hardal, Terrakota }

    public Renk renk = Renk.Krem;

    // Renk sirasiyla malzemeler; kurulum doldurur
    [HideInInspector] public Material[] malzemeler = new Material[6];

    public void Uygula()
    {
        if (malzemeler == null || malzemeler.Length == 0) return;
        var hedef = malzemeler[Mathf.Clamp((int)renk, 0, malzemeler.Length - 1)];
        if (!hedef) return;

        foreach (var mr in GetComponentsInChildren<MeshRenderer>(true))
        {
            var su = mr.sharedMaterial;
            if (su == hedef || System.Array.IndexOf(malzemeler, su) < 0) continue;   // baska malzemeye dokunma
#if UNITY_EDITOR
            if (!Application.isPlaying) Undo.RecordObject(mr, "Mobilya rengi");
#endif
            mr.sharedMaterial = hedef;
#if UNITY_EDITOR
            if (!Application.isPlaying && PrefabUtility.IsPartOfPrefabInstance(mr))
                PrefabUtility.RecordPrefabInstancePropertyModifications(mr);
#endif
        }
        // Ic ice dolaplarin kendi renk ayari da ayni olsun (sonra kendi kendine eski rengine donmesin)
        foreach (var r in GetComponentsInChildren<MobilyaRengi>(true))
        {
            if (r == this || r.renk == renk) continue;
#if UNITY_EDITOR
            if (!Application.isPlaying) Undo.RecordObject(r, "Mobilya rengi");
#endif
            r.renk = renk;
#if UNITY_EDITOR
            if (!Application.isPlaying && PrefabUtility.IsPartOfPrefabInstance(r))
                PrefabUtility.RecordPrefabInstancePropertyModifications(r);
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
