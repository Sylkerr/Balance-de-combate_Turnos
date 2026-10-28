using UnityEngine;

/// <summary>Movimiento, escala y desvanecimiento de un efecto de corta duración. Se destruye solo al acabar.</summary>
public class EfectoTemporal : MonoBehaviour
{
    public float duracion = 0.5f;
    public Vector3 velocidad;
    public Vector3 gravedad;
    public Vector3 escalaInicial = Vector3.one;
    public Vector3 escalaFinal = Vector3.one;
    public bool mirarCamara;

    public Renderer rend;   // Esferas y cubos
    public TextMesh texto;  // Números de daño

    private float tiempo;
    private Color colorBase;

    void Start()
    {
        transform.localScale = escalaInicial;
        if (rend != null) colorBase = rend.material.color;
        if (texto != null) colorBase = texto.color;
    }

    void Update()
    {
        tiempo += Time.deltaTime;
        float t = Mathf.Clamp01(tiempo / duracion);

        velocidad += gravedad * Time.deltaTime;
        transform.position += velocidad * Time.deltaTime;
        transform.localScale = Vector3.Lerp(escalaInicial, escalaFinal, t);

        // Se mantiene opaco la primera mitad y luego se apaga
        Color c = colorBase;
        c.a = colorBase.a * (1f - Mathf.SmoothStep(0f, 1f, (t - 0.5f) * 2f));
        if (rend != null) rend.material.color = c;
        if (texto != null) texto.color = c;

        if (mirarCamara && Camera.main != null)
            transform.rotation = Camera.main.transform.rotation;

        if (t >= 1f) Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (rend != null) Destroy(rend.material);
    }
}
