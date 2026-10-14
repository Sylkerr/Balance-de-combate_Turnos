using UnityEngine;

[RequireComponent(typeof(UnitStats))]
public class UnitCombatAI : MonoBehaviour
{
    private UnitStats misStats;
    private UnitStats objetivoActual;
    private float temporizadorAtaque;

    [Header("Movimiento")]
    public float velocidadMovimiento = 2.8f;

    public bool HaCruzadoPuenteEnemigo { get; private set; } = false;

    void Awake()
    {
        misStats = GetComponent<UnitStats>();
    }

    void Update()
    {
        if (BattlePrototypeManager.Instance == null || !misStats.EstaVivo) return;

        var estado = BattlePrototypeManager.Instance.EstadoActual;

        if (estado == BattlePrototypeManager.EstadoPartida.Combate)
        {
            ActualizarCombate();
        }
        else if (estado == BattlePrototypeManager.EstadoPartida.MarchaVictoria)
        {
            ActualizarMarchaVictoria();
        }
    }

    void ActualizarCombate()
    {
        if (objetivoActual == null || !objetivoActual.EstaVivo)
        {
            objetivoActual = BattlePrototypeManager.Instance.BuscarEnemigoMasCercano(misStats);
            if (objetivoActual == null) return;
        }

        // Distancia medida directamente en metros de Unity
        float distanciaMetros = Vector3.Distance(transform.position, objetivoActual.transform.position);

        // Pequeño margen de 0.15m para evitar bloqueo entre radios de cápsulas a rango 1m
        if (distanciaMetros <= misStats.alcance + 0.15f)
        {
            temporizadorAtaque -= Time.deltaTime;
            if (temporizadorAtaque <= 0f)
            {
                RealizarAtaque(objetivoActual);
                // VelocidadAtaque = Ataques por segundo -> Tiempo entre ataques = 1 / APS
                float aps = Mathf.Max(0.05f, misStats.velocidadAtaque);
                temporizadorAtaque = 1f / aps;
            }
        }
        else
        {
            // Avanzar hacia el puente primero si la unidad aún está en su orilla y lejos del puente
            Vector3 destino = ObtenerSiguientePuntoDeAvance(objetivoActual.transform.position);
            MoverHacia(destino);
        }
    }

    Vector3 ObtenerSiguientePuntoDeAvance(Vector3 posEnemigo)
    {
        Vector3 entradaPuente = BattlePrototypeManager.Instance.ObtenerEntradaPuente(misStats.idJugador);

        // Si el jugador 1 está a la izquierda de la entrada del puente, se encamina al puente primero
        if (misStats.idJugador == 1 && transform.position.x < entradaPuente.x - 0.2f)
        {
            return new Vector3(entradaPuente.x, transform.position.y, Mathf.Lerp(transform.position.z, entradaPuente.z, 0.7f));
        }
        // Si el jugador 2 está a la derecha de la entrada del puente, se encamina al puente primero
        if (misStats.idJugador == 2 && transform.position.x > entradaPuente.x + 0.2f)
        {
            return new Vector3(entradaPuente.x, transform.position.y, Mathf.Lerp(transform.position.z, entradaPuente.z, 0.7f));
        }

        return posEnemigo;
    }

    void ActualizarMarchaVictoria()
    {
        Vector3 metaEnemiga = BattlePrototypeManager.Instance.ObtenerMetaVictoriaLadoEnemigo(misStats.idJugador);
        Vector3 destinoPlano = new Vector3(metaEnemiga.x, transform.position.y, metaEnemiga.z);

        float distancia = Vector3.Distance(transform.position, destinoPlano);
        if (distancia > 0.35f)
        {
            MoverHacia(destinoPlano);
        }
        else
        {
            HaCruzadoPuenteEnemigo = true;
        }
    }

    void MoverHacia(Vector3 destino)
    {
        Vector3 destinoMismaAltura = new Vector3(destino.x, transform.position.y, destino.z);
        Vector3 direccion = (destinoMismaAltura - transform.position).normalized;
        transform.position += direccion * velocidadMovimiento * Time.deltaTime;
    }

    void RealizarAtaque(UnitStats enemigo)
    {
        float multFaccion = ExcelManager.Instance.ObtenerMultiplicadorFaccion(misStats.faccion, enemigo.faccion);

        bool esMagico = misStats.tipoAtaque.ToLowerInvariant().Contains("mág") || misStats.tipoAtaque.ToLowerInvariant().Contains("mag");
        float defensaObjetivo = esMagico ? enemigo.defensaMagica : enemigo.defensaFisica;
        defensaObjetivo = Mathf.Clamp(defensaObjetivo, 0f, 0.95f);

        float danoFinal = misStats.danoAtaque * multFaccion * (1f - defensaObjetivo);
        enemigo.vidaActual -= danoFinal;

        Debug.Log($"[Combate] {misStats.nombreUnidad} (J{misStats.idJugador}) golpea a {enemigo.nombreUnidad} (J{enemigo.idJugador}) por {danoFinal:F1} de daño. Vida restante: {Mathf.Max(0, enemigo.vidaActual):F1}");

        if (!enemigo.EstaVivo)
        {
            BattlePrototypeManager.Instance.NotificarMuerteUnidad(enemigo);
        }
    }
}