using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
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
    [Tooltip("Las unidades con alcance <= a este valor atacan cuerpo a cuerpo; el resto dispara proyectiles.")]
    public float alcanceMaxCuerpoACuerpo = 1.5f;
    [Tooltip("Solo se atacan enemigos que estén dentro de la arena (más el margen).")]
    public bool limitarCombateAlArea = true;
    public float margenArea = 1.5f;

    public int MascaraCasillas => layout != null ? layout.capaCasillas.value : ~0;

    private List<UnitStats> unidadesEnTablero = new List<UnitStats>();
    private List<GridTile> casillas = new List<GridTile>();
    private int equipoGanador = 0;
    private float tiempoInicioCombate;
    private float duracionCombate;

    // Composición del último combate, para poder repetirlo tras recargar la escena
    private struct Colocacion { public string unidad; public int jugador, fila, columna; }
    private static List<Colocacion> composicionGuardada = new List<Colocacion>();
    private static bool repetirAlCargar;

    private static readonly CultureInfo es = new CultureInfo("es-ES");

    // Referencias de UI generada automáticamente
    private Text textoEstadoSuperior;
    private Button botonIniciarCombate;
    private Text textoPrediccion;
    private Text textoEquipoJ1;
    private Text textoEquipoJ2;
    private Text textoResultado;
    private GameObject botonesFinales;
    private float temporizadorUI;

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
        CrearInterfaz();

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
            ComprobarLlegadaAlLadoEnemigo();

        temporizadorUI -= Time.deltaTime;
        if (temporizadorUI <= 0f)
        {
            temporizadorUI = 0.15f;
            ActualizarPanelCombate();
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
                casillas.Add(existentes[i]);
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
                casillas.Add(tile);
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
    // MODELO DE CADA UNIDAD (cápsula + accesorios según su nombre en el Excel)
    // Las medidas están en unidades locales de la cápsula: radio 0.5, altura 2
    // =========================================================
    private static Mesh mallaCono;

    static bool EsUnidad(string nombre, params string[] claves)
    {
        if (string.IsNullOrEmpty(nombre)) return false;
        string n = nombre.ToLowerInvariant();
        foreach (string c in claves)
            if (n.Contains(c)) return true;
        return false;
    }

    static float EscalaUnidad(string nombre)
    {
        if (EsUnidad(nombre, "ogro")) return 0.55f * 1.5f;
        if (EsUnidad(nombre, "palad")) return 0.55f * 1.15f;
        return 0.55f;
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

    void AnadirAccesorios(GameObject unidad, string nombre, int idJugador)
    {
        Transform t = unidad.transform;
        Color equipo = CombatVFX.ColorEquipo(idJugador);
        Color metal = new Color(0.8f, 0.8f, 0.85f);
        Color oro = new Color(1f, 0.8f, 0.25f);
        Color madera = new Color(0.4f, 0.25f, 0.12f);

        if (EsUnidad(nombre, "caballero"))
        {
            Espada(t, metal, 1f);
            Escudo(t, equipo);
        }
        else if (EsUnidad(nombre, "palad"))
        {
            Espada(t, oro, 1.25f);
            Escudo(t, oro);
            Aureola(t, oro);
        }
        else if (EsUnidad(nombre, "mago"))
        {
            CrearAccesorio(t, PrimitiveType.Cube, "Sombrero", new Vector3(0f, 0.8f, 0f), Vector3.zero, Vector3.one, equipo, ObtenerMallaCono());
            Baston(t, madera);
        }
        else if (EsUnidad(nombre, "sacerdote"))
        {
            Baston(t, Color.white);
            Aureola(t, oro);
        }
        else if (EsUnidad(nombre, "cultista"))
        {
            CrearAccesorio(t, PrimitiveType.Cube, "Capucha", new Vector3(0f, 0.55f, 0f), Vector3.zero, new Vector3(1.1f, 0.95f, 1.1f),
                new Color(0.3f, 0.05f, 0.35f), ObtenerMallaCono());
            Espada(t, new Color(0.5f, 0.1f, 0.1f), 0.6f); // Daga
        }
        else if (EsUnidad(nombre, "mosquet"))
        {
            CrearAccesorio(t, PrimitiveType.Cube, "Rifle", new Vector3(0.45f, 0.1f, 0.55f), Vector3.zero,
                new Vector3(0.12f, 0.14f, 1.3f), madera);
        }
        else if (EsUnidad(nombre, "arquero"))
        {
            CrearAccesorio(t, PrimitiveType.Cube, "Arco", new Vector3(-0.5f, 0.1f, 0.45f), Vector3.zero,
                new Vector3(0.08f, 1.2f, 0.1f), madera);
        }
        else if (EsUnidad(nombre, "ogro"))
        {
            Color piel = new Color(0.45f, 0.6f, 0.3f);
            CrearAccesorio(t, PrimitiveType.Cube, "Mano_Izq", new Vector3(-0.65f, 0f, 0.35f), Vector3.zero, Vector3.one * 0.35f, piel);
            CrearAccesorio(t, PrimitiveType.Cube, "Mano_Der", new Vector3(0.65f, 0f, 0.35f), Vector3.zero, Vector3.one * 0.35f, piel);
        }
        else
        {
            // Tropa Base y cualquier unidad nueva del Excel: espada sencilla
            Espada(t, metal, 0.8f);
        }

        // Aro del color del equipo en los pies para distinguir los bandos de un vistazo
        CrearAccesorio(t, PrimitiveType.Cylinder, "Aro_Equipo", new Vector3(0f, -0.98f, 0f), Vector3.zero,
            new Vector3(1.5f, 0.02f, 1.5f), equipo);
    }

    void Espada(Transform t, Color color, float tamano)
    {
        CrearAccesorio(t, PrimitiveType.Cylinder, "Espada", new Vector3(0.65f, 0.15f, 0.3f), new Vector3(35f, 0f, 0f),
            new Vector3(0.12f, 0.6f * tamano, 0.12f), color);
    }

    void Escudo(Transform t, Color color)
    {
        CrearAccesorio(t, PrimitiveType.Cube, "Escudo", new Vector3(-0.6f, 0f, 0.3f), Vector3.zero, new Vector3(0.12f, 0.75f, 0.6f), color);
    }

    void Baston(Transform t, Color color)
    {
        CrearAccesorio(t, PrimitiveType.Cylinder, "Baston", new Vector3(0.6f, 0.1f, 0.25f), Vector3.zero, new Vector3(0.08f, 0.9f, 0.08f), color);
    }

    void Aureola(Transform t, Color color)
    {
        CrearAccesorio(t, PrimitiveType.Cylinder, "Aureola", new Vector3(0f, 1.3f, 0f), Vector3.zero, new Vector3(0.6f, 0.02f, 0.6f), color);
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
    // 2. COLOCACIÓN DE UNIDADES POR DRAG & DROP
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

        if (ContarUnidadesVivas(idJugador) >= maxUnidadesPorEquipo)
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
        float escala = EscalaUnidad(datosUnidad.nombre);
        nuevaUnidad.transform.position = casilla.transform.position + Vector3.up * (alturaUnidadSobreCasilla + (escala - 0.55f));
        nuevaUnidad.transform.localScale = new Vector3(escala, escala, escala);
        nuevaUnidad.transform.rotation = RotacionMirandoAlRival(idJugador);

        // El NavMeshAgent se encarga de evitar a otras unidades; el collider solo sirve de referencia
        Collider col = nuevaUnidad.GetComponent<Collider>();
        if (col != null) col.isTrigger = true;

        Renderer rend = nuevaUnidad.GetComponent<Renderer>();
        if (rend != null)
            rend.material.color = (idJugador == 1) ? Color.white : new Color(0.08f, 0.08f, 0.08f);

        AnadirAccesorios(nuevaUnidad, datosUnidad.nombre, idJugador);
        nuevaUnidad.AddComponent<UnitAttackAnimator>(); // Debe ir después de crear los accesorios

        UnitStats stats = nuevaUnidad.AddComponent<UnitStats>();
        stats.InicializarDesdeDatos(datosUnidad, idJugador);
        stats.casillaAsignada = casilla;

        nuevaUnidad.AddComponent<UnitHealthBar>(); // Después de UnitStats, que es de donde lee la vida
        nuevaUnidad.AddComponent<UnitCombatAI>();

        casilla.unidadEnCasilla = stats;
        unidadesEnTablero.Add(stats);

        ActualizarTextoEstado($"Unidad colocada: {datosUnidad.nombre} (J{idJugador}) | Aliadas: {ContarUnidadesVivas(1)}/{maxUnidadesPorEquipo} - Enemigas: {ContarUnidadesVivas(2)}/{maxUnidadesPorEquipo}");
        ActualizarPanelCombate();
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

        GuardarComposicion();

        EstadoActual = EstadoPartida.Combate;
        tiempoInicioCombate = Time.time;
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
        unidadMuerta.segundoMuerte = Time.time - tiempoInicioCombate;
        unidadMuerta.gameObject.SetActive(false);

        // Un proyectil disparado antes de acabar puede llegar durante la marcha: ya no cambia el resultado
        if (EstadoActual != EstadoPartida.Combate) return;

        int vivasJ1 = ContarUnidadesVivas(1);
        int vivasJ2 = ContarUnidadesVivas(2);

        if (vivasJ1 > 0 && vivasJ2 > 0) return;

        duracionCombate = Time.time - tiempoInicioCombate;
        equipoGanador = vivasJ1 > 0 ? 1 : vivasJ2 > 0 ? 2 : 0;
        MostrarResultado();

        if (equipoGanador == 0)
        {
            EstadoActual = EstadoPartida.Finalizado;
            ActualizarTextoEstado("¡EMPATE! Los dos bandos han caído.");
        }
        else
        {
            IniciarMarchaVictoria(equipoGanador);
        }
    }

    void IniciarMarchaVictoria(int ganador)
    {
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
        foreach (UnitStats u in unidadesEnTablero)
        {
            if (u != null && u.EstaVivo && u.idJugador == equipoGanador)
            {
                UnitCombatAI ai = u.GetComponent<UnitCombatAI>();
                if (ai != null && !ai.HaCruzadoPuenteEnemigo) return;
            }
        }

        EstadoActual = EstadoPartida.Finalizado;
        string nombreBando = equipoGanador == 1 ? "JUGADOR 1 (BASE ALIADA)" : "JUGADOR 2 (BASE ENEMIGA)";
        ActualizarTextoEstado($"🏆 ¡VICTORIA DEL {nombreBando}!");
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

    // ---------------------------------------------------------
    // Repetir el mismo combate / empezar uno nuevo
    // ---------------------------------------------------------
    void GuardarComposicion()
    {
        composicionGuardada.Clear();
        foreach (UnitStats u in unidadesEnTablero)
        {
            if (u == null || u.casillaAsignada == null) continue;
            composicionGuardada.Add(new Colocacion
            {
                unidad = u.nombreUnidad.ToLowerInvariant(),
                jugador = u.idJugador,
                fila = u.casillaAsignada.fila,
                columna = u.casillaAsignada.columna
            });
        }
    }

    public void RepetirCombate()
    {
        repetirAlCargar = true;
        RecargarEscena();
    }

    public void NuevaPrueba()
    {
        repetirAlCargar = false;
        RecargarEscena();
    }

    static void RecargarEscena()
    {
        Scene escena = SceneManager.GetActiveScene();
#if UNITY_EDITOR
        // En el editor funciona aunque la escena no esté añadida a Build Settings
        UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(escena.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
        SceneManager.LoadScene(escena.buildIndex);
#endif
    }

    void RecolocarComposicionGuardada()
    {
        repetirAlCargar = false;
        foreach (Colocacion c in composicionGuardada)
        {
            GridTile tile = casillas.Find(t => t.idPropietarioCasilla == c.jugador && t.fila == c.fila && t.columna == c.columna);
            if (tile != null) IntentarColocarUnidadPorDrag(tile, c.unidad, c.jugador);
        }
        ActualizarTextoEstado("Combate anterior recolocado. Pulsa PELEAR para repetirlo.");
    }

    // =========================================================
    // 4. PREDICCIÓN DEL EXCEL vs RESULTADO REAL
    // =========================================================
    struct ResumenEquipo
    {
        public int unidades, vivas;
        public float poder, dps, vidaMax, vidaActual, danoHecho;
    }

    ResumenEquipo Resumir(int idJugador)
    {
        ResumenEquipo r = new ResumenEquipo();
        foreach (UnitStats u in unidadesEnTablero)
        {
            if (u == null || u.idJugador != idJugador) continue;
            r.unidades++;
            if (u.EstaVivo) r.vivas++;
            r.poder += u.poder;
            r.dps += u.DanoPorSegundo;
            r.vidaMax += u.vidaMaxima;
            r.vidaActual += Mathf.Max(0f, u.vidaActual);
            r.danoHecho += u.danoInfligido;
        }
        return r;
    }

    /// <summary>Predicción del Excel: gana el bando con más Poder Combate sumado.</summary>
    int GanadorPredicho(out ResumenEquipo j1, out ResumenEquipo j2)
    {
        j1 = Resumir(1);
        j2 = Resumir(2);
        if (Mathf.Approximately(j1.poder, j2.poder)) return 0;
        return j1.poder > j2.poder ? 1 : 2;
    }

    static string N(float v, string formato = "#,0.##") => v.ToString(formato, es);

    void ActualizarPanelCombate()
    {
        if (textoPrediccion == null) return;

        int predicho = GanadorPredicho(out var j1, out var j2);
        string txtPred = predicho == 0 ? "EMPATE" : $"GANA J{predicho}";
        textoPrediccion.text =
            $"<b>PREDICCIÓN DEL EXCEL</b>  (Poder = Daño × Vida × Alcance / Cadencia, sumado por equipo)\n" +
            $"<color=#59BFFF>J1: {N(j1.poder)}</color>   vs   <color=#FF5A4D>J2: {N(j2.poder)}</color>   →   <b>{txtPred}</b>";

        textoEquipoJ1.text = ListadoEquipo(1, j1);
        textoEquipoJ2.text = ListadoEquipo(2, j2);
    }

    string ListadoEquipo(int idJugador, ResumenEquipo r)
    {
        StringBuilder sb = new StringBuilder();
        string color = idJugador == 1 ? "#59BFFF" : "#FF5A4D";
        sb.AppendLine($"<color={color}><b>J{idJugador}</b></color>  vivas {r.vivas}/{r.unidades} · PV {N(r.vidaActual, "0")}/{N(r.vidaMax, "0")} · DPS {N(r.dps, "0.#")} · daño hecho {N(r.danoHecho, "0")}");

        foreach (UnitStats u in unidadesEnTablero)
        {
            if (u == null || u.idJugador != idJugador) continue;

            string estado = u.EstaVivo
                ? $"PV {N(Mathf.Max(0f, u.vidaActual), "0")}/{N(u.vidaMaxima, "0")}"
                : $"<color=#888888>muerto ({N(u.segundoMuerte, "0.0")} s)</color>";

            sb.AppendLine($"<b>{u.nombreUnidad}</b>  Dñ {N(u.danoAtaque)} · Cad {N(u.cadencia)}s · Alc {N(u.alcance)}  |  {estado}  |  hecho {N(u.danoInfligido, "0")}");
        }

        return sb.ToString();
    }

    void MostrarResultado()
    {
        int predicho = GanadorPredicho(out var j1, out var j2);
        bool acierto = predicho == equipoGanador;
        ResumenEquipo g = equipoGanador == 1 ? j1 : j2;
        float vidaRestante = equipoGanador == 0 || g.vidaMax <= 0f ? 0f : g.vidaActual / g.vidaMax;

        string ganador = equipoGanador == 0 ? "EMPATE" : $"GANA J{equipoGanador}";
        string veredicto = acierto
            ? "<color=#7CFF7C>EL EXCEL ACIERTA</color>"
            : "<color=#FF8080>EL EXCEL FALLA</color>";

        textoResultado.text =
            $"<b>RESULTADO REAL: {ganador}</b> en {N(duracionCombate, "0.0")} s · vida restante del ganador {N(vidaRestante * 100f, "0")}%   {veredicto}\n" +
            $"<size=13>Daño hecho  J1: {N(j1.danoHecho, "0")}  ·  J2: {N(j2.danoHecho, "0")}   (guardado en resultados_combate.csv)</size>";
        textoResultado.transform.parent.gameObject.SetActive(true);
        botonesFinales.SetActive(true);

        GuardarResultadoCSV(j1, j2, predicho, vidaRestante);
    }

    string Composicion(int idJugador)
    {
        var cuenta = new SortedDictionary<string, int>();
        foreach (UnitStats u in unidadesEnTablero)
        {
            if (u == null || u.idJugador != idJugador) continue;
            cuenta.TryGetValue(u.nombreUnidad, out int n);
            cuenta[u.nombreUnidad] = n + 1;
        }

        var partes = new List<string>();
        foreach (var kv in cuenta) partes.Add($"{kv.Value}x {kv.Key}");
        return string.Join(" + ", partes);
    }

    /// <summary>Añade una fila por combate (separada por ';' para abrirla directamente en Excel en español).</summary>
    void GuardarResultadoCSV(ResumenEquipo j1, ResumenEquipo j2, int predicho, float vidaRestante)
    {
        string ruta = Path.Combine(Application.persistentDataPath, "resultados_combate.csv");
        try
        {
            bool nuevo = !File.Exists(ruta);
            using (StreamWriter w = new StreamWriter(ruta, true, new UTF8Encoding(true)))
            {
                if (nuevo)
                    w.WriteLine("Fecha;Equipo J1;Poder J1;Equipo J2;Poder J2;Predicción Excel;Ganador real;Acierto;Duración (s);Vida restante ganador (%);Daño hecho J1;Daño hecho J2");

                string pred = predicho == 0 ? "Empate" : $"J{predicho}";
                string real = equipoGanador == 0 ? "Empate" : $"J{equipoGanador}";
                w.WriteLine(string.Join(";",
                    System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Composicion(1), N(j1.poder, "0.##"),
                    Composicion(2), N(j2.poder, "0.##"),
                    pred, real, predicho == equipoGanador ? "SÍ" : "NO",
                    N(duracionCombate, "0.0"), N(vidaRestante * 100f, "0"),
                    N(j1.danoHecho, "0"), N(j2.danoHecho, "0")));
            }
            Debug.Log($"[BattleManager] Resultado guardado en {ruta}");
        }
        catch (IOException e)
        {
            Debug.LogWarning($"[BattleManager] No se pudo guardar el resultado ({e.Message}). ¿Tienes el CSV abierto en Excel?");
        }
    }

    // =========================================================
    // 5. INTERFAZ GENERADA POR CÓDIGO
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

    void CrearInterfaz()
    {
        GameObject canvasObj = new GameObject("Canvas_Prototipo");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasObj.AddComponent<GraphicRaycaster>();
        Transform raiz = canvasObj.transform;

        // Paneles laterales con las unidades del Excel
        contenedorListaJ1 = CrearPanelLateral(raiz, "Panel_Aliado_J1",
            new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(10, 0),
            "UNIDADES BASE ALIADA (J1)\n(Arrastra a tu Grid)", new Color(0.1f, 0.2f, 0.3f, 0.9f));

        contenedorListaJ2 = CrearPanelLateral(raiz, "Panel_Enemigo_J2",
            new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(-10, 0),
            "UNIDADES BASE ENEMIGA (J2)\n(Arrastra a su Grid)", new Color(0.3f, 0.12f, 0.12f, 0.9f));

        // Barra superior: estado y botón de pelear
        RectTransform rtTop = CrearPanel(raiz, "Panel_Superior", new Color(0.12f, 0.12f, 0.12f, 0.9f),
            new Vector2(0.5f, 1f), new Vector2(900, 110), new Vector2(0, -10));

        textoEstadoSuperior = CrearTextoUI(rtTop, "Cargando estadísticas desde Google Sheets...", 20, TextAnchor.UpperCenter);
        RectTransform rtTxt = textoEstadoSuperior.GetComponent<RectTransform>();
        rtTxt.anchorMin = new Vector2(0, 0.45f);
        rtTxt.anchorMax = new Vector2(1, 1f);
        rtTxt.offsetMin = new Vector2(10, 0);
        rtTxt.offsetMax = new Vector2(-10, -5);

        botonIniciarCombate = CrearBoton(rtTop, "⚔️ PELEAR", new Color(0.2f, 0.7f, 0.3f), new Vector2(0, 8), IniciarCombate);

        // Predicción del Excel (debajo de la barra superior)
        RectTransform rtPred = CrearPanel(raiz, "Panel_Prediccion", new Color(0.1f, 0.1f, 0.18f, 0.92f),
            new Vector2(0.5f, 1f), new Vector2(900, 60), new Vector2(0, -125));
        textoPrediccion = CrearTextoUI(rtPred, "", 16, TextAnchor.MiddleCenter);
        SetFullStretch(textoPrediccion.rectTransform);

        // Resultado real (oculto hasta que acaba el combate) con botones para repetir o empezar otra prueba
        RectTransform rtRes = CrearPanel(raiz, "Panel_Resultado", new Color(0.08f, 0.08f, 0.08f, 0.95f),
            new Vector2(0.5f, 1f), new Vector2(900, 110), new Vector2(0, -190));
        textoResultado = CrearTextoUI(rtRes, "", 17, TextAnchor.UpperCenter);
        RectTransform rtTxtRes = textoResultado.rectTransform;
        rtTxtRes.anchorMin = new Vector2(0, 0.42f);
        rtTxtRes.anchorMax = Vector2.one;
        rtTxtRes.offsetMin = new Vector2(10, 0);
        rtTxtRes.offsetMax = new Vector2(-10, -6);

        botonesFinales = new GameObject("Botones_Finales", typeof(RectTransform));
        botonesFinales.transform.SetParent(rtRes, false);
        SetFullStretch((RectTransform)botonesFinales.transform);
        CrearBoton(botonesFinales.transform, "REPETIR COMBATE", new Color(0.25f, 0.45f, 0.8f), new Vector2(-150, 8), RepetirCombate);
        CrearBoton(botonesFinales.transform, "NUEVA PRUEBA", new Color(0.5f, 0.5f, 0.5f), new Vector2(150, 8), NuevaPrueba);
        rtRes.gameObject.SetActive(false);

        // Información en vivo de cada unidad (parte inferior)
        RectTransform rtVivo = CrearPanel(raiz, "Panel_Combate_EnVivo", new Color(0.05f, 0.05f, 0.05f, 0.85f),
            new Vector2(0.5f, 0f), new Vector2(1260, 250), new Vector2(0, 10));
        textoEquipoJ1 = CrearTextoUI(rtVivo, "", 14, TextAnchor.UpperLeft);
        textoEquipoJ2 = CrearTextoUI(rtVivo, "", 14, TextAnchor.UpperLeft);
        RectTransform r1 = textoEquipoJ1.rectTransform, r2 = textoEquipoJ2.rectTransform;
        r1.anchorMin = new Vector2(0f, 0f); r1.anchorMax = new Vector2(0.5f, 1f);
        r2.anchorMin = new Vector2(0.5f, 0f); r2.anchorMax = new Vector2(1f, 1f);
        r1.offsetMin = r2.offsetMin = new Vector2(10, 6);
        r1.offsetMax = r2.offsetMax = new Vector2(-10, -6);
    }

    RectTransform CrearPanel(Transform padre, string nombre, Color color, Vector2 ancla, Vector2 tamano, Vector2 pos)
    {
        GameObject panel = new GameObject(nombre);
        panel.transform.SetParent(padre, false);
        Image img = panel.AddComponent<Image>();
        img.color = color;

        RectTransform rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = ancla;
        rt.anchorMax = ancla;
        rt.pivot = ancla;
        rt.sizeDelta = tamano;
        rt.anchoredPosition = pos;
        return rt;
    }

    Button CrearBoton(Transform padre, string texto, Color color, Vector2 pos, UnityEngine.Events.UnityAction accion)
    {
        GameObject btnObj = new GameObject("Boton_" + texto);
        btnObj.transform.SetParent(padre, false);
        Image img = btnObj.AddComponent<Image>();
        img.color = color;
        Button btn = btnObj.AddComponent<Button>();
        btn.onClick.AddListener(accion);

        RectTransform rt = btnObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(280, 42);
        rt.anchoredPosition = pos;

        Text txt = CrearTextoUI(btnObj.transform, texto, 20, TextAnchor.MiddleCenter);
        SetFullStretch(txt.rectTransform);
        return btn;
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

        // Zona con scroll para la lista de unidades
        GameObject scrollObj = new GameObject("Scroll_Unidades");
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

        GameObject contenedor = new GameObject("Contenedor_Unidades");
        contenedor.transform.SetParent(scrollObj.transform, false);
        RectTransform rtCont = contenedor.AddComponent<RectTransform>();
        rtCont.anchorMin = new Vector2(0, 1);
        rtCont.anchorMax = new Vector2(1, 1);
        rtCont.pivot = new Vector2(0.5f, 1);
        rtCont.sizeDelta = Vector2.zero;
        rtCont.anchoredPosition = Vector2.zero;

        VerticalLayoutGroup vlg = contenedor.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 4f;
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
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
        ActualizarTextoEstado($"Arrastra hasta {maxUnidadesPorEquipo} unidades desde ambos paneles hacia cada cuadrícula y pulsa PELEAR.");

        LimpiarHijos(contenedorListaJ1);
        LimpiarHijos(contenedorListaJ2);

        foreach (var u in ExcelManager.Instance.ListaUnidades)
        {
            CrearTarjetaArrastrable(contenedorListaJ1, u, 1);
            CrearTarjetaArrastrable(contenedorListaJ2, u, 2);
        }

        if (repetirAlCargar) RecolocarComposicionGuardada();
        ActualizarPanelCombate();
    }

    void LimpiarHijos(Transform padre)
    {
        if (padre == null) return;
        foreach (Transform hijo in padre) Destroy(hijo.gameObject);
    }

    void CrearTarjetaArrastrable(Transform padre, ExcelManager.UnitData data, int idJugador)
    {
        GameObject card = new GameObject($"Card_{data.nombre}_J{idJugador}");
        card.transform.SetParent(padre, false);

        Image bg = card.AddComponent<Image>();
        bg.color = idJugador == 1 ? new Color(0.18f, 0.32f, 0.45f) : new Color(0.45f, 0.2f, 0.2f);

        UnitDragItem drag = card.AddComponent<UnitDragItem>();
        drag.nombreUnidadKey = data.nombre.ToLowerInvariant();
        drag.idJugadorPropietario = idJugador;

        LayoutElement le = card.AddComponent<LayoutElement>();
        le.preferredHeight = 50f;

        // Línea 1: nombre y Poder Combate del Excel
        Text txtNombre = CrearTextoUI(card.transform, $"<b>{data.nombre}</b>", 16, TextAnchor.UpperLeft);
        RectTransform rtNombre = txtNombre.rectTransform;
        SetFullStretch(rtNombre);
        rtNombre.offsetMin = new Vector2(10, 0);
        rtNombre.offsetMax = new Vector2(-8, -4);

        Text txtPoder = CrearTextoUI(card.transform, $"Poder {N(data.Poder)}", 14, TextAnchor.UpperRight);
        txtPoder.color = new Color(1f, 0.9f, 0.5f);
        RectTransform rtPoder = txtPoder.rectTransform;
        SetFullStretch(rtPoder);
        rtPoder.offsetMin = new Vector2(10, 0);
        rtPoder.offsetMax = new Vector2(-8, -4);

        // Línea 2: parámetros que usa el combate
        Text txtStats = CrearTextoUI(card.transform,
            $"Daño {N(data.danoAtaque)} · Vida {N(data.vida)} · Cad {N(data.cadencia)}s · Alc {N(data.alcance)} · DPS {N(data.DanoPorSegundo, "0.#")}",
            12, TextAnchor.LowerLeft);
        txtStats.color = new Color(0.85f, 0.85f, 0.85f);
        RectTransform rtStats = txtStats.rectTransform;
        SetFullStretch(rtStats);
        rtStats.offsetMin = new Vector2(10, 4);
        rtStats.offsetMax = new Vector2(-8, 0);
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
        t.supportRichText = true;
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
