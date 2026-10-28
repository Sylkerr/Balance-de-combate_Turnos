using UnityEngine;

/// <summary>Tiñe de rojo el cuerpo de la unidad durante un instante cuando recibe daño.</summary>
public class DestelloGolpe : MonoBehaviour
{
    public Color colorGolpe = new Color(1f, 0.15f, 0.1f);
    public float duracion = 0.12f;

    private Renderer rend;
    private Color colorOriginal;
    private float restante;

    void Awake()
    {
        rend = GetComponent<Renderer>();
        if (rend != null) colorOriginal = rend.material.color;
    }

    public void Lanzar()
    {
        if (rend == null) return;
        restante = duracion;
        rend.material.color = colorGolpe;
    }

    void Update()
    {
        if (restante <= 0f || rend == null) return;

        restante -= Time.deltaTime;
        rend.material.color = Color.Lerp(colorOriginal, colorGolpe, Mathf.Clamp01(restante / duracion));
    }
}
