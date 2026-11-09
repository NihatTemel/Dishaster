// Mutfak mobilyasi kurulumu (Ozgur / art). Ust menude: Dishaster > Art > Mobilyalari kur.
// Bir kez calistirilir; sonuc (.meta, Materials ve Prefabs klasorleri) depoya girer, Nihat'in tekrar calistirmasi gerekmez.
// FBX ya da doku yenilenince tekrar calistirilir. Var olan prefab silinmez: mesh, malzeme ve parca yerleri guncellenir,
// prefaba sonradan eklenen bilesenler (script, collider...) ve dolaplardaki secenek ayarlari korunur.
//
// Assets/ART/3D/Furniture/
//   SM_<Ad>.fbx            tek parca, tek nesne. Blender'dan Game\Scripts\25_export_furniture_unity.py ile.
//   Textures\              trim sheet (2048): T_Furniture_Trim_BaseColor, _MetallicSmoothness, _Normal
//   Materials\             bu betik olusturur: M_Furniture_Trim.mat (Standard, Metallic). Butun mobilya tek malzeme.
//   Prefabs\               hazir dolaplar (PF_Dolap_100 ...), kokte MutfakModulu bileseni
//   Prefabs\Parcalar\      her parca tek basina (PF_Top_200_EndR ...), duz yerlestirmek icin
//   Scripts\MutfakModulu.cs   dolap secenekleri (tezgah, on yuz, kulp, ayrac, raf)
//
// Olcu ve pivot: metre. Govde ve tezgah pivotu sol-arka-alt kose, yerde (onden bakinca sol arka).
// 50 cm izgaraya oturur. Kapak pivotu mentese cizgisinde (Y ekseninde doner), cekmece Z'de one kayar.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class MobilyaKurulum
{
    const string Root = "Assets/ART/3D/Furniture";
    const string MatName = "M_Furniture_Trim";

    // Blender'daki konum (x sag, y arka, z yukari) -> Unity (FBX aktarim ayarlarimizla)
    static Vector3 B(float x, float y, float z) => new Vector3(-x, z, -y);

    // MobilyaRengi.Renk ile ayni sira. Doku: Textures/T_Furniture_Trim_BaseColor_<Ad>.png (Krem: eksiz)
    static readonly string[] TemaAdlari = { "Krem", "Ahsap", "Adacayi", "TozluMavi", "Hardal", "Terrakota" };
    static Material[] Temalar;

    // Modulun kokune renk secici ekler (ilk kurulumda Krem), secili rengi yeniden uygular
    static void RenkEkle(GameObject root)
    {
        var r = root.GetComponent<MobilyaRengi>();
        if (!r) r = root.AddComponent<MobilyaRengi>();
        r.malzemeler = Temalar;
        r.Uygula();
    }

    [MenuItem("Dishaster/Art/Mobilyalari kur")]
    public static void Run()
    {
        var mat = SetupMaterial("");
        // Renk temalari: MobilyaRengi.Renk sirasiyla (Krem = ana malzeme)
        Temalar = new[] { mat }.Concat(TemaAdlari.Skip(1).Select(SetupMaterial)).ToArray();
        Directory.CreateDirectory($"{Root}/Prefabs/Parcalar");

        var fbxs = Directory.GetFiles(Root, "SM_*.fbx", SearchOption.TopDirectoryOnly)
                            .Select(p => p.Replace('\\', '/')).OrderBy(p => p).ToArray();
        foreach (var fbx in fbxs)
        {
            SetupModel(fbx, mat);
            // Tek parca prefabi yalnizca hicbir modulde olmayan parcalar icin (ada parcalari)
            if (TekParcalar.Contains(Path.GetFileNameWithoutExtension(fbx)))
                BuildPartPrefab(fbx, mat);
        }
        EskileriSil();

        // Hazir dolaplar
        // 100'luk dolap: kapakli, tek kapakli, acik ya da cekmeceli (cekmece secilince govde de degisir)
        Dolap("PF_Dolap_100", "SM_Base_Carcass_100", "SM_Top_100", mat, d =>
        {
            // Mentese yerleri Ozgur'un prefabda ayarladigi gibi (9 Kas 2026): kapaklar ortada 6 mm aralikla bulusur
            Kapaklar(d, mat, 0.047f, 0.953f);
            d.m.govdeCekmeceli = Parca(d.root, "Govde_Cekmeceli", "SM_Base_Carcass_100_Drawer", Vector3.zero, mat);
            Cekmece(d, mat);
            d.m.ayracParca = Parca(d.root, "Ayrac", "SM_Divider_100", Vector3.zero, mat);
            d.m.rafParca = Parca(d.root, "Raf", "SM_Shelf_080", Vector3.zero, mat);
        }, MutfakModulu.OnYuzTipi.IkiKapak);

        Dolap("PF_Dolap_050", "SM_Base_Carcass_050", "SM_Top_050", mat, d =>
        {
            Kapaklar(d, mat, 0.025f, 0.475f);
            d.m.ikiKapakSigar = false;
        }, MutfakModulu.OnYuzTipi.SolMenteseKapak);

        Dolap("PF_Evye_100", "SM_Base_Carcass_Sink_100", null, mat, d =>
        {
            d.m.tezgahDuz = Parca(d.root, "Tezgah_Duz", "SM_Top_Sink_100", Vector3.zero, mat);
            Parca(d.root, "Lavabo", "SM_Sink_Basin_01", B(0.5f, -0.4235f, 0.9f), mat);
            Parca(d.root, "Musluk", "SM_Faucet_01", B(0.5f, -0.094f, 0.8943f), mat);
            Parca(d.root, "Vana_Sol", "SM_Faucet_01_Knob_L", B(0.4068f, -0.0937f, 0.9461f), mat);
            Parca(d.root, "Vana_Sag", "SM_Faucet_01_Knob_R", B(0.5932f, -0.0937f, 0.9461f), mat);
            Kapaklar(d, mat, 0.047f, 0.953f);   // PF_Dolap_100 ile ayni
        }, MutfakModulu.OnYuzTipi.IkiKapak);

        Dolap("PF_Dolgu_025", "SM_Filler_025", "SM_Top_025", mat, null, MutfakModulu.OnYuzTipi.Bos);

        // Ust (duvar) dolabi: kapakli / tek kapakli / acik, raf ac-kapa. Govde yerden 160-240 cm.
        // Kapaklar govdenin ortasinda (170-230 cm), kulp kapagin altina yakin (ele yakin olsun).
        Dolap("PF_Duvar_Dolabi_100", "SM_Wall_Carcass_100", null, mat, d =>
        {
            Kapaklar(d, mat, 0.047f, 0.953f, 2.0f, -0.22f);
            d.m.rafParca = Parca(d.root, "Raf", "SM_Shelf_080", B(0f, 0f, 1.55f), mat);   // 197-203 cm, ortada
        }, MutfakModulu.OnYuzTipi.IkiKapak, MutfakModulu.TezgahTipi.Yok);

        KoseL(mat);
        DuvarRafi(mat);
        Ada(mat);

        AssetDatabase.SaveAssets();
        Debug.Log($"Mobilya kurulum: bitti ({fbxs.Length} parca; 5 dolap, L kose, duvar rafi, ada).");
    }

    // Tek basina prefabi yapilan parcalar (bir modulun icinde olmayanlar). Ada tezgahi ve 100'luk ada govdesi
    // artik PF_Ada_200'un icinde; kalan kucuk ada parcalari burada.
    static readonly string[] TekParcalar =
    {
        "SM_Island_Top_050", "SM_Island_Top_100", "SM_Island_Carcass_050",
    };

    // ---------- ada (200 x 150) ----------
    // Blender'da: tezgah x 0..2, y -1,5..0. On sira govdeleri y -1,45..-0,75 (one bakar), arka sira 180 derece
    // donuk y -0,75..-0,05 (arkaya bakar). Her sira iki 100'luk yuva.
    static void Ada(Material mat)
    {
        string pfPath = $"{Root}/Prefabs/PF_Ada_200.prefab";
        bool exists = File.Exists(pfPath);
        var root = exists ? PrefabUtility.LoadPrefabContents(pfPath) : new GameObject("PF_Ada_200");
        try
        {
            var m = root.GetComponent<AdaModulu>();
            if (!m) m = root.AddComponent<AdaModulu>();
            Parca(root, "Tezgah", "SM_Top_Island_200x150", Vector3.zero, mat);

            GameObject Yuva(string ad, float x, bool arka, out GameObject dolap)
            {
                var pos = B(x, -0.75f, 0f);
                float rot = arka ? 180f : 0f;
                var kapali = Parca(root, ad + "_Kapali", "SM_Island_Carcass_100", pos, mat, rot);
                dolap = IcDolap(root, ad + "_Dolap", "PF_Dolap_100");
                if (dolap)
                {
                    dolap.transform.localPosition = pos;
                    dolap.transform.localRotation = Quaternion.Euler(0f, rot, 0f);
                }
                return kapali;
            }
            // On sira pivotu yuvanin sol-arka kosesi; arka sira donuk oldugu icin pivotu yuvanin sag ucunda
            m.onSolKapali = Yuva("On_Sol", 0f, false, out m.onSolDolap);
            m.onSagKapali = Yuva("On_Sag", 1f, false, out m.onSagDolap);
            m.arkaSolKapali = Yuva("Arka_Sol", 1f, true, out m.arkaSolDolap);
            m.arkaSagKapali = Yuva("Arka_Sag", 2f, true, out m.arkaSagDolap);

            m.Uygula();
            RenkEkle(root);
            PrefabUtility.SaveAsPrefabAsset(root, pfPath);
        }
        finally
        {
            if (exists) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }
        Debug.Log("Mobilya kurulum: PF_Ada_200");
    }

    // Artik kullanilmayan prefablar (yerlerine moduller geldi). Varsa silinir.
    static void EskileriSil()
    {
        var eski = new List<string> { "PF_Dolap_Cekmeceli_100", "PF_Kose_070" }
            .Select(n => $"{Root}/Prefabs/{n}.prefab").ToList();
        if (Directory.Exists($"{Root}/Prefabs/Parcalar"))
            eski.AddRange(Directory.GetFiles($"{Root}/Prefabs/Parcalar", "PF_*.prefab")
                .Select(p => p.Replace('\\', '/'))
                .Where(p => !TekParcalar.Contains("SM_" + Path.GetFileNameWithoutExtension(p).Substring(3))));
        foreach (var p in eski)
            if (File.Exists(p) && AssetDatabase.DeleteAsset(p))
                Debug.Log($"Mobilya kurulum: eski prefab silindi {p}");
    }

    // ---------- L kose ----------

    static readonly string[] KoseBoylari = { "120x120", "120x170", "170x120", "170x170" };   // arka x yan (cm)
    static readonly string[] KoseUclari = { "", "_EndL", "_EndR", "_EndLR" };   // EndL = arka kol ucu, EndR = yan kol ucu

    static void KoseL(Material mat)
    {
        string pfPath = $"{Root}/Prefabs/PF_Kose_L.prefab";
        bool exists = File.Exists(pfPath);
        var root = exists ? PrefabUtility.LoadPrefabContents(pfPath) : new GameObject("PF_Kose_L");
        try
        {
            var m = root.GetComponent<KoseModulu>();
            if (!m) m = root.AddComponent<KoseModulu>();

            m.govdeKose = Parca(root, "Govde_Kose", "SM_Base_Carcass_Corner_070", Vector3.zero, mat);
            m.arka050 = IcDolap(root, "Arka_Dolap_050", "PF_Dolap_050");
            m.arka100 = IcDolap(root, "Arka_Dolap_100", "PF_Dolap_100");
            m.yan050 = IcDolap(root, "Yan_Dolap_050", "PF_Dolap_050");
            m.yan100 = IcDolap(root, "Yan_Dolap_100", "PF_Dolap_100");

            m.tezgahlar = new GameObject[16];
            for (int b = 0; b < 4; b++)
                for (int u = 0; u < 4; u++)
                    m.tezgahlar[b * 4 + u] = Parca(root, $"Tezgah_{KoseBoylari[b]}{KoseUclari[u]}",
                                                   $"SM_Top_Corner_{KoseBoylari[b]}{KoseUclari[u]}", Vector3.zero, mat);
            m.Uygula();
            RenkEkle(root);
            PrefabUtility.SaveAsPrefabAsset(root, pfPath);
        }
        finally
        {
            if (exists) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }
        Debug.Log("Mobilya kurulum: PF_Kose_L");
    }

    // Kose modulundeki dolap: normal dolap prefabinin kopyasi (ic ice prefab), tezgahi kapali.
    static GameObject IcDolap(GameObject root, string ad, string prefab)
    {
        var t = root.transform.Find(ad);
        GameObject go;
        if (t) go = t.gameObject;
        else
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Prefabs/{prefab}.prefab");
            if (!asset) { Debug.LogWarning($"Mobilya kurulum: {prefab} yok"); return null; }
            go = (GameObject)PrefabUtility.InstantiatePrefab(asset, root.transform);
            go.name = ad;
        }
        var mm = go.GetComponent<MutfakModulu>();
        if (mm)
        {
            mm.tezgah = MutfakModulu.TezgahTipi.Yok;   // ustunu L tezgah kaplar
            // 50'lik dolapta sol menteseli kapagin kulbu kose tarafina duser ve iki kolun kulplari ic kosede
            // carpisir (Ozgur, 9 Kas 2026). Kosede mentese sagda olsun, kulp kosenin uzak tarafinda kalsin.
            // Yalnizca varsayilan (sol mentese) degistirilir; elle secilmis baska ayar korunur.
            if (prefab == "PF_Dolap_050" && mm.onYuz == MutfakModulu.OnYuzTipi.SolMenteseKapak)
                mm.onYuz = MutfakModulu.OnYuzTipi.SagMenteseKapak;
            mm.Uygula();
        }
        return go;
    }

    // ---------- katli duvar rafi ----------

    static void DuvarRafi(Material mat)
    {
        string pfPath = $"{Root}/Prefabs/PF_Duvar_Rafi_150.prefab";
        bool exists = File.Exists(pfPath);
        var root = exists ? PrefabUtility.LoadPrefabContents(pfPath) : new GameObject("PF_Duvar_Rafi_150");
        try
        {
            var m = root.GetComponent<RafModulu>();
            if (!m) m = root.AddComponent<RafModulu>();
            m.katlar = new GameObject[4];
            for (int i = 0; i < 4; i++)
            {
                string ad = $"Kat_{i + 1}";
                var k = root.transform.Find(ad);
                if (!k)
                {
                    k = new GameObject(ad).transform;
                    k.SetParent(root.transform, false);
                }
                // Kat icindeki yerler rafin alt yuzune gore (Blender'da birlikte dizilip bakildi).
                // Raf duvardan 10 cm onde; direk duvarda, rafin 10 cm altindan 30 cm ustune;
                // kol rafin altinda, ucundaki cikinti rafin onunu tutar. Tutacaklar raf ucundan 20 cm iceride.
                Parca(k, "Raf", "SM_Wall_Shelf_150", B(0f, -0.10f, -0.8f), mat);
                foreach (var (taraf, xc) in new[] { ("Sol", 0.2f), ("Sag", 1.3f) })
                {
                    Parca(k, "Direk_" + taraf, "SM_Rod_040", B(xc, -0.05f, -0.06f), mat, 180f);
                    Parca(k, "Kol_" + taraf, "SM_Arm_044", B(xc - 0.5f, -0.285f, 0.04985f), mat, 180f);
                }
                m.katlar[i] = k.gameObject;
            }
            m.Uygula();
            PrefabUtility.SaveAsPrefabAsset(root, pfPath);
        }
        finally
        {
            if (exists) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }
        Debug.Log("Mobilya kurulum: PF_Duvar_Rafi_150");
    }

    // ---------- dolap (modul) prefabi ----------

    class D
    {
        public GameObject root;
        public MutfakModulu m;
        public List<GameObject> kulplar = new List<GameObject>();
    }

    static void Dolap(string pfName, string govde, string tezgah, Material mat, System.Action<D> ekle,
                      MutfakModulu.OnYuzTipi onYuz, MutfakModulu.TezgahTipi tez = MutfakModulu.TezgahTipi.Duz)
    {
        string pfPath = $"{Root}/Prefabs/{pfName}.prefab";
        bool exists = File.Exists(pfPath);
        var root = exists ? PrefabUtility.LoadPrefabContents(pfPath) : new GameObject(pfName);
        try
        {
            var d = new D { root = root, m = root.GetComponent<MutfakModulu>() };
            bool yeni = !d.m;
            if (yeni) d.m = root.AddComponent<MutfakModulu>();

            d.m.govde = Parca(root, "Govde", govde, Vector3.zero, mat);
            if (tezgah != null)
            {
                d.m.tezgahDuz = Parca(root, "Tezgah_Duz", tezgah, Vector3.zero, mat);
                d.m.tezgahSol = Parca(root, "Tezgah_SolCikinti", tezgah + "_EndL", Vector3.zero, mat);
                d.m.tezgahSag = Parca(root, "Tezgah_SagCikinti", tezgah + "_EndR", Vector3.zero, mat);
                d.m.tezgahIki = Parca(root, "Tezgah_IkiCikinti", tezgah + "_EndLR", Vector3.zero, mat);
            }
            ekle?.Invoke(d);
            d.m.kulplar = d.kulplar.Where(k => k).ToArray();

            if (yeni)   // ilk kurulumda varsayilan secenekler; sonra kullanicinin ayari korunur
            {
                d.m.tezgah = tez;
                d.m.onYuz = onYuz;
                d.m.kulp = true;
                d.m.ayrac = false;
                d.m.raf = false;
            }
            d.m.Uygula();
            RenkEkle(root);
            PrefabUtility.SaveAsPrefabAsset(root, pfPath);
        }
        finally
        {
            if (exists) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }
        Debug.Log($"Mobilya kurulum: {pfName}");
    }

    // Kapaklar: sol kapak menteşesi xSol'da, sag kapak menteşesi xSag'da (Blender x, metre). Kapak 45 x 60 cm.
    // zMerkez: kapagin orta yuksekligi (alt dolapta 0,45). kulpZ: kulbun kapak ortasina gore yuksekligi
    // (alt dolapta +0,22 = ustten 8 cm asagi; ust dolapta -0,22 = alttan 8 cm yukari).
    static void Kapaklar(D d, Material mat, float xSol, float xSag, float zMerkez = 0.45f, float kulpZ = 0.22f)
    {
        var ks = Parca(d.root, "Kapak_Sol", "SM_Door_L_01", B(xSol, -0.70f, zMerkez), mat);
        var kr = Parca(d.root, "Kapak_Sag", "SM_Door_R_01", B(xSag, -0.70f, zMerkez), mat);
        d.m.kapakSol = ks;
        d.m.kapakSag = kr;
        // Kulp kapagin cocugu: kapakla birlikte doner. Mentesenin karsi tarafinda.
        d.kulplar.Add(Parca(ks.transform, "Kulp", "SM_Knob_01", B(0.40f, -0.05f, kulpZ), mat));
        d.kulplar.Add(Parca(kr.transform, "Kulp", "SM_Knob_01", B(-0.40f, -0.05f, kulpZ), mat));
    }

    // Cekmece govdenin pivotunda durur, one (+Z) kayar. Kulp cekmecenin cocugu, on panelin ortasinda.
    static void Cekmece(D d, Material mat)
    {
        var c = Parca(d.root, "Cekmece", "SM_Drawer_086", Vector3.zero, mat);
        d.m.cekmece = c;
        d.kulplar.Add(Parca(c.transform, "Kulp", "SM_Knob_01", B(0.5f, -0.75f, 0.60f), mat));
        // Ikinci cekmece: ustekinin 27 cm alti (on yuzler 47-73 ve 20-46 cm; kapaklarin kapladigi alanla ayni)
        var c2 = Parca(d.root, "Cekmece_Alt", "SM_Drawer_086", B(0f, 0f, -0.27f), mat);
        d.m.cekmece2 = c2;
        d.kulplar.Add(Parca(c2.transform, "Kulp", "SM_Knob_01", B(0.5f, -0.75f, 0.60f), mat));
    }

    static GameObject Parca(GameObject parent, string ad, string mesh, Vector3 pos, Material mat, float rotY = 0f)
        => Parca(parent.transform, ad, mesh, pos, mat, rotY);

    // Ada gore cocuk bulunur (yoksa olusturulur), yeri ve mesh'i guncellenir. rotY: Unity Y ekseninde ek donus.
    static GameObject Parca(Transform parent, string ad, string mesh, Vector3 pos, Material mat, float rotY = 0f)
    {
        var src = LoadMesh($"{Root}/{mesh}.fbx", out var rot);
        if (!src) { Debug.LogWarning($"Mobilya kurulum: {mesh}.fbx yok, '{ad}' atlandi"); return null; }
        var t = parent.Find(ad);
        if (!t)
        {
            t = new GameObject(ad).transform;
            t.SetParent(parent, false);
        }
        t.localPosition = pos;
        t.localRotation = Quaternion.Euler(0f, rotY, 0f) * rot;
        t.localScale = Vector3.one;
        // Unity'de eksik bilesen "sahte null" doner; ?? ile degil, acik kontrolle eklenir
        var mf = t.GetComponent<MeshFilter>();
        if (!mf) mf = t.gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = src;
        var mr = t.GetComponent<MeshRenderer>();
        if (!mr) mr = t.gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        return t.gameObject;
    }

    static Mesh LoadMesh(string fbxPath, out Quaternion rot)
    {
        rot = Quaternion.identity;
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (!model) return null;
        var mf = model.GetComponentInChildren<MeshFilter>(true);
        if (!mf) return null;
        if (mf.transform != model.transform) rot = mf.transform.localRotation;
        return mf.sharedMesh;
    }

    // ---------- tek parca prefabi ----------

    static void BuildPartPrefab(string fbxPath, Material mat)
    {
        string part = Path.GetFileNameWithoutExtension(fbxPath);    // SM_Top_200_EndR
        string pfName = "PF_" + part.Substring(3);                   // PF_Top_200_EndR
        string pfPath = $"{Root}/Prefabs/Parcalar/{pfName}.prefab";
        bool exists = File.Exists(pfPath);
        var root = exists ? PrefabUtility.LoadPrefabContents(pfPath) : new GameObject(pfName);
        try
        {
            Parca(root, "Model", part, Vector3.zero, mat);
            PrefabUtility.SaveAsPrefabAsset(root, pfPath);
        }
        finally
        {
            if (exists) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }
    }

    // ---------- malzeme ve aktarim ayarlari ----------

    // tema "" = krem (ana malzeme M_Furniture_Trim). Temanin kendi Normal / MetallicSmoothness dokusu yoksa
    // kreminkiler kullanilir (renkli temalarda yalnizca renk farkli).
    static Material SetupMaterial(string tema)
    {
        string s = string.IsNullOrEmpty(tema) ? "" : "_" + tema;
        string Tex(string ch)
        {
            string p = $"{Root}/Textures/T_Furniture_Trim_{ch}{s}.png";
            return File.Exists(p) ? p : $"{Root}/Textures/T_Furniture_Trim_{ch}.png";
        }
        var baseColor = SetupTexture(Tex("BaseColor"), true, false);
        var metSmooth = SetupTexture(Tex("MetallicSmoothness"), false, false);
        var normal = SetupTexture(Tex("Normal"), false, true);

        string path = $"{Root}/Materials/{MatName}{s}.mat";
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var std = Shader.Find("Standard");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat)
        {
            mat = new Material(std);
            AssetDatabase.CreateAsset(mat, path);
        }
        if (std && mat.shader != std) mat.shader = std;

        mat.SetColor("_Color", Color.white);
        mat.SetTexture("_MainTex", baseColor);
        mat.SetTexture("_MetallicGlossMap", metSmooth);      // RGB = metallic, alfa = smoothness
        mat.EnableKeyword("_METALLICGLOSSMAP");
        mat.SetFloat("_GlossMapScale", 1f);
        mat.SetFloat("_SmoothnessTextureChannel", 0f);
        mat.SetTexture("_BumpMap", normal);
        mat.SetFloat("_BumpScale", 1f);
        if (normal) mat.EnableKeyword("_NORMALMAP"); else mat.DisableKeyword("_NORMALMAP");
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Texture2D SetupTexture(string path, bool srgb, bool isNormal)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { Debug.LogWarning($"Mobilya kurulum: {path} yok"); return null; }
        ti.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        ti.sRGBTexture = srgb;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.alphaIsTransparency = false;
        ti.maxTextureSize = 2048;
        ti.mipmapEnabled = true;
        // Trim sheet: ahsap seritleri yatayda tekrarlar, UV'ler 0-1 disina tasar. Clamp olursa doku cizgi gibi uzar.
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void SetupModel(string path, Material mat)
    {
        var mi = (ModelImporter)AssetImporter.GetAtPath(path);
        mi.globalScale = 1f;
        mi.useFileScale = true;
        mi.importCameras = false;
        mi.importLights = false;
        mi.importAnimation = false;
        mi.animationType = ModelImporterAnimationType.None;
        mi.importBlendShapes = false;
        mi.addCollider = false;
        mi.isReadable = false;
        mi.importNormals = ModelImporterNormals.Import;            // bevel'larin duzgun golgesi Blender'dan gelir
        mi.importTangents = ModelImporterTangents.CalculateMikk;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), MatName), mat);
        mi.SaveAndReimport();
    }
}
