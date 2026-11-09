// Mutfak modulu (Ozgur / art). Dolap prefablarinin kokunde durur.
// Inspector'da secenekleri degistirince ilgili parcalar acilip kapanir; alt nesnelerle tek tek ugrasmaya gerek yok.
//   Tezgah : Yok / Duz / SolCikinti / SagCikinti / IkiCikinti
//            (cikinti = tezgahin o ucu 5 cm tasar; sira sonunda ya da tek basina duran dolapta kullan.
//             Yanina baska dolap gelecek uc Duz kalmali.)
//   On yuz : Bos / IkiKapak / SolMenteseKapak / SagMenteseKapak / Cekmece
//   Kulp, Ayrac, Raf: ac / kapat
// Prefabda olmayan bir secenek secilirse (ornegin lavabo dolabinda cikintili tezgah yok) en yakin olan gosterilir ve uyari yazilir.
// Parca alanlari MobilyaKurulum (Dishaster > Art > Mobilyalari kur) tarafindan doldurulur.
// Oyun icinde de calisir: secenegi degistirip Uygula() cagirmak yeter.
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class MutfakModulu : MonoBehaviour
{
    public enum TezgahTipi { Yok, Duz, SolCikinti, SagCikinti, IkiCikinti }
    // Yeni secenek hep sona eklenir (kayitli prefablarda sira numarasi saklanir)
    public enum OnYuzTipi { Bos, IkiKapak, SolMenteseKapak, SagMenteseKapak, Cekmece, IkiCekmece }

    [Header("Secenekler")]
    public TezgahTipi tezgah = TezgahTipi.Duz;
    public OnYuzTipi onYuz = OnYuzTipi.IkiKapak;
    [Tooltip("Kapak ve cekmece kulplari")]
    public bool kulp = true;
    [Tooltip("Iki kapagin arkasindaki dik ara bolme")]
    public bool ayrac = false;
    [Tooltip("Dolabin icindeki yatay raf")]
    public bool raf = false;

    [Header("Parcalar (kurulum doldurur, elle degistirme)")]
    [Tooltip("Kapakli ya da acik dolap govdesi")]
    public GameObject govde;
    [Tooltip("Cekmece secilince bu govde gorunur (ust kisimda cekmece boslugu var). Bos ise tek govde kullanilir.")]
    public GameObject govdeCekmeceli;
    public GameObject tezgahDuz;
    public GameObject tezgahSol;
    public GameObject tezgahSag;
    public GameObject tezgahIki;
    public GameObject kapakSol;
    public GameObject kapakSag;
    public GameObject cekmece;
    [Tooltip("Iki cekmece secilince alttaki ikinci cekmece")]
    public GameObject cekmece2;
    public GameObject[] kulplar;
    public GameObject ayracParca;
    public GameObject rafParca;
    [Tooltip("Dolap iki kapaga yetecek kadar genis mi (50 cm dolapta tek kapak sigar)")]
    public bool ikiKapakSigar = true;

    public void Uygula()
    {
        // Tezgah
        var t = Sec(tezgah);
        Ac(tezgahDuz, t == tezgahDuz);
        Ac(tezgahSol, t == tezgahSol);
        Ac(tezgahSag, t == tezgahSag);
        Ac(tezgahIki, t == tezgahIki);

        // On yuz
        var on = onYuz;
        if (on == OnYuzTipi.IkiKapak && !ikiKapakSigar)
        {
            Debug.LogWarning($"{name}: bu dolaba iki kapak sigmaz, sol menteseli tek kapak gosteriliyor.", this);
            on = OnYuzTipi.SolMenteseKapak;
        }
        if (on == OnYuzTipi.IkiCekmece && !cekmece2)
        {
            Debug.LogWarning($"{name}: bu dolaba iki cekmece yok, tek cekmece gosteriliyor.", this);
            on = OnYuzTipi.Cekmece;
        }
        if (on == OnYuzTipi.Cekmece && !cekmece)
        {
            Debug.LogWarning($"{name}: bu dolapta cekmece yok.", this);
            on = OnYuzTipi.Bos;
        }
        if (on != OnYuzTipi.Bos && on != OnYuzTipi.Cekmece && on != OnYuzTipi.IkiCekmece && !kapakSol && !kapakSag)
        {
            Debug.LogWarning($"{name}: bu dolapta kapak yok.", this);
            on = OnYuzTipi.Bos;
        }
        bool tekCek = on == OnYuzTipi.Cekmece;
        bool ikiCek = on == OnYuzTipi.IkiCekmece;
        // Tek cekmece: ustte cekmece, altta acik raf (cekmece boslugu olan govde).
        // Iki cekmece: duz govde, iki cekmece on yuzu kapaklar gibi govdenin onunu kaplar.
        if (govdeCekmeceli)
        {
            Ac(govde, !tekCek);
            Ac(govdeCekmeceli, tekCek);
        }
        Ac(kapakSol, on == OnYuzTipi.IkiKapak || on == OnYuzTipi.SolMenteseKapak);
        Ac(kapakSag, on == OnYuzTipi.IkiKapak || on == OnYuzTipi.SagMenteseKapak);
        Ac(cekmece, tekCek || ikiCek);
        Ac(cekmece2, ikiCek);

        if (kulplar != null)
            foreach (var k in kulplar) Ac(k, kulp);
        // Ayrac cekmecelerle carpisir. Raf tek cekmecede cekmecenin altina oturur (Ozgur oyle olcturdu),
        // iki cekmecede alttaki cekmeceyle carpisir.
        bool cekmeceVar = (tekCek || ikiCek) && cekmece;
        Ac(ayracParca, ayrac && !cekmeceVar);
        Ac(rafParca, raf && !(ikiCek && cekmece2));
    }

    GameObject Sec(TezgahTipi tip)
    {
        if (tip == TezgahTipi.Yok) return null;
        var g = tip switch
        {
            TezgahTipi.SolCikinti => tezgahSol,
            TezgahTipi.SagCikinti => tezgahSag,
            TezgahTipi.IkiCikinti => tezgahIki,
            _ => tezgahDuz,
        };
        if (!g && tip != TezgahTipi.Duz)
        {
            Debug.LogWarning($"{name}: '{tip}' tezgahi bu dolapta yok, duz tezgah gosteriliyor.", this);
            g = tezgahDuz;
        }
        return g;
    }

    static void Ac(GameObject g, bool acik)
    {
        if (!g || g.activeSelf == acik) return;
#if UNITY_EDITOR
        if (!Application.isPlaying) Undo.RecordObject(g, "Mutfak modulu");
#endif
        g.SetActive(acik);
#if UNITY_EDITOR
        if (!Application.isPlaying && PrefabUtility.IsPartOfPrefabInstance(g))
            PrefabUtility.RecordPrefabInstancePropertyModifications(g);
#endif
    }

#if UNITY_EDITOR
    // Inspector'da bir sey degisince (OnValidate icinde SetActive yapilamadigi icin bir kare sonra) uygula
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
