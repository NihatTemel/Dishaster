// Sarkit lamba (Ozgur / art). PF_Lamp_Pendant_01 prefabinin kokunde durur.
// Acik: nokta isik yanar ve kubbe parlar (mat hardal cam: tepede hardal, alt agza dogru sicak beyaz).
// Kapali: isik soner, kubbe parlamaz. Her lamba kendi parlakligini tasir (malzeme ortak kalir, MaterialPropertyBlock).
using UnityEngine;

[DisallowMultipleComponent]
public class Lamba : MonoBehaviour
{
    public bool acik = true;
    [Tooltip("Kubbenin parlaklik carpani (emission). Isigin gucu Isik nesnesindeki Light'ta.")]
    [Range(0f, 4f)] public float parlaklik = 1.5f;

    [HideInInspector] public Light isik;
    [HideInInspector] public Renderer kubbe;

    static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
    MaterialPropertyBlock mpb;

    public void Uygula()
    {
        if (isik) isik.enabled = acik;
        if (!kubbe) return;
        mpb ??= new MaterialPropertyBlock();
        kubbe.GetPropertyBlock(mpb, 0);   // 0: kubbe malzemesi (M_Lamp_Shade)
        mpb.SetColor(EmissionColor, Color.white * (acik ? parlaklik : 0f));
        kubbe.SetPropertyBlock(mpb, 0);
    }

    void OnEnable() => Uygula();
    void OnValidate() => Uygula();
}
