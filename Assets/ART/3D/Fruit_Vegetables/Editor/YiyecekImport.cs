// Yiyecek FBX'leri ice aktarilirken dilimler (SM_<Ad>_SegNN) kapali gelir, butun hal acik kalir.
// Boylece FBX dogrudan sahneye atilsa da yalnizca butun hal gorunur. Prefablar (PF_<Ad>) ayrica kendi Slices dugumuyle calisir.
// Yalnizca Assets/ART/3D/Fruit_Vegetables kokundeki SM_*.fbx dosyalarina dokunur (Test klasoru haric).
using UnityEditor;
using UnityEngine;

public class YiyecekImport : AssetPostprocessor
{
    const string Root = "Assets/ART/3D/Fruit_Vegetables/";

    void OnPostprocessModel(GameObject model)
    {
        string p = assetPath.Replace('\\', '/');
        if (!p.StartsWith(Root) || p.Substring(Root.Length).Contains("/")) return;
        if (!System.IO.Path.GetFileName(p).StartsWith("SM_")) return;
        foreach (Transform t in model.transform)
            t.gameObject.SetActive(!t.name.Contains("_Seg"));
    }
}
