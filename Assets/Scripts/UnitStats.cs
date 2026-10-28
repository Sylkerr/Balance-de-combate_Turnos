using UnityEngine;

public class UnitStats : MonoBehaviour
{
    [Header("Identidad (Desde Google Sheets)")]
    public string nombreUnidad;

    [Header("Estadísticas (Desde Google Sheets)")]
    public float danoAtaque;
    public float vidaMaxima;
    public float vidaActual;
    public float cadencia;  // Segundos entre ataques
    public float alcance;   // Metros de Unity
    public float poder;     // Poder Combate del Excel

    [Header("Estado en Partida")]
    public int idJugador = 1; // 1 = Base Aliada, 2 = Base Enemiga
    public GridTile casillaAsignada;
    public bool EstaVivo => vidaActual > 0f;

    [Header("Registro del combate")]
    public float danoInfligido;
    public float danoRecibido;
    public int golpesDados;
    public int eliminaciones;
    public float segundoMuerte = -1f; // Segundos desde el inicio del combate (-1 = sigue viva)

    public float DanoPorSegundo => danoAtaque / Mathf.Max(0.01f, cadencia);

    // Cuántas unidades cuerpo a cuerpo tienen ahora mismo a esta unidad como objetivo.
    // Se usa para repartir a los atacantes en vez de apelotonarlos todos sobre el mismo enemigo.
    [HideInInspector] public int atacantesActuales;

    public void InicializarDesdeDatos(ExcelManager.UnitData data, int propietario)
    {
        nombreUnidad = data.nombre;
        danoAtaque = data.danoAtaque;
        vidaMaxima = data.vida;
        vidaActual = data.vida;
        cadencia = data.cadencia;
        alcance = data.alcance;
        poder = data.Poder;
        idJugador = propietario;
    }

    /// <summary>Aplica el daño (sin modificadores: el combate usa exactamente los números del Excel) y lanza el feedback visual.</summary>
    public void RecibirDano(float cantidad, UnitStats atacante)
    {
        if (!EstaVivo) return;

        float real = Mathf.Min(cantidad, vidaActual);
        vidaActual -= cantidad;
        danoRecibido += real;

        if (atacante != null)
        {
            atacante.danoInfligido += real;
            atacante.golpesDados++;
        }

        CombatVFX.NumeroFlotante(transform.position + Vector3.up * (transform.localScale.y * 2.4f), cantidad, idJugador);
        CombatVFX.Destello(gameObject);

        if (!EstaVivo)
        {
            if (atacante != null) atacante.eliminaciones++;
            CombatVFX.Muerte(transform.position, idJugador);
            BattlePrototypeManager.Instance.NotificarMuerteUnidad(this);
        }
    }
}
