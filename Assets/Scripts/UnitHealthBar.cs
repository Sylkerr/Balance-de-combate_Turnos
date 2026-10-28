using UnityEngine;

/// <summary>
/// Barra de vida flotante sobre la cabeza de cada unidad. Se crea por código con dos quads
/// (fondo y relleno), se orienta siempre a la cámara y se oculta cuando la unidad muere.
/// </summary>
[RequireComponent(typeof(UnitStats))]
public class UnitHealthBar : MonoBehaviour
{
    [Header("Tamaño (metros de Unity)")]
    public float ancho = 0.9f;
    public float alto = 0.12f;
    [Tooltip("Separación extra entre la parte más alta de la unidad y la barra.")]
    public float margenSobreCabeza = 0.1f;

    [Header("Colores")]
    public Color colorVidaLlena = new Color(0.25f, 0.85f, 0.3f);
    public Color colorVidaBaja = new Color(0.9f, 0.15f, 0.15f);
    public Color colorFondo = new Color(0.05f, 0.05f, 0.05f);

    private UnitStats stats;
    private Camera cam;
    private Transform raiz;
    private Transform relleno;
    private Material matRelleno;
    private Material matFondo;
    private TextMesh etiqueta;
    private int vidaMostrada = -1;

    void Awake()
    {
        stats = GetComponent<UnitStats>();
        CrearBarra();
    }

    void CrearBarra()
    {
        raiz = new GameObject($"BarraVida_{name}").transform;

        matFondo = CrearMaterial(colorFondo);
        matRelleno = CrearMaterial(colorVidaLlena);

        // Fondo (un poco más grande para que haga de borde) detrás del relleno, mirando desde la cámara
        Transform fondo = CrearQuad("Fondo", matFondo);
        fondo.localScale = new Vector3(ancho + 0.04f, alto + 0.04f, 1f);
        fondo.localPosition = new Vector3(0f, 0f, 0.005f);

        relleno = CrearQuad("Relleno", matRelleno);
        relleno.localPosition = new Vector3(0f, 0f, -0.005f);
        relleno.localScale = new Vector3(ancho, alto, 1f);

        // Nombre y vida numérica encima de la barra, en el color del equipo
        etiqueta = CombatVFX.CrearTexto("", Vector3.zero, CombatVFX.ColorEquipo(stats.idJugador), 0.022f);
        etiqueta.transform.SetParent(raiz, false);
        etiqueta.transform.localPosition = new Vector3(0f, alto + 0.12f, 0f);
    }

    Transform CrearQuad(string nombre, Material mat)
    {
        GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = nombre;

        Collider col = q.GetComponent<Collider>();
        if (col != null) Destroy(col);

        q.transform.SetParent(raiz, false);

        Renderer r = q.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        return q.transform;
    }

    // Material sin iluminación para que la barra se vea igual con cualquier luz de la escena
    Material CrearMaterial(Color color)
    {
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Standard");

        Material m = new Material(sh);
        m.color = color;
        return m;
    }

    void LateUpdate()
    {
        if (raiz == null || stats == null) return;

        if (cam == null) cam = Camera.main;

        // Sigue a la unidad (por encima del punto más alto, sombrero incluido) y mira siempre a la cámara
        float altura = transform.localScale.y * 2.1f + margenSobreCabeza;
        raiz.position = transform.position + Vector3.up * altura;
        if (cam != null) raiz.rotation = cam.transform.rotation;

        float ratio = stats.vidaMaxima > 0f ? Mathf.Clamp01(stats.vidaActual / stats.vidaMaxima) : 0f;
        float anchoRelleno = Mathf.Max(0.001f, ancho * ratio);

        relleno.localScale = new Vector3(anchoRelleno, alto, 1f);
        relleno.localPosition = new Vector3(-ancho * 0.5f + anchoRelleno * 0.5f, 0f, -0.005f);
        matRelleno.color = Color.Lerp(colorVidaBaja, colorVidaLlena, ratio);

        int vida = Mathf.CeilToInt(Mathf.Max(0f, stats.vidaActual));
        if (etiqueta != null && vida != vidaMostrada)
        {
            vidaMostrada = vida;
            etiqueta.text = $"{stats.nombreUnidad}  {vida}/{stats.vidaMaxima:0}";
        }
    }

    // La unidad muerta se desactiva (SetActive(false)): su barra desaparece con ella
    void OnEnable()
    {
        if (raiz != null) raiz.gameObject.SetActive(true);
    }

    void OnDisable()
    {
        if (raiz != null) raiz.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (raiz != null) Destroy(raiz.gameObject);
        if (matRelleno != null) Destroy(matRelleno);
        if (matFondo != null) Destroy(matFondo);
    }
}
