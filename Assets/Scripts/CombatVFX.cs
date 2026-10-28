using UnityEngine;

/// <summary>
/// Efectos visuales del combate creados por código (no hace falta ningún prefab):
/// números de daño flotantes, destello al recibir un golpe, proyectiles, impactos y explosión al morir.
/// </summary>
public static class CombatVFX
{
    public static readonly Color ColorJ1 = new Color(0.35f, 0.75f, 1f);
    public static readonly Color ColorJ2 = new Color(1f, 0.35f, 0.3f);

    public static Color ColorEquipo(int idJugador) => idJugador == 1 ? ColorJ1 : ColorJ2;

    private static Font fuente;

    static Font Fuente()
    {
        if (fuente == null) fuente = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (fuente == null) fuente = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return fuente;
    }

    public static Material MaterialSinLuz(Color color)
    {
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Standard");

        Material m = new Material(sh);
        m.color = color;
        return m;
    }

    /// <summary>Texto 3D que siempre mira a cámara. Lo usan los números de daño y las etiquetas de las unidades.</summary>
    public static TextMesh CrearTexto(string contenido, Vector3 pos, Color color, float tamano)
    {
        GameObject go = new GameObject("Texto3D");
        go.transform.position = pos;

        TextMesh tm = go.AddComponent<TextMesh>();
        tm.font = Fuente();
        tm.text = contenido;
        tm.fontSize = 64;
        tm.characterSize = tamano;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = color;
        tm.richText = true;

        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (tm.font != null) mr.sharedMaterial = tm.font.material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return tm;
    }

    // ---------------------------------------------------------
    // Número de daño: sube, se desplaza un poco hacia un lado y se desvanece
    // ---------------------------------------------------------
    public static void NumeroFlotante(Vector3 pos, float dano, int idVictima)
    {
        Vector3 lateral = new Vector3(Random.Range(-0.25f, 0.25f), 0f, Random.Range(-0.25f, 0.25f));
        TextMesh tm = CrearTexto($"-{dano:0.#}", pos + lateral, new Color(1f, 0.92f, 0.3f), 0.045f);

        EfectoTemporal e = tm.gameObject.AddComponent<EfectoTemporal>();
        e.duracion = 0.9f;
        e.velocidad = Vector3.up * 1.4f + lateral;
        e.gravedad = Vector3.down * 1.2f;
        e.escalaInicial = Vector3.one * 1.3f;
        e.escalaFinal = Vector3.one * 0.9f;
        e.texto = tm;
        e.mirarCamara = true;
    }

    // ---------------------------------------------------------
    // La unidad golpeada se tiñe de rojo un instante
    // ---------------------------------------------------------
    public static void Destello(GameObject unidad)
    {
        DestelloGolpe d = unidad.GetComponent<DestelloGolpe>();
        if (d == null) d = unidad.AddComponent<DestelloGolpe>();
        d.Lanzar();
    }

    // ---------------------------------------------------------
    // Golpe cuerpo a cuerpo / llegada de un proyectil: esfera que se expande y se apaga
    // ---------------------------------------------------------
    public static void Impacto(Vector3 pos, Color color, float tamano = 0.6f)
    {
        GameObject go = CrearPrimitivaVisual(PrimitiveType.Sphere, pos, color);
        EfectoTemporal e = go.AddComponent<EfectoTemporal>();
        e.duracion = 0.25f;
        e.escalaInicial = Vector3.one * tamano * 0.3f;
        e.escalaFinal = Vector3.one * tamano;
        e.rend = go.GetComponent<Renderer>();

        // Unas chispas que salen despedidas
        for (int i = 0; i < 5; i++)
            Fragmento(pos, color, 0.08f, 2.5f, 0.35f);
    }

    // ---------------------------------------------------------
    // Muerte: estallido de cubos del color del equipo
    // ---------------------------------------------------------
    public static void Muerte(Vector3 pos, int idJugador)
    {
        Color c = ColorEquipo(idJugador);
        for (int i = 0; i < 16; i++)
            Fragmento(pos, c, Random.Range(0.1f, 0.2f), 4f, 0.9f);

        Impacto(pos, Color.white, 1.4f);
    }

    static void Fragmento(Vector3 pos, Color color, float tamano, float fuerza, float duracion)
    {
        GameObject go = CrearPrimitivaVisual(PrimitiveType.Cube, pos, color);
        go.transform.rotation = Random.rotation;

        EfectoTemporal e = go.AddComponent<EfectoTemporal>();
        e.duracion = duracion;
        Vector3 dir = Random.onUnitSphere;
        dir.y = Mathf.Abs(dir.y) + 0.3f;
        e.velocidad = dir.normalized * fuerza * Random.Range(0.5f, 1f);
        e.gravedad = Vector3.down * 9f;
        e.escalaInicial = Vector3.one * tamano;
        e.escalaFinal = Vector3.zero;
        e.rend = go.GetComponent<Renderer>();
    }

    // ---------------------------------------------------------
    // Proyectil de las unidades a distancia. El daño se aplica cuando llega.
    // ---------------------------------------------------------
    public static void Disparar(Vector3 origen, UnitStats objetivo, Color color, System.Action alImpactar)
    {
        GameObject go = CrearPrimitivaVisual(PrimitiveType.Sphere, origen, color);
        go.transform.localScale = Vector3.one * 0.18f;

        Proyectil p = go.AddComponent<Proyectil>();
        p.objetivo = objetivo;
        p.color = color;
        p.alImpactar = alImpactar;
    }

    public static GameObject CrearPrimitivaVisual(PrimitiveType tipo, Vector3 pos, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(tipo);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.position = pos;

        Renderer r = go.GetComponent<Renderer>();
        r.material = MaterialSinLuz(color);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }
}
