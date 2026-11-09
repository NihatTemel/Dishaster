// MutfakModulu icin sade inspector (Ozgur / art).
// Acilir listelerde yalnizca o dolapta gercekten olan secenekler gorunur
// (ornegin cekmeceli dolapta kapak secenegi yok, lavabo dolabinda cikintili tezgah yok).
// Parca alanlari kapali bir bolumde durur; kurulum doldurur, elle degistirmeye gerek yok.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MutfakModulu))]
[CanEditMultipleObjects]
public class MutfakModuluEditor : Editor
{
    static bool parcalarAcik;

    static readonly Dictionary<MutfakModulu.TezgahTipi, string> TezgahAd = new()
    {
        { MutfakModulu.TezgahTipi.Yok, "Yok" },
        { MutfakModulu.TezgahTipi.Duz, "Duz" },
        { MutfakModulu.TezgahTipi.SolCikinti, "Sol uc cikintili" },
        { MutfakModulu.TezgahTipi.SagCikinti, "Sag uc cikintili" },
        { MutfakModulu.TezgahTipi.IkiCikinti, "Iki uc cikintili" },
    };

    static readonly Dictionary<MutfakModulu.OnYuzTipi, string> OnAd = new()
    {
        { MutfakModulu.OnYuzTipi.Bos, "Bos (acik raf)" },
        { MutfakModulu.OnYuzTipi.IkiKapak, "Iki kapak" },
        { MutfakModulu.OnYuzTipi.SolMenteseKapak, "Tek kapak, mentese solda" },
        { MutfakModulu.OnYuzTipi.SagMenteseKapak, "Tek kapak, mentese sagda" },
        { MutfakModulu.OnYuzTipi.Cekmece, "Tek cekmece, alti acik raf" },
        { MutfakModulu.OnYuzTipi.IkiCekmece, "Iki cekmece" },
    };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var m = (MutfakModulu)target;

        // Tezgah: yalnizca bu dolapta olan cesitler
        var tezgahlar = new List<MutfakModulu.TezgahTipi> { MutfakModulu.TezgahTipi.Yok };
        if (m.tezgahDuz) tezgahlar.Add(MutfakModulu.TezgahTipi.Duz);
        if (m.tezgahSol) tezgahlar.Add(MutfakModulu.TezgahTipi.SolCikinti);
        if (m.tezgahSag) tezgahlar.Add(MutfakModulu.TezgahTipi.SagCikinti);
        if (m.tezgahIki) tezgahlar.Add(MutfakModulu.TezgahTipi.IkiCikinti);
        if (tezgahlar.Count > 1)
            Liste(serializedObject.FindProperty("tezgah"), "Tezgah", tezgahlar, TezgahAd,
                  "Cikinti: tezgahin o ucu 5 cm tasar. Yanina baska dolap gelecek uc duz kalmali.");

        // On yuz: yalnizca bu dolapta olan parcalar
        var onlar = new List<MutfakModulu.OnYuzTipi> { MutfakModulu.OnYuzTipi.Bos };
        if (m.kapakSol && m.kapakSag && m.ikiKapakSigar) onlar.Add(MutfakModulu.OnYuzTipi.IkiKapak);
        if (m.kapakSol) onlar.Add(MutfakModulu.OnYuzTipi.SolMenteseKapak);
        if (m.kapakSag) onlar.Add(MutfakModulu.OnYuzTipi.SagMenteseKapak);
        if (m.cekmece) onlar.Add(MutfakModulu.OnYuzTipi.Cekmece);
        if (m.cekmece && m.cekmece2) onlar.Add(MutfakModulu.OnYuzTipi.IkiCekmece);
        if (onlar.Count > 1)
            Liste(serializedObject.FindProperty("onYuz"), "On yuz", onlar, OnAd, null);

        if (m.kulplar != null && m.kulplar.Any(k => k))
            EditorGUILayout.PropertyField(serializedObject.FindProperty("kulp"), new GUIContent("Kulp"));
        // Ayrac cekmeceyle olmaz; raf tek cekmecede olur, iki cekmecede olmaz
        bool cekmeceli = (m.onYuz == MutfakModulu.OnYuzTipi.Cekmece || m.onYuz == MutfakModulu.OnYuzTipi.IkiCekmece) && m.cekmece;
        bool ikiCekmece = m.onYuz == MutfakModulu.OnYuzTipi.IkiCekmece && m.cekmece2;
        if (m.ayracParca && !cekmeceli)
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ayrac"), new GUIContent("Ayrac", "Iki kapagin arkasindaki dik bolme"));
        if (m.rafParca && !ikiCekmece)
            EditorGUILayout.PropertyField(serializedObject.FindProperty("raf"), new GUIContent("Raf", "Dolabin icindeki yatay raf"));

        EditorGUILayout.Space();
        parcalarAcik = EditorGUILayout.Foldout(parcalarAcik, "Parcalar (kurulum doldurur, elle degistirme)", true);
        if (parcalarAcik)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                var it = serializedObject.GetIterator();
                bool ilk = true;
                bool basladi = false;
                while (it.NextVisible(ilk))
                {
                    ilk = false;
                    if (it.name == "govde") basladi = true;
                    if (basladi) EditorGUILayout.PropertyField(it, true);
                }
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    static void Liste<T>(SerializedProperty prop, string etiket, List<T> secenekler,
                         Dictionary<T, string> adlar, string ipucu) where T : System.Enum
    {
        var su = (T)System.Enum.ToObject(typeof(T), prop.enumValueIndex);
        int i = secenekler.IndexOf(su);
        var gosterim = secenekler.Select(s => adlar[s]).ToList();
        if (i < 0)   // eski ayar bu dolapta yoksa listede gorunsun ki fark edilsin
        {
            gosterim.Add(adlar[su] + " (bu dolapta yok)");
            i = gosterim.Count - 1;
        }
        EditorGUI.showMixedValue = prop.hasMultipleDifferentValues;
        int yeni = EditorGUILayout.Popup(new GUIContent(etiket, ipucu), i, gosterim.Select(g => new GUIContent(g)).ToArray());
        EditorGUI.showMixedValue = false;
        if (yeni != i && yeni < secenekler.Count)
            prop.enumValueIndex = System.Convert.ToInt32(secenekler[yeni]);
    }
}
