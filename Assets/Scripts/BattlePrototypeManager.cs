using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class BattlePrototypeManager : MonoBehaviour
{
    public static BattlePrototypeManager Instance { get; private set; }

    public enum EstadoPartida
    {
        Colocacion,
        Combate,
        MarchaVictoria,
        Finalizado
    }

    [Header("Estado Actual")]
    public EstadoPartida EstadoActual = EstadoPartida.Colocacion;

    [Header("Escena (puntos creados por ti)")]
    [Tooltip("Si lo dejas vacío se busca automáticamente un BattleLayout en la escena.")]
    public BattleLayout layout;
    public int maxUnidadesPorEquipo = 9;
    [Tooltip("Altura del centro de la cápsula sobre el pivote de la casilla al colocarla.")]
    public float alturaUnidadSobreCasilla = 0.65f;

    [Header("Combate en área")]
    [Tooltip("Máximo de unidades cuerpo a cuerpo que pueden atacar a la vez al mismo enemigo (evita el apelotonamiento).")]
    public int maxAtacantesPorObjetivo = 3;
    [Tooltip("Las unidades con alcance <= a este valor cuentan como cuerpo a cuerpo.")]
    public float alcanceMaxCuerpoACuerpo = 2f;
    [Tooltip("Solo se atacan enemigos que estén dentro de la arena (más el margen).")]
    public bool limitarCombateAlArea = true;
    public float margenArea = 1.5f;

    public int MascaraCasillas => layout != null ? layout.capaCasillas.value : ~0;

    private List<UnitStats> unidadesEnTablero = new List<UnitStats>();
    private int equipoGanador = 0;

    // Referencias de UI generada automáticamente
    private Text textoEstadoSuperior;
    private Button botonIniciarCombate;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        if (layout == null) layout = FindAnyObjectByType<BattleLayout>();
        if (layout == null)
        {
            Debug.LogError("[BattleManager] No hay ningún BattleLayout en la escena. Crea un objeto vacío con el componente BattleLayout y asigna sus puntos.");
            enabled = false;
            return;
        }

        AsegurarEventSystem();
        CrearCasillas(1);
        CrearCasillas(2);
        CrearInterfazDobleDragAndDrop();

        if (ExcelManager.Instance != null)
        {
            if (ExcelManager.Instance.DatosCargados)
                PoblarListasUI();
            else
                ExcelManager.Instance.OnDatosCargados += PoblarListasUI;
        }
    }

    void Update()
    {
        if (EstadoActual == EstadoPartida.MarchaVictoria)
        {
            ComprobarLlegadaAlLadoEnemigo();
        }
    }

    // =========================================================
    // 1. CASILLAS: usa las tuyas (hijos del ancla) o las genera en tus puntos
    // =========================================================
    void CrearCasillas(int idJugador)
    {
        Transform ancla = layout.AnclaDe(idJugador);
        if (ancla == null)
        {
            Debug.LogError($"[BattleManager] Falta asignar el ancla del Jugador {idJugador} en el BattleLayout.");
            return;
        }

        Color color = idJugador == 1 ? layout.colorCasillaJ1 : layout.colorCasillaJ2;

        // Caso A: ya has colocado tus propias casillas (hijos con GridTile)
        GridTile[] existentes = ancla.GetComponentsInChildren<GridTile>();
        if (existentes.Length > 0)
        {
            for (int i = 0; i < existentes.Length; i++)
            {
                existentes[i].InicializarCasilla(idJugador, i / layout.columnas, i % layout.columnas, color, layout.tintarCasillas);
                AsegurarCollider(existentes[i].gameObject);
            }
            return;
        }

        // Caso B: se generan a partir del ancla
        for (int fila = 0; fila < layout.filas; fila++)
        {
            for (int col = 0; col < layout.columnas; col++)
            {
                Vector3 pos = BattleLayout.PosicionCasilla(ancla, layout.tamanoCasilla, fila, col);
                GameObject tileObj;

                if (layout.prefabCasilla != null)
                {
                    tileObj = Instantiate(layout.prefabCasilla, pos, ancla.rotation, ancla);
                }
                else
                {
                    tileObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    tileObj.transform.SetParent(ancla, true);
                    tileObj.transform.position = pos;
                    tileObj.transform.rotation = ancla.rotation;
                    tileObj.transform.localScale = new Vector3(layout.tamanoCasilla * 0.92f, 0.2f, layout.tamanoCasilla * 0.92f);
                }

                tileObj.name = $"Celda_J{idJugador}_{fila}_{col}";

                GridTile tile = tileObj.GetComponent<GridTile>();
                if (tile == null) tile = tileObj.AddComponent<GridTile>();
                AsegurarCollider(tileObj);
                tile.InicializarCasilla(idJugador, fila, col, color, layout.tintarCasillas);
            }
        }
    }

    void AsegurarCollider(GameObject go)
    {
        if (go.GetComponentInChildren<Collider>() == null)
            go.AddComponent<BoxCollider>();
    }

    public Vector3 ObtenerPuntoAgrupacion(int idJugador)
    {
        return layout.AjustarANavMesh(layout.PuntoDeAgrupacion(idJugador));
    }

    public Vector3 ObtenerMetaVictoria(int idGanador)
    {
        return layout.AjustarANavMesh(layout.PuntoMetaVictoria(idGanador));
    }

    // =========================================================
    // ACCESORIOS VISUALES POR ROL
    // (las medidas están en unidades locales de la cápsula: radio 0.5, altura 2)
    // =========================================================
    [Header("Accesorios por rol")]
    [Tooltip("Los tanques se crean con este factor de tamaño respecto al resto.")]
    public float factorEscalaTanque = 1.25f;

    private static Mesh mallaCono;

    static bool EsRol(string rol, params string[] claves)
    {
        if (string.IsNullOrEmpty(rol)) return false;
        string r = rol.ToLowerInvariant();
        foreach (string c in claves)
            if (r.Contains(c)) return true;
        return false;
    }

    /// <summary>Orden = azul, Caos = rojo, Vida = verde, Vacío = morado. Otra facción: color por defecto del accesorio.</summary>
    static Color ColorAccesorio(string faccion, Color porDefecto)
    {
        if (string.IsNullOrEmpty(faccion)) return porDefecto;
        string f = faccion.Trim().ToLowerInvariant();

        if (f.Contains("orden")) return new Color(0.15f, 0.4f, 1f);
        if (f.Contains("caos")) return new Color(0.9f, 0.15f, 0.15f);
        if (f.Contains("vida")) return new Color(0.2f, 0.8f, 0.25f);
        if (f.Contains("vac")) return new Color(0.6f, 0.2f, 0.85f);   // Vacío / Vacio

        return porDefecto;
    }

    /// <summary>Mira desde su propia cuadrícula hacia la del rival (en una escena simétrica equivale a girar 180º al J2).</summary>
    Quaternion RotacionMirandoAlRival(int idJugador)
    {
        int rival = idJugador == 1 ? 2 : 1;
        Vector3 dir = layout.CentroGrid(rival) - layout.CentroGrid(idJugador);
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.01f)
            return idJugador == 2 ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;

        return Quaternion.LookRotation(dir.normalized, Vector3.up);
    }

    void AnadirAccesoriosPorRol(GameObject unidad, string rol, string faccion)
    {
        Transform t = unidad.transform;

        if (EsRol(rol, "mag", "mág"))
        {
            // Sombrero de mago: cono sobre la cabeza
            CrearAccesorio(t, PrimitiveType.Cube, "Sombrero", new Vector3(0f, 0.8f, 0f), Vector3.zero,
                Vector3.one, ColorAccesorio(faccion, new Color(0.45f, 0.2f, 0.75f)), ObtenerMallaCono());
        }
        else if (EsRol(rol, "luchador"))
        {
            // Espada: cilindro pequeño en la mano derecha, inclinado hacia delante
            CrearAccesorio(t, PrimitiveType.Cylinder, "Espada", new Vector3(0.65f, 0.15f, 0.3f), new Vector3(35f, 0f, 0f),
                new Vector3(0.12f, 0.6f, 0.12f), ColorAccesorio(faccion, new Color(0.8f, 0.8f, 0.85f)));
        }
        else if (EsRol(rol, "tanque", "tank"))
        {
            // Manos: dos cubos pequeños a los lados
            Color piel = ColorAccesorio(faccion, new Color(0.9f, 0.75f, 0.6f));
            CrearAccesorio(t, PrimitiveType.Cube, "Mano_Izq", new Vector3(-0.62f, 0f, 0.35f), Vector3.zero, Vector3.one * 0.28f, piel);
            CrearAccesorio(t, PrimitiveType.Cube, "Mano_Der", new Vector3(0.62f, 0f, 0.35f), Vector3.zero, Vector3.one * 0.28f, piel);
        }
        else if (EsRol(rol, "arquero", "archer"))
        {
            // Rifle: cubo largo apuntando hacia delante
            CrearAccesorio(t, PrimitiveType.Cube, "Rifle", new Vector3(0.45f, 0.1f, 0.55f), Vector3.zero,
                new Vector3(0.12f, 0.14f, 1.3f), ColorAccesorio(faccion, new Color(0.3f, 0.2f, 0.1f)));
        }
    }

    GameObject CrearAccesorio(Transform padre, PrimitiveType tipo, string nombre, Vector3 posLocal, Vector3 rotEuler,
                              Vector3 escalaLocal, Color color, Mesh mallaPersonalizada = null)
    {
        GameObject go = GameObject.CreatePrimitive(tipo);
        go.name = nombre;

        Collider col = go.GetComponent<Collider>();
        if (col != null) Destroy(col); // Los accesorios son solo visuales

        go.transform.SetParent(padre, false);
        go.transform.localPosition = posLocal;
        go.transform.localRotation = Quaternion.Euler(rotEuler);
        go.transform.localScale = escalaLocal;

        if (mallaPersonalizada != null)
            go.GetComponent<MeshFilter>().sharedMesh = mallaPersonalizada;

        Renderer r = go.GetComponent<Renderer>();
        if (r != null) r.material.color = color;

        return go;
    }

    /// <summary>Cono de base en y=0 y punta en y=altura (Unity no trae un cono como primitiva).</summary>
    static Mesh ObtenerMallaCono(float radio = 0.6f, float altura = 1f, int segmentos = 16)
    {
        if (mallaCono != null) return mallaCono;

        var vertices = new List<Vector3>();
        var triangulos = new List<int>();

        vertices.Add(new Vector3(0f, altura, 0f));           // 0: punta
        for (int i = 0; i < segmentos; i++)                   // 1..segmentos: anillo de la base
        {
            float a = i * Mathf.PI * 2f / segmentos;
            vertices.Add(new Vector3(Mathf.Cos(a) * radio, 0f, Mathf.Sin(a) * radio));
        }
        vertices.Add(Vector3.zero);                           // centro de la base
        int centro = segmentos + 1;

        for (int i = 0; i < segmentos; i++)
        {
            int a = 1 + i;
            int b = 1 + (i + 1) % segmentos;

            triangulos.Add(0); triangulos.Add(b); triangulos.Add(a);          // lateral
            triangulos.Add(centro); triangulos.Add(a); triangulos.Add(b);     // base
        }

        mallaCono = new Mesh { name = "Cono_Sombrero" };
        mallaCono.SetVertices(vertices);
        mallaCono.SetTriangles(triangulos, 0);
        mallaCono.RecalculateNormals();
        mallaCono.RecalculateBounds();
        return mallaCono;
    }

    // =========================================================
    // 2. COLOCACIÓN DE UNIDADES POR DRAG & DROP (MÁX 12 POR BANDO)
    // =========================================================
    public void IntentarColocarUnidadPorDrag(GridTile casilla, string unitKey, int idJugador)
    {
        if (EstadoActual != EstadoPartida.Colocacion) return;

        if (casilla.idPropietarioCasilla != idJugador)
        {
            Debug.LogWarning($"Solo puedes arrastrar unidades del panel del Jugador {idJugador} a la cuadrícula del Jugador {idJugador}.");
            return;
        }

        if (casilla.EstaOcupada)
        {
            Debug.LogWarning("Esta celda ya está ocupada.");
            return;
        }

        int unidadesEquipo = ContarUnidadesVivas(idJugador);
        if (unidadesEquipo >= maxUnidadesPorEquipo)
        {
            Debug.LogWarning($"El Jugador {idJugador} ya tiene el máximo de {maxUnidadesPorEquipo} unidades colocadas.");
            return;
        }

        if (!ExcelManager.Instance.UnidadesPorNombre.TryGetValue(unitKey, out var datosUnidad))
        {
            Debug.LogError($"No se encontraron datos en el Excel para: {unitKey}");
            return;
        }

        GameObject nuevaUnidad = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        nuevaUnidad.name = $"{datosUnidad.nombre}_J{idJugador}";
        float escala = EsRol(datosUnidad.rol, "tanque", "tank") ? 0.55f * factorEscalaTanque : 0.55f;
        nuevaUnidad.transform.position = casilla.transform.position + Vector3.up * (alturaUnidadSobreCasilla + (escala - 0.55f));
        nuevaUnidad.transform.localScale = new Vector3(escala, escala, escala);
        nuevaUnidad.transform.rotation = RotacionMirandoAlRival(idJugador);

        // El NavMeshAgent se encarga de evitar a otras unidades; el collider solo sirve de referencia
        Collider col = nuevaUnidad.GetComponent<Collider>();
        if (col != null) col.isTrigger = true;

        Renderer rend = nuevaUnidad.GetComponent<Renderer>();
        if (rend != null)
            rend.material.color = (idJugador == 1) ? Color.white : new Color(0.08f, 0.08f, 0.08f);

        AnadirAccesoriosPorRol(nuevaUnidad, datosUnidad.rol, datosUnidad.faccion);
        nuevaUnidad.AddComponent<UnitAttackAnimator>(); // Debe ir después de crear los accesorios

        UnitStats stats = nuevaUnidad.AddComponent<UnitStats>();
        stats.InicializarDesdeDatos(datosUnidad, idJugador);
        stats.casillaAsignada = casilla;

        nuevaUnidad.AddComponent<UnitHealthBar>(); // Después de UnitStats, que es de donde lee la vida
        nuevaUnidad.AddComponent<UnitCombatAI>();

        casilla.unidadEnCasilla = stats;
        unidadesEnTablero.Add(stats);

        ActualizarTextoEstado($"Unidad colocada: {datosUnidad.nombre} (J{idJugador}) | Aliadas: {ContarUnidadesVivas(1)}/12 - Enemigas: {ContarUnidadesVivas(2)}/12");
    }

    // =========================================================
    // 3. COMBATE, VICTORIA, MARCHA Y REINICIO
    // =========================================================
    public void IniciarCombate()
    {
        if (EstadoActual != EstadoPartida.Colocacion) return;

        if (ContarUnidadesVivas(1) == 0 || ContarUnidadesVivas(2) == 0)
        {
            ActualizarTextoEstado("¡Arrastra al menos 1 unidad a cada cuadrícula (Aliada y Enemiga) antes de pelear!");
            return;
        }

        EstadoActual = EstadoPartida.Combate;
        if (botonIniciarCombate != null) botonIniciarCombate.interactable = false;

        // Cada unidad recibe su propio punto dentro de su mitad de la arena
        foreach (UnitStats u in unidadesEnTablero)
        {
            if (u == null || !u.EstaVivo) continue;
            UnitCombatAI ai = u.GetComponent<UnitCombatAI>();
            if (ai != null) ai.IniciarNavegacion(ObtenerPuntoAgrupacion(u.idJugador));
        }

        ActualizarTextoEstado("⚔️ ¡COMBATE EN CURSO EN LA ARENA!");
    }

    /// <summary>
    /// Elige objetivo: el enemigo más cercano, priorizando los que aún tienen hueco para atacantes cuerpo a cuerpo.
    /// Si limitarCombateAlArea está activo, solo se consideran enemigos dentro de la arena.
    /// </summary>
    public UnitStats BuscarObjetivo(UnitStats solicitante, bool ignorarArea = false)
    {
        bool restringirArea = limitarCombateAlArea && !ignorarArea && layout != null && layout.arena.EsValida;
        bool soyCuerpoACuerpo = solicitante.alcance <= alcanceMaxCuerpoACuerpo;

        UnitStats mejorConHueco = null;
        UnitStats mejorCualquiera = null;
        float dHueco = float.MaxValue;
        float dCualquiera = float.MaxValue;

        foreach (UnitStats u in unidadesEnTablero)
        {
            if (u == null || !u.EstaVivo || u.idJugador == solicitante.idJugador) continue;
            if (restringirArea && !layout.arena.Contiene(u.transform.position, margenArea)) continue;

            float d = Vector3.Distance(solicitante.transform.position, u.transform.position);

            if (d < dCualquiera)
            {
                dCualquiera = d;
                mejorCualquiera = u;
            }

            if (u.atacantesActuales < maxAtacantesPorObjetivo && d < dHueco)
            {
                dHueco = d;
                mejorConHueco = u;
            }
        }

        // Cuerpo a cuerpo: prefiere un enemigo con hueco libre; si todos están "llenos", ataca al más cercano
        if (soyCuerpoACuerpo && mejorConHueco != null) return mejorConHueco;
        return mejorCualquiera;
    }

    public void NotificarMuerteUnidad(UnitStats unidadMuerta)
    {
        unidadMuerta.gameObject.SetActive(false);

        int vivasJ1 = ContarUnidadesVivas(1);
        int vivasJ2 = ContarUnidadesVivas(2);

        if (vivasJ1 > 0 && vivasJ2 == 0)
        {
            IniciarMarchaVictoria(1);
        }
        else if (vivasJ2 > 0 && vivasJ1 == 0)
        {
            IniciarMarchaVictoria(2);
        }
        else if (vivasJ1 == 0 && vivasJ2 == 0)
        {
            EstadoActual = EstadoPartida.Finalizado;
            ActualizarTextoEstado("¡EMPATE! Reiniciando escena...");
            StartCoroutine(ReiniciarEscenaTrasSegundos(2.5f));
        }
    }

    void IniciarMarchaVictoria(int ganador)
    {
        equipoGanador = ganador;
        EstadoActual = EstadoPartida.MarchaVictoria;
        string nombreBando = ganador == 1 ? "BASE ALIADA (J1)" : "BASE ENEMIGA (J2)";
        ActualizarTextoEstado($"¡{nombreBando} HA DERROTADO AL RIVAL! Marchando hacia la base rival...");

        foreach (UnitStats u in unidadesEnTablero)
        {
            if (u == null || !u.EstaVivo || u.idJugador != ganador) continue;
            UnitCombatAI ai = u.GetComponent<UnitCombatAI>();
            if (ai != null) ai.IniciarMarcha(ObtenerMetaVictoria(ganador));
        }
    }

    void ComprobarLlegadaAlLadoEnemigo()
    {
        bool todasCruzaron = true;

        foreach (UnitStats u in unidadesEnTablero)
        {
            if (u != null && u.EstaVivo && u.idJugador == equipoGanador)
            {
                UnitCombatAI ai = u.GetComponent<UnitCombatAI>();
                if (ai != null && !ai.HaCruzadoPuenteEnemigo)
                {
                    todasCruzaron = false;
                    break;
                }
            }
        }

        if (todasCruzaron)
        {
            EstadoActual = EstadoPartida.Finalizado;
            string nombreBando = equipoGanador == 1 ? "JUGADOR 1 (BASE ALIADA)" : "JUGADOR 2 (BASE ENEMIGA)";
            ActualizarTextoEstado($"🏆 ¡VICTORIA DEL {nombreBando}! Reiniciando partida...");
            StartCoroutine(ReiniciarEscenaTrasSegundos(2.5f));
        }
    }

    IEnumerator ReiniciarEscenaTrasSegundos(float segundos)
    {
        yield return new WaitForSeconds(segundos);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    int ContarUnidadesVivas(int idJugador)
    {
        int cuenta = 0;
        foreach (var u in unidadesEnTablero)
        {
            if (u != null && u.EstaVivo && u.idJugador == idJugador)
                cuenta++;
        }
        return cuenta;
    }

    // =========================================================
    // 4. CREACIÓN AUTOMÁTICA DE LAS 2 UI LATERALES (DRAG & DROP)
    // =========================================================
    private Transform contenedorListaJ1;
    private Transform contenedorListaJ2;

    void AsegurarEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }
    }

    void CrearInterfazDobleDragAndDrop()
    {
        GameObject canvasObj = new GameObject("Canvas_Prototipo");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasObj.AddComponent<GraphicRaycaster>();
        canvasRaiz = canvasObj.transform;

        // Panel Izquierdo: Selección Base Aliada (Jugador 1)
        contenedorListaJ1 = CrearPanelLateral(canvasObj.transform, "Panel_Aliado_J1",
            new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(10, 0),
            "UNIDADES BASE ALIADA\n(Arrastra a tu Grid)", new Color(0.1f, 0.25f, 0.15f, 0.9f));

        // Panel Derecho: Selección Base Enemiga (Jugador 2)
        contenedorListaJ2 = CrearPanelLateral(canvasObj.transform, "Panel_Enemigo_J2",
            new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(-10, 0),
            "UNIDADES BASE ENEMIGA\n(Arrastra a su Grid)", new Color(0.3f, 0.12f, 0.12f, 0.9f));

        // Barra Superior: Estado y Botón de Iniciar Combate
        GameObject panelTop = new GameObject("Panel_Superior");
        panelTop.transform.SetParent(canvasObj.transform, false);
        Image imgTop = panelTop.AddComponent<Image>();
        imgTop.color = new Color(0.12f, 0.12f, 0.12f, 0.9f);
        RectTransform rtTop = panelTop.GetComponent<RectTransform>();
        rtTop.anchorMin = new Vector2(0.5f, 1f);
        rtTop.anchorMax = new Vector2(0.5f, 1f);
        rtTop.pivot = new Vector2(0.5f, 1f);
        rtTop.sizeDelta = new Vector2(900, 110);
        rtTop.anchoredPosition = new Vector2(0, -10);

        textoEstadoSuperior = CrearTextoUI(panelTop.transform, "Cargando estadísticas desde Google Sheets...", 20, TextAnchor.UpperCenter);
        RectTransform rtTxt = textoEstadoSuperior.GetComponent<RectTransform>();
        rtTxt.anchorMin = new Vector2(0, 0.45f);
        rtTxt.anchorMax = new Vector2(1, 1f);
        rtTxt.offsetMin = new Vector2(10, 0);
        rtTxt.offsetMax = new Vector2(-10, -5);

        // Botón Iniciar Combate
        GameObject btnObj = new GameObject("Boton_IniciarCombate");
        btnObj.transform.SetParent(panelTop.transform, false);
        Image imgBtn = btnObj.AddComponent<Image>();
        imgBtn.color = new Color(0.2f, 0.7f, 0.3f);
        botonIniciarCombate = btnObj.AddComponent<Button>();
        botonIniciarCombate.onClick.AddListener(IniciarCombate);

        RectTransform rtBtn = btnObj.GetComponent<RectTransform>();
        rtBtn.anchorMin = new Vector2(0.5f, 0f);
        rtBtn.anchorMax = new Vector2(0.5f, 0f);
        rtBtn.pivot = new Vector2(0.5f, 0f);
        rtBtn.sizeDelta = new Vector2(280, 42);
        rtBtn.anchoredPosition = new Vector2(0, 8);

        Text txtBtn = CrearTextoUI(btnObj.transform, "⚔️ PELEAR", 20, TextAnchor.MiddleCenter);
        SetFullStretch(txtBtn.GetComponent<RectTransform>());
    }

    Transform CrearPanelLateral(Transform parent, string nombre, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, string titulo, Color colorFondo)
    {
        GameObject panel = new GameObject(nombre);
        panel.transform.SetParent(parent, false);
        Image img = panel.AddComponent<Image>();
        img.color = colorFondo;

        RectTransform rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.sizeDelta = new Vector2(310, -20);
        rt.anchoredPosition = pos;

        Text lblTitulo = CrearTextoUI(panel.transform, titulo, 16, TextAnchor.MiddleCenter);
        RectTransform rtTit = lblTitulo.GetComponent<RectTransform>();
        rtTit.anchorMin = new Vector2(0, 1);
        rtTit.anchorMax = new Vector2(1, 1);
        rtTit.pivot = new Vector2(0.5f, 1);
        rtTit.sizeDelta = new Vector2(0, 50);
        rtTit.anchoredPosition = new Vector2(0, -5);

        // Zona con scroll: aquí van los desplegables de cada facción
        GameObject scrollObj = new GameObject("Scroll_Facciones");
        scrollObj.transform.SetParent(panel.transform, false);
        RectTransform rtScroll = scrollObj.AddComponent<RectTransform>();
        rtScroll.anchorMin = Vector2.zero;
        rtScroll.anchorMax = Vector2.one;
        rtScroll.offsetMin = new Vector2(8, 8);
        rtScroll.offsetMax = new Vector2(-8, -60);
        Image imgScroll = scrollObj.AddComponent<Image>();
        imgScroll.color = Color.clear; // Invisible, pero recibe la rueda del ratón
        scrollObj.AddComponent<RectMask2D>();
        ScrollRect scroll = scrollObj.AddComponent<ScrollRect>();

        GameObject contenedor = new GameObject("Contenedor_Facciones");
        contenedor.transform.SetParent(scrollObj.transform, false);
        RectTransform rtCont = contenedor.AddComponent<RectTransform>();
        rtCont.anchorMin = new Vector2(0, 1);
        rtCont.anchorMax = new Vector2(1, 1);
        rtCont.pivot = new Vector2(0.5f, 1);
        rtCont.sizeDelta = Vector2.zero;
        rtCont.anchoredPosition = Vector2.zero;

        ConfigurarVLG(contenedor.AddComponent<VerticalLayoutGroup>(), 4f);
        ContentSizeFitter fitter = contenedor.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = rtScroll;
        scroll.content = rtCont;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 25f;

        return contenedor.transform;
    }

    void PoblarListasUI()
    {
        ActualizarTextoEstado("Arrastra hasta 12 unidades desde ambos paneles hacia cada cuadrícula y pulsa PELEAR.");

        LimpiarHijos(contenedorListaJ1);
        LimpiarHijos(contenedorListaJ2);
        if (panelVentajas != null) Destroy(panelVentajas);

        // Agrupar unidades por facción manteniendo el orden del Excel
        List<string> facciones = new List<string>();
        Dictionary<string, List<ExcelManager.UnitData>> porFaccion =
            new Dictionary<string, List<ExcelManager.UnitData>>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var u in ExcelManager.Instance.ListaUnidades)
        {
            string f = string.IsNullOrWhiteSpace(u.faccion) ? "Sin facción" : u.faccion.Trim();
            if (!porFaccion.TryGetValue(f, out var lista))
            {
                lista = new List<ExcelManager.UnitData>();
                porFaccion[f] = lista;
                facciones.Add(f);
            }
            lista.Add(u);
        }

        foreach (string f in facciones)
        {
            CrearGrupoFaccion(contenedorListaJ1, f, porFaccion[f], 1);
            CrearGrupoFaccion(contenedorListaJ2, f, porFaccion[f], 2);
        }

        CrearPanelVentajasFacciones(facciones);
    }

    // ---------------------------------------------------------
    // Desplegables por facción, nivel de poder y panel de ventajas
    // ---------------------------------------------------------
    private Transform canvasRaiz;
    private GameObject panelVentajas;

    /// <summary>
    /// Nivel de poder base de una unidad (solo para orientar al jugador).
    /// Combina daño por segundo y vida efectiva (la vida ajustada por su defensa media).
    /// Solo se usa como respaldo cuando una unidad no tiene "Poder de combate" en el Excel.
    /// </summary>
    public static float CalcularNivelPoder(ExcelManager.UnitData d)
    {
        float dps = d.danoAtaque * d.velocidadAtaque;
        float defMedia = Mathf.Clamp((d.defensaFisica + d.defensaMagica) * 0.5f, 0f, 0.9f);
        float vidaEfectiva = d.vida / (1f - defMedia);
        return Mathf.Round(Mathf.Sqrt(Mathf.Max(0f, dps * vidaEfectiva)));
    }

    void LimpiarHijos(Transform padre)
    {
        if (padre == null) return;
        foreach (Transform hijo in padre) Destroy(hijo.gameObject);
    }

    void ConfigurarVLG(VerticalLayoutGroup v, float spacing)
    {
        v.spacing = spacing;
        v.childControlHeight = true;
        v.childControlWidth = true;
        v.childForceExpandHeight = false;
        v.childForceExpandWidth = true;
    }

    /// <summary>Crea una cabecera clicable que muestra/oculta un cuerpo. Devuelve el objeto raíz del grupo.</summary>
    GameObject CrearDesplegable(Transform padre, string nombre, string titulo, Color colorCabecera, bool abierto, out Transform cuerpo)
    {
        GameObject grupo = new GameObject(nombre);
        grupo.transform.SetParent(padre, false);
        ConfigurarVLG(grupo.AddComponent<VerticalLayoutGroup>(), 2f);

        GameObject cab = new GameObject("Cabecera");
        cab.transform.SetParent(grupo.transform, false);
        Image imgCab = cab.AddComponent<Image>();
        imgCab.color = colorCabecera;
        Button btn = cab.AddComponent<Button>();
        LayoutElement leCab = cab.AddComponent<LayoutElement>();
        leCab.preferredHeight = 34f;

        Text txt = CrearTextoUI(cab.transform, "", 15, TextAnchor.MiddleLeft);
        RectTransform rtTxt = txt.GetComponent<RectTransform>();
        SetFullStretch(rtTxt);
        rtTxt.offsetMin = new Vector2(8, 0);
        rtTxt.offsetMax = new Vector2(-8, 0);

        GameObject cuerpoObj = new GameObject("Cuerpo");
        cuerpoObj.transform.SetParent(grupo.transform, false);
        ConfigurarVLG(cuerpoObj.AddComponent<VerticalLayoutGroup>(), 2f);
        cuerpoObj.SetActive(abierto);

        RectTransform rtGrupo = grupo.GetComponent<RectTransform>();
        void RefrescarTitulo() { txt.text = (cuerpoObj.activeSelf ? "-  " : "+  ") + titulo; }
        RefrescarTitulo();

        btn.onClick.AddListener(() =>
        {
            cuerpoObj.SetActive(!cuerpoObj.activeSelf);
            RefrescarTitulo();

            RectTransform raiz = rtGrupo;
            if (rtGrupo.parent != null && rtGrupo.parent.GetComponent<ContentSizeFitter>() != null)
                raiz = (RectTransform)rtGrupo.parent;
            LayoutRebuilder.ForceRebuildLayoutImmediate(raiz);
        });

        cuerpo = cuerpoObj.transform;
        return grupo;
    }

    void CrearGrupoFaccion(Transform padre, string faccion, List<ExcelManager.UnitData> unidades, int idJugador)
    {
        Color colorCab = idJugador == 1 ? new Color(0.12f, 0.32f, 0.22f) : new Color(0.38f, 0.14f, 0.14f);

        CrearDesplegable(padre, $"Grupo_{faccion}_J{idJugador}", $"{faccion.ToUpperInvariant()} ({unidades.Count})",
            colorCab, false, out Transform cuerpo);

        foreach (var u in unidades)
            CrearTarjetaArrastrable(cuerpo, u, idJugador);
    }

    void CrearPanelVentajasFacciones(List<string> facciones)
    {
        if (canvasRaiz == null || facciones.Count == 0) return;

        panelVentajas = CrearDesplegable(canvasRaiz, "Panel_Ventajas_Facciones", "VENTAJAS ENTRE FACCIONES",
            new Color(0.15f, 0.15f, 0.25f, 0.95f), false, out Transform cuerpo);

        RectTransform rt = panelVentajas.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(760f, 0f);
        rt.anchoredPosition = new Vector2(0f, -125f); // Justo debajo de la barra superior

        ContentSizeFitter fitter = panelVentajas.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Image fondoCuerpo = cuerpo.gameObject.AddComponent<Image>();
        fondoCuerpo.color = new Color(0.08f, 0.08f, 0.12f, 0.92f);
        fondoCuerpo.raycastTarget = false;
        cuerpo.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(10, 10, 6, 6);

        bool hayRelaciones = false;
        foreach (string atacante in facciones)
        {
            var fuerte = new List<string>();
            var debil = new List<string>();

            foreach (string defensora in facciones)
            {
                if (string.Equals(atacante, defensora, System.StringComparison.OrdinalIgnoreCase)) continue;

                // Lo que mi facción hace a la otra, y lo que la otra me hace a mí
                float yoAtaco = ExcelManager.Instance.ObtenerMultiplicadorFaccion(atacante, defensora);
                float meAtacan = ExcelManager.Instance.ObtenerMultiplicadorFaccion(defensora, atacante);
                var ci = System.Globalization.CultureInfo.InvariantCulture;

                if (yoAtaco > 1.001f)
                {
                    fuerte.Add($"{defensora} (x{yoAtaco.ToString("0.##", ci)})");
                    hayRelaciones = true;
                }

                if (meAtacan > 1.001f)
                {
                    debil.Add($"{defensora} (x{meAtacan.ToString("0.##", ci)})");
                    hayRelaciones = true;
                }
                else if (yoAtaco < 0.999f)
                {
                    debil.Add($"{defensora} (x{yoAtaco.ToString("0.##", ci)})");
                    hayRelaciones = true;
                }
            }

            string txtFuerte = fuerte.Count > 0 ? string.Join(", ", fuerte) : "-";
            string txtDebil = debil.Count > 0 ? string.Join(", ", debil) : "-";

            Text linea = CrearTextoUI(cuerpo, $"<b>{atacante}</b>   <color=#7CFF7C>Fuerte vs:</color> {txtFuerte}   <color=#FF8080>Débil ante:</color> {txtDebil}", 14, TextAnchor.MiddleLeft);
            LayoutElement le = linea.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 24f;
        }

        if (!hayRelaciones)
        {
            Text aviso = CrearTextoUI(cuerpo, "No se han cargado relaciones entre facciones. Revisa la tabla FACCIONES del Excel.", 14, TextAnchor.MiddleLeft);
            aviso.gameObject.AddComponent<LayoutElement>().minHeight = 24f;
        }
    }

    void CrearTarjetaArrastrable(Transform padre, ExcelManager.UnitData data, int idJugador)
    {
        GameObject card = new GameObject($"Card_{data.nombre}_J{idJugador}");
        card.transform.SetParent(padre, false);

        Image bg = card.AddComponent<Image>();
        bg.color = idJugador == 1 ? new Color(0.2f, 0.4f, 0.3f) : new Color(0.45f, 0.2f, 0.2f);

        UnitDragItem drag = card.AddComponent<UnitDragItem>();
        drag.nombreUnidadKey = data.nombre.ToLowerInvariant();
        drag.idJugadorPropietario = idJugador;

        LayoutElement le = card.AddComponent<LayoutElement>();
        le.preferredHeight = 30f;

        // Solo nombre (izquierda) y nivel de poder base (derecha)
        Text txtNombre = CrearTextoUI(card.transform, data.nombre, 15, TextAnchor.MiddleLeft);
        RectTransform rtNombre = txtNombre.GetComponent<RectTransform>();
        SetFullStretch(rtNombre);
        rtNombre.offsetMin = new Vector2(10, 0);
        rtNombre.offsetMax = new Vector2(-85, 0);

        float poderMostrado = data.poderCombate > 0f ? data.poderCombate : CalcularNivelPoder(data);
        Text txtPoder = CrearTextoUI(card.transform, $"Poder {poderMostrado:0.##}", 13, TextAnchor.MiddleRight);
        txtPoder.color = new Color(1f, 0.9f, 0.5f);
        RectTransform rtPoder = txtPoder.GetComponent<RectTransform>();
        SetFullStretch(rtPoder);
        rtPoder.offsetMin = new Vector2(10, 0);
        rtPoder.offsetMax = new Vector2(-8, 0);
    }

    Text CrearTextoUI(Transform parent, string contenido, int fontSize, TextAnchor align)
    {
        GameObject go = new GameObject("Texto");
        go.transform.SetParent(parent, false);
        Text t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (t.font == null) t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        t.text = contenido;
        t.fontSize = fontSize;
        t.alignment = align;
        t.color = Color.white;
        t.raycastTarget = false;
        return t;
    }

    void SetFullStretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    void ActualizarTextoEstado(string msg)
    {
        if (textoEstadoSuperior != null) textoEstadoSuperior.text = msg;
        Debug.Log(msg);
    }
}
