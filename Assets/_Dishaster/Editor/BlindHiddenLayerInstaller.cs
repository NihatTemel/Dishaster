using UnityEditor;
using UnityEngine;

namespace Dishaster.EditorTools
{
    /// <summary>
    /// "BlindHidden" katmanını (kaplardaki dolum, dökülen akıntı) projede açık tutuyor. Görmeyenin kamerası bu katmanı
    /// çizmiyor; diğer oyuncular ve editörün Scene görünümü çiziyor.
    /// </summary>
    [InitializeOnLoad]
    public static class BlindHiddenLayerInstaller
    {
        const int PreferredLayer = 8;

        static BlindHiddenLayerInstaller()
        {
            EditorApplication.delayCall += EnsureLayer;
        }

        static void EnsureLayer()
        {
            if (LayerMask.NameToLayer(BlindHiddenLayer.Name) >= 0) return;

            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;

            var tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");

            for (int i = PreferredLayer; i < layers.arraySize; i++)
            {
                SerializedProperty layer = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(layer.stringValue)) continue;

                layer.stringValue = BlindHiddenLayer.Name;
                tagManager.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                return;
            }

            Debug.LogError("[Dishaster] BlindHidden katmanı için boş katman yok.");
        }
    }
}
