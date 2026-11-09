// Ada tezgahi modulu (Ozgur / art). PF_Ada_200 prefabinin kokunde durur.
// 200 x 150 cm tezgah, altinda sirt sirta iki sira 100'luk yuva: on sira (+Z'ye bakar) ve arka sira (-Z'ye bakar).
// Her yuva: Kapali (duz panel, ada yani gibi) ya da Dolap (normal PF_Dolap_100 kopyasi; kapak, cekmece, kulp
// ayari icin hiyerarside o dolabi sec, tezgahi kapali).
// Pivot: tezgahin sol-arka-alt kosesi, yerde (diger dolaplar gibi). Tezgah onde ve arkada 5 cm tasar,
// yanlarda govdeyle hizali. Govdeler 2 x 1,4 m.
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class AdaModulu : MonoBehaviour
{
    public enum Yuva { Kapali, Dolap }

    [Header("On sira (one bakan)")]
    public Yuva onSol = Yuva.Dolap;
    public Yuva onSag = Yuva.Dolap;
    [Header("Arka sira (arkaya bakan)")]
    [Tooltip("Onden bakinca solda kalan arka yuva")]
    public Yuva arkaSol = Yuva.Kapali;
    [Tooltip("Onden bakinca sagda kalan arka yuva")]
    public Yuva arkaSag = Yuva.Kapali;

    // Kurulum doldurur: her yuva icin [Kapali, Dolap]
    [HideInInspector] public GameObject onSolKapali, onSolDolap, onSagKapali, onSagDolap;
    [HideInInspector] public GameObject arkaSolKapali, arkaSolDolap, arkaSagKapali, arkaSagDolap;

    public void Uygula()
    {
        Sec(onSol, onSolKapali, onSolDolap);
        Sec(onSag, onSagKapali, onSagDolap);
        Sec(arkaSol, arkaSolKapali, arkaSolDolap);
        Sec(arkaSag, arkaSagKapali, arkaSagDolap);
    }

    static void Sec(Yuva y, GameObject kapali, GameObject dolap)
    {
        Ac(kapali, y == Yuva.Kapali);
        Ac(dolap, y == Yuva.Dolap);
    }

    static void Ac(GameObject g, bool acik)
    {
        if (!g || g.activeSelf == acik) return;
#if UNITY_EDITOR
        if (!Application.isPlaying) Undo.RecordObject(g, "Ada modulu");
#endif
        g.SetActive(acik);
#if UNITY_EDITOR
        if (!Application.isPlaying && PrefabUtility.IsPartOfPrefabInstance(g))
            PrefabUtility.RecordPrefabInstancePropertyModifications(g);
#endif
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
