using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(UnitStats))]
public class UnitCombatAI : MonoBehaviour
{
    private UnitStats misStats;
    private UnitStats objetivoActual;
    private NavMeshAgent agente;
    private UnitAttackAnimator animador;

    private float temporizadorAtaque;
    private float temporizadorRepath;
    private float temporizadorBusqueda;
    private float tiempoSinObjetivo;

    private bool navegacionActiva;
    private bool marchando;
    private bool yendoAAgrupacion;
    private Vector3 puntoAgrupacion;

    [Header("Movimiento (NavMeshAgent)")]
    public float velocidadMovimiento = 2.8f;
    public float radioAgente = 0.3f;
    public float alturaAgente = 1.1f;       // Cápsula de 2m * escala 0.55
    public float intervaloRepath = 0.2f;
    [Tooltip("Margen extra sobre el alcance para considerar al objetivo 'a tiro' (radios de las cápsulas).")]
    public float margenAlcance = 0.3f;
    [Tooltip("Si pasan estos segundos sin encontrar rival dentro de la arena, se ignora la restricción de área.")]
    public float esperaAntesDeIgnorarArea = 6f;

    public bool HaCruzadoPuenteEnemigo { get; private set; } = false;

    void Awake()
    {
        misStats = GetComponent<UnitStats>();
        animador = GetComponent<UnitAttackAnimator>();

        agente = GetComponent<NavMeshAgent>();
        if (agente == null) agente = gameObject.AddComponent<NavMeshAgent>();

        // Se queda desactivado hasta que empiece el combate (durante la colocación no hay NavMesh que respetar).
        agente.enabled = false;
        // El tamaño del agente sigue al de la cápsula (los tanques son más grandes)
        float escala = transform.localScale.y;
        alturaAgente = 2f * escala;
        radioAgente = 0.55f * escala;

        agente.speed = velocidadMovimiento;
        agente.radius = radioAgente;
        agente.height = alturaAgente;
        agente.baseOffset = alturaAgente * 0.5f; // El pivote de la cápsula está en su centro
        agente.acceleration = 14f;
        agente.angularSpeed = 540f;
        agente.autoBraking = true;
        agente.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        agente.avoidancePriority = Random.Range(30, 70);
    }

    void OnDisable()
    {
        LiberarObjetivo();
    }

    /// <summary>Lo llama el manager al empezar el combate. Coloca al agente en el NavMesh y lo manda a su punto de la arena.</summary>
    public void IniciarNavegacion(Vector3 puntoDeAgrupacionDeseado)
    {
        if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
        {
            Debug.LogError($"[IA] {name}: no hay NavMesh cerca de su casilla. ¿Has horneado (Bake) el NavMeshSurface de tu escena?");
            return;
        }

        agente.enabled = true;
        agente.Warp(hit.position);

        puntoAgrupacion = puntoDeAgrupacionDeseado;
        navegacionActiva = true;
        marchando = false;
        IrAAgrupacion();
    }

    /// <summary>Lo llama el manager cuando el bando de esta unidad gana.</summary>
    public void IniciarMarcha(Vector3 destino)
    {
        if (!navegacionActiva) return;

        LiberarObjetivo();
        marchando = true;
        HaCruzadoPuenteEnemigo = false;

        agente.isStopped = false;
        agente.stoppingDistance = 0.3f;
        agente.SetDestination(destino);
    }

    void Update()
    {
        if (BattlePrototypeManager.Instance == null || !misStats.EstaVivo || !navegacionActiva) return;

        var estado = BattlePrototypeManager.Instance.EstadoActual;

        if (estado == BattlePrototypeManager.EstadoPartida.Combate)
            ActualizarCombate();
        else if (estado == BattlePrototypeManager.EstadoPartida.MarchaVictoria && marchando)
            ActualizarMarcha();
    }

