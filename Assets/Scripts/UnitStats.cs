using UnityEngine;

public class UnitStats : MonoBehaviour
{
    [Header("Identidad (Desde Google Sheets)")]
    public string faccion;
    public string nombreUnidad;
    public string rol;
    public string tipoAtaque;

    [Header("Estadísticas (Desde Google Sheets)")]
    public float danoAtaque;
    public float vidaMaxima;
    public float vidaActual;
    public float velocidadAtaque; // Ataques por segundo (APS)
    public float alcance;         // Metros de Unity
    [Range(0f, 1f)] public float defensaFisica;
    [Range(0f, 1f)] public float defensaMagica;

    [Header("Estado en Partida")]
    public int idJugador = 1; // 1 = Base Aliada, 2 = Base Enemiga
    public GridTile casillaAsignada;
    public bool EstaVivo => vidaActual > 0f;

    // Cuántas unidades cuerpo a cuerpo tienen ahora mismo a esta unidad como objetivo.
    // Se usa para repartir a los atacantes en vez de apelotonarlos todos sobre el mismo enemigo.
    [HideInInspector] public int atacantesActuales;

    public void InicializarDesdeDatos(ExcelManager.UnitData data, int propietario)
    {
        faccion = data.faccion;
        nombreUnidad = data.nombre;
        rol = data.rol;
        tipoAtaque = data.tipoAtaque;
        danoAtaque = data.danoAtaque;
        vidaMaxima = data.vida;
        vidaActual = data.vida;
        velocidadAtaque = data.velocidadAtaque;
        alcance = data.alcance;
        defensaFisica = data.defensaFisica;
        defensaMagica = data.defensaMagica;
        idJugador = propietario;
    }
}
