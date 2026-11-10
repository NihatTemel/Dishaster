// Oda parcalarinin renk secimi (Ozgur / art). Duvar ve zemin prefablarinin kokunde durur.
// Duvar: boya rengi degisir, supurgelik ayni ahsap kalir. Zemin: damanin bir karesi hep krem, oteki secilen renk.
// Renkler oyunun paletinden. Her renk ayri malzeme (M_Room_Wall_<Renk>, M_Room_Floor_<Renk>), kurulum olusturur.
// Butun duvarlari birden boyamak icin prefabin kendisinde (Prefabs klasorunde) rengi degistir; sahnedeki kopyalar izler.
// Kapi ve pencere bu secimden etkilenmez (mobilya ahsabi).
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class OdaRengi : MonoBehaviour
{
    // Duvar ve zemin ayni listeyi kullanir; parcada olmayan renk secilirse uyari verir, degismez.
    // Duvarda: Krem, Bej, Adacayi, TozluMavi, Hardal, Terrakota. Zeminde: Bej, Adacayi, TozluMavi, GriMavi, Hardal, Terrakota.
    public enum Renk { Krem, Bej, Adacayi, TozluMavi, GriMavi, Hardal, Terrakota }

    public Renk renk = Renk.Krem;

    // Renk sirasiyla malzemeler (parcada olmayan renk bos); kurulum doldurur
    [HideInInspector] public Material[] malzemeler = new Material[7];

    public void Uygula()
    {
        if (malzemeler == null || malzemeler.Length == 0) return;
        var hedef = malzemeler[Mathf.Clamp((int)renk, 0, malzemeler.Length - 1)];
        if (!hedef) { Debug.LogWarning($"{name}: {renk} rengi bu parcada yok", this); return; }

        foreach (var mr in GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = mr.sharedMaterials;
            bool degisti = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == hedef || System.Array.IndexOf(malzemeler, mats[i]) < 0) continue;   // baska malzemeye dokunma
                mats[i] = hedef; degisti = true;
            }
            if (!degisti) continue;
#if UNITY_EDITOR
            if (!Application.isPlaying) Undo.RecordObject(mr, "Oda rengi");
#endif
            mr.sharedMaterials = mats;
#if UNITY_EDITOR
            if (!Application.isPlaying && PrefabUtility.IsPartOfPrefabInstance(mr))
                PrefabUtility.RecordPrefabInstancePropertyModifications(mr);
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
