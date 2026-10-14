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

    [Header("Escala del Tablero (En Metros de Unity)")]
    public float tamanoCasilla = 1.2f;       // Cada celda mide 1.2m x 1.2m
    public float distanciaCentroBases = 3.8f; // Distancia desde el centro del puente (0,0,0) al inicio de cada Grid
    public int maxUnidadesPorEquipo = 9;

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
        AsegurarEventSystem();
        GenerarEscenarioYGrids();
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
    // 1. GENERACIÓN DEL MAPA: BASES 3x3 Y PUENTE CENTRAL
    // =========================================================
    void GenerarEscenarioYGrids()
    {
        // Puente central en (0, 0, 0) que une ambas bases
        GameObject puente = GameObject.CreatePrimitive(PrimitiveType.Cube);
        puente.name = "Puente_Central";
        puente.transform.position = new Vector3(0f, -0.05f, 0f);
        puente.transform.localScale = new Vector3(3.6f, 0.2f, 2.2f);
        puente.GetComponent<Renderer>().material.color = new Color(0.75f, 0.65f, 0.45f);

        // Suelo Base Aliada (Izquierda)
        CrearPlataformaBase("Plataforma_Base_Aliada", new Vector3(-4.2f, -0.15f, 0f), new Color(0.85f, 0.95f, 0.85f));
        // Suelo Base Enemiga (Derecha)
        CrearPlataformaBase("Plataforma_Base_Enemiga", new Vector3(4.2f, -0.15f, 0f), new Color(0.95f, 0.85f, 0.85f));

        // Grid 3x3 Base Aliada (Jugador 1 - Izquierda)
        Vector3 origenJ1 = new Vector3(-distanciaCentroBases - (2 * tamanoCasilla), 0f, -tamanoCasilla);
        CrearGrid3x3(1, origenJ1, new Color(0.25f, 0.55f, 0.35f));

        // Grid 3x3 Base Enemiga (Jugador 2 - Derecha)
        Vector3 origenJ2 = new Vector3(distanciaCentroBases, 0f, -tamanoCasilla);
        CrearGrid3x3(2, origenJ2, new Color(0.65f, 0.25f, 0.25f));
    }

    void CrearPlataformaBase(string nombre, Vector3 pos, Color color)
    {
        GameObject plat = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plat.name = nombre;
        Destroy(plat.GetComponent<Collider>());
        plat.transform.position = pos;
        plat.transform.localScale = new Vector3(4.8f, 0.1f, 5.5f);
        plat.GetComponent<Renderer>().material.color = color;
    }

    void CrearGrid3x3(int idJugador, Vector3 origen, Color colorCasilla)
    {
        GameObject contenedor = new GameObject(idJugador == 1 ? "Grid_Base_Aliada" : "Grid_Base_Enemiga");
        for (int fila = 0; fila < 3; fila++)
        {
            for (int col = 0; col < 3; col++)
            {
                GameObject tileObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tileObj.name = $"Celda_J{idJugador}_{fila}_{col}";
                tileObj.transform.SetParent(contenedor.transform);
                tileObj.transform.position = origen + new Vector3(col * tamanoCasilla, 0f, fila * tamanoCasilla);
                tileObj.transform.localScale = new Vector3(tamanoCasilla * 0.92f, 0.2f, tamanoCasilla * 0.92f);

                GridTile tile = tileObj.AddComponent<GridTile>();
                tile.InicializarCasilla(idJugador, fila, col, colorCasilla);
            }
        }
    }

    public Vector3 ObtenerEntradaPuente(int idJugador)
    {
        // Punto de entrada al puente de cada bando
        return idJugador == 1 ? new Vector3(-1.6f, 0.5f, 0f) : new Vector3(1.6f, 0.5f, 0f);
    }

    public Vector3 ObtenerMetaVictoriaLadoEnemigo(int idJugadorGanador)
    {
        // Al ganar, las unidades cruzan el puente hasta el lado del enemigo
        return idJugadorGanador == 1 ? new Vector3(2.4f, 0.5f, 0f) : new Vector3(-2.4f, 0.5f, 0f);
    }

    // =========================================================
    // 2. COLOCACIÓN DE UNIDADES POR DRAG & DROP (MÁX 9 POR BANDO)
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
        nuevaUnidad.transform.position = casilla.transform.position + Vector3.up * 0.65f;
        nuevaUnidad.transform.localScale = new Vector3(0.55f, 0.55f, 0.55f);

        // Desactivar colisión física para que las unidades no se atasquen en el puente
        Collider col = nuevaUnidad.GetComponent<Collider>();
        if (col != null) col.isTrigger = true;

        Renderer rend = nuevaUnidad.GetComponent<Renderer>();
        if (rend != null)
            rend.material.color = (idJugador == 1) ? new Color(0.15f, 0.65f, 1f) : new Color(1f, 0.3f, 0.35f);

        UnitStats stats = nuevaUnidad.AddComponent<UnitStats>();
        stats.InicializarDesdeDatos(datosUnidad, idJugador);
        stats.casillaAsignada = casilla;

        nuevaUnidad.AddComponent<UnitCombatAI>();

        casilla.unidadEnCasilla = stats;
        unidadesEnTablero.Add(stats);

        ActualizarTextoEstado($"Unidad colocada: {datosUnidad.nombre} (J{idJugador}) | Aliadas: {ContarUnidadesVivas(1)}/9 - Enemigas: {ContarUnidadesVivas(2)}/9");
    }

    // =========================================================
    // 3. COMBATE, VICTORIA, MARCHA POR EL PUENTE Y REINICIO
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
        ActualizarTextoEstado("⚔️ ¡COMBATE EN CURSO EN EL PUENTE!");
    }

    public UnitStats BuscarEnemigoMasCercano(UnitStats solicitante)
    {
        UnitStats masCercano = null;
        float menorDistancia = float.MaxValue;

        foreach (UnitStats u in unidadesEnTablero)
        {
            if (u == null || !u.EstaVivo || u.idJugador == solicitante.idJugador) continue;

            float d = Vector3.Distance(solicitante.transform.position, u.transform.position);
            if (d < menorDistancia)
            {
                menorDistancia = d;
                masCercano = u;
            }
        }
        return masCercano;
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
        ActualizarTextoEstado($"¡{nombreBando} HA DERROTADO AL RIVAL! Cruzando el puente...");
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

        // Panel Izquierdo: Selección Base Aliada (Jugador 1)
        contenedorListaJ1 = CrearPanelLateral(canvasObj.transform, "Panel_Aliado_J1",
            new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(10, 0),
            "UNIDADES BASE ALIADA\n(Arrastra a tu Grid 3x3)", new Color(0.1f, 0.25f, 0.15f, 0.9f));

        // Panel Derecho: Selección Base Enemiga (Jugador 2)
        contenedorListaJ2 = CrearPanelLateral(canvasObj.transform, "Panel_Enemigo_J2",
            new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(-10, 0),
            "UNIDADES BASE ENEMIGA\n(Arrastra a su Grid 3x3)", new Color(0.3f, 0.12f, 0.12f, 0.9f));

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

        GameObject contenedor = new GameObject("Contenedor_Tarjetas");
        contenedor.transform.SetParent(panel.transform, false);
        RectTransform rtCont = contenedor.AddComponent<RectTransform>();
        rtCont.anchorMin = new Vector2(0, 0);
        rtCont.anchorMax = new Vector2(1, 1);
        rtCont.offsetMin = new Vector2(8, 8);
        rtCont.offsetMax = new Vector2(-8, -60);

        VerticalLayoutGroup vlg = contenedor.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 4;
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = true;
        vlg.childForceExpandWidth = true;

        return contenedor.transform;
    }

    void PoblarListasUI()
    {
        ActualizarTextoEstado("Arrastra hasta 9 unidades desde ambos paneles hacia cada cuadrícula 3x3 y pulsa PELEAR.");

        foreach (var u in ExcelManager.Instance.ListaUnidades)
        {
            CrearTarjetaArrastrable(contenedorListaJ1, u, 1);
            CrearTarjetaArrastrable(contenedorListaJ2, u, 2);
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

        string info = $"[{data.faccion}] {data.nombre} ({data.rol}) | ATQ:{data.danoAtaque} VIDA:{data.vida} APS:{data.velocidadAtaque} ALC:{data.alcance}m";
        Text txt = CrearTextoUI(card.transform, info, 12, TextAnchor.MiddleLeft);
        RectTransform rtTxt = txt.GetComponent<RectTransform>();
        SetFullStretch(rtTxt);
        rtTxt.offsetMin = new Vector2(6, 2);
        rtTxt.offsetMax = new Vector2(-6, -2);
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