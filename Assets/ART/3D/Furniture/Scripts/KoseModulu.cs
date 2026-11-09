// L kose tezgah modulu (Ozgur / art). PF_Kose_L prefabinin kokunde durur.
// Kose sag-arkada: arka kol duvar boyunca sola uzanir, yan kol sag duvar boyunca one gelir.
// Baska bir koseye koymak icin prefabi Y ekseninde 90 derece cevir.
//   Arka kol / Yan kol : Kisa 50 (kose + 50 cm dolap = 120 cm) ya da Uzun 100 (kose + 100 cm dolap = 170 cm)
//   Tezgah uclari      : Duz / ArkaKolCikinti / YanKolCikinti / IkiUcCikinti
//                        (cikinti = o kolun bos ucu 5 cm tasar; yanina baska dolap gelecekse Duz kalmali)
// Kollardaki dolaplar normal PF_Dolap_050 / PF_Dolap_100 kopyalaridir: kapak, cekmece, kulp ayari
// icin hiyerarside o dolabi sec (tezgahlari kapali, ustlerini L tezgah kaplar).
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class KoseModulu : MonoBehaviour
{
    public enum KolBoyu { Kisa50, Uzun100 }
    public enum UcTipi { Duz, ArkaKolCikinti, YanKolCikinti, IkiUcCikinti }

    [Header("Secenekler")]
    public KolBoyu arkaKol = KolBoyu.Uzun100;
    public KolBoyu yanKol = KolBoyu.Kisa50;
    [Tooltip("Kollarin bos uclari. Yanina dolap gelecek uc Duz kalmali.")]
    public UcTipi tezgahUclari = UcTipi.Duz;

    // Kurulum doldurur
    [HideInInspector] public GameObject govdeKose;
    [HideInInspector] public GameObject arka050, arka100, yan050, yan100;
    // 16 tezgah: (arka uzun ? 2 : 0) + (yan uzun ? 1 : 0) -> x4 + uc tipi
    [HideInInspector] public GameObject[] tezgahlar = new GameObject[16];

    public void Uygula()
    {
        bool arkaUzun = arkaKol == KolBoyu.Uzun100;
        bool yanUzun = yanKol == KolBoyu.Uzun100;
        float wArka = arkaUzun ? 1f : 0.5f;
        float lx = 0.7f + wArka;   // arka kolun boyu

        Ac(arka050, !arkaUzun);
        Ac(arka100, arkaUzun);
        Ac(yan050, !yanUzun);
        Ac(yan100, yanUzun);

        // Kose govdesi arka dolabin sagina, yan kol dolaplari kose govdesinin onune gelir
        Yer(govdeKose, new Vector3(-wArka, 0f, 0f), Quaternion.identity);
        var yanRot = Quaternion.Euler(0f, 90f, 0f);
        Yer(yan050, new Vector3(-lx, 0f, 0.7f), yanRot);
        Yer(yan100, new Vector3(-lx, 0f, 0.7f), yanRot);

        int secili = ((arkaUzun ? 2 : 0) + (yanUzun ? 1 : 0)) * 4 + (int)tezgahUclari;
        if (tezgahlar != null)
            for (int i = 0; i < tezgahlar.Length; i++) Ac(tezgahlar[i], i == secili);
    }

    static void Ac(GameObject g, bool acik)
    {
        if (!g || g.activeSelf == acik) return;
#if UNITY_EDITOR
        if (!Application.isPlaying) Undo.RecordObject(g, "Kose modulu");
#endif
        g.SetActive(acik);
#if UNITY_EDITOR
        if (!Application.isPlaying && PrefabUtility.IsPartOfPrefabInstance(g))
            PrefabUtility.RecordPrefabInstancePropertyModifications(g);
#endif
    }

    static void Yer(GameObject g, Vector3 pos, Quaternion rot)
    {
        if (!g) return;
        var t = g.transform;
        if ((t.localPosition - pos).sqrMagnitude < 1e-10f && Quaternion.Angle(t.localRotation, rot) < 0.01f) return;
#if UNITY_EDITOR
        if (!Application.isPlaying) Undo.RecordObject(t, "Kose modulu");
#endif
        t.localPosition = pos;
        t.localRotation = rot;
#if UNITY_EDITOR
        if (!Application.isPlaying && PrefabUtility.IsPartOfPrefabInstance(t))
            PrefabUtility.RecordPrefabInstancePropertyModifications(t);
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