    void ActualizarCombate()
    {
        if (objetivoActual != null && !objetivoActual.EstaVivo)
            AsignarObjetivo(null);

        // Sin objetivo: buscar uno (con poca frecuencia) o esperar en la arena
        if (objetivoActual == null)
        {
            temporizadorBusqueda -= Time.deltaTime;
            if (temporizadorBusqueda <= 0f)
            {
                temporizadorBusqueda = 0.25f;
                bool ignorarArea = tiempoSinObjetivo > esperaAntesDeIgnorarArea;
                AsignarObjetivo(BattlePrototypeManager.Instance.BuscarObjetivo(misStats, ignorarArea));
            }

            if (objetivoActual == null)
            {
                tiempoSinObjetivo += Time.deltaTime;
                if (!yendoAAgrupacion) IrAAgrupacion();
                return;
            }
        }

        tiempoSinObjetivo = 0f;
        yendoAAgrupacion = false;

        float distancia = DistanciaPlano(transform.position, objetivoActual.transform.position);

        if (distancia <= misStats.alcance + margenAlcance)
        {
            // A tiro: parar y atacar
            if (!agente.isStopped) agente.isStopped = true;
            MirarHacia(objetivoActual.transform.position);

            temporizadorAtaque -= Time.deltaTime;
            if (temporizadorAtaque <= 0f)
            {
                RealizarAtaque(objetivoActual);
                // Cadencia del Excel = segundos entre un ataque y el siguiente
                temporizadorAtaque = Mathf.Max(0.05f, misStats.cadencia);
            }
        }
        else
        {
            // Perseguir usando el NavMesh (rodea obstáculos y cruza el puente solo)
            if (agente.isStopped) agente.isStopped = false;

            temporizadorRepath -= Time.deltaTime;
            if (temporizadorRepath <= 0f)
            {
                temporizadorRepath = intervaloRepath;
                agente.stoppingDistance = Mathf.Max(0.1f, misStats.alcance * 0.85f);
                agente.SetDestination(objetivoActual.transform.position);
            }
        }
    }

    void ActualizarMarcha()
    {
        if (agente.pathPending) return;
        if (agente.remainingDistance <= agente.stoppingDistance + 0.25f)
            HaCruzadoPuenteEnemigo = true;
    }

    void IrAAgrupacion()
    {
        yendoAAgrupacion = true;
        agente.isStopped = false;
        agente.stoppingDistance = 0.3f;
        agente.SetDestination(puntoAgrupacion);
    }

    // ---------------------------------------------------------
    // Objetivos y reparto de atacantes
    // ---------------------------------------------------------
    void AsignarObjetivo(UnitStats nuevo)
    {
        if (nuevo == objetivoActual) return;

        LiberarObjetivo();
        objetivoActual = nuevo;

        if (objetivoActual != null && EsCuerpoACuerpo())
            objetivoActual.atacantesActuales++;

        temporizadorRepath = 0f;
    }

    void LiberarObjetivo()
    {
        if (objetivoActual != null && EsCuerpoACuerpo())
            objetivoActual.atacantesActuales = Mathf.Max(0, objetivoActual.atacantesActuales - 1);
        objetivoActual = null;
    }

    bool EsCuerpoACuerpo()
    {
        return misStats != null && misStats.alcance <= BattlePrototypeManager.Instance.alcanceMaxCuerpoACuerpo;
    }

    // ---------------------------------------------------------
    // Utilidades
    // ---------------------------------------------------------
    static float DistanciaPlano(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    void MirarHacia(Vector3 punto)
    {
        Vector3 d = punto - transform.position;
        d.y = 0f;
        if (d.sqrMagnitude < 0.001f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), 10f * Time.deltaTime);
    }

    void RealizarAtaque(UnitStats enemigo)
    {
        // La animación dura como mucho lo que tarda el siguiente ataque, así que nunca se solapa en unidades rápidas
        if (animador != null)
            animador.Reproducir(Mathf.Min(0.4f, 0.75f * Mathf.Max(0.05f, misStats.cadencia)));

        // Sin facciones ni defensas: cada golpe hace exactamente el "Daño de ataque" del Excel
        float dano = misStats.danoAtaque;
        UnitStats atacante = misStats;
        Color color = CombatVFX.ColorEquipo(misStats.idJugador);

        if (EsCuerpoACuerpo())
        {
            CombatVFX.Impacto(Vector3.Lerp(transform.position, enemigo.transform.position, 0.7f), color);
            enemigo.RecibirDano(dano, atacante);
        }
        else
        {
            Vector3 boca = transform.position + transform.forward * 0.4f + Vector3.up * transform.localScale.y * 0.3f;
            CombatVFX.Disparar(boca, enemigo, color, () =>
            {
                if (enemigo != null && enemigo.EstaVivo) enemigo.RecibirDano(dano, atacante);
            });
        }

        Debug.Log($"[Combate] {misStats.nombreUnidad} (J{misStats.idJugador}) ataca a {enemigo.nombreUnidad} (J{enemigo.idJugador}) por {dano:0.#} de daño.");
    }
}
