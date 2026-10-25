using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Se coloca UNA vez en tu escena (objeto vacío "BattleLayout").
/// Aquí defines, desde el Inspector y moviendo objetos en la escena, dónde está
/// cada cuadrícula, dónde es la zona de combate y a dónde marcha el ganador.
/// Nada del escenario se genera por código: tu environment decorado es el que manda.
/// </summary>
public class BattleLayout : MonoBehaviour
{
    [System.Serializable]
    public class ZonaCircular
    {
        public Transform centro;
        [Min(0f)] public float radio = 3f;

        public bool EsValida => centro != null;

        public Vector3 PuntoAleatorio()
        {
            Vector2 c = Random.insideUnitCircle * radio;
            return centro.position + new Vector3(c.x, 0f, c.y);
        }

        public bool Contiene(Vector3 p, float margen = 0f)
        {
            if (centro == null) return false;
            Vector3 d = p - centro.position;
            d.y = 0f;
            return d.magnitude <= radio + margen;
        }
    }

    [Header("Cuadrículas (un objeto vacío por bando)")]
    [Tooltip("Posición/rotación = esquina de la fila 0, columna 0. Las columnas crecen hacia su eje X (rojo) y las filas hacia su eje Z (azul). " +
             "Si dejas como hijos tus propias casillas con el componente GridTile, se usarán esas en lugar de generarlas.")]
    public Transform anclaJ1;
    public Transform anclaJ2;
    [Min(1)] public int filas = 3;
    [Min(1)] public int columnas = 3;
    public float tamanoCasilla = 1.2f;
    [Tooltip("Opcional. Si está vacío se generan cubos simples. El pivote del prefab debería estar a nivel del suelo.")]
    public GameObject prefabCasilla;
    [Tooltip("Desactívalo si tus casillas ya tienen su propio material/decoración y no quieres que se tiñan.")]
    public bool tintarCasillas = true;
    public Color colorCasillaJ1 = new Color(0.25f, 0.55f, 0.35f);
    public Color colorCasillaJ2 = new Color(0.65f, 0.25f, 0.25f);
    [Tooltip("Capa(s) en las que están las casillas. Evita que el decorado bloquee el raycast del drag & drop.")]
    public LayerMask capaCasillas = ~0;

    [Header("Zona de combate")]
    [Tooltip("Los dos bandos se reparten por este círculo (cada uno por su mitad) y pelean aquí dentro.")]
    public ZonaCircular arena = new ZonaCircular();

    [Header("Meta de la marcha victoriosa")]
    [Tooltip("Zona a la que marcha el Jugador 1 si gana (normalmente, la base del J2).")]
    public ZonaCircular metaVictoriaJ1 = new ZonaCircular();
    [Tooltip("Zona a la que marcha el Jugador 2 si gana (normalmente, la base del J1).")]
    public ZonaCircular metaVictoriaJ2 = new ZonaCircular();

    [Header("NavMesh")]
    public float distanciaMuestreoNavMesh = 3f;

    // ---------------------------------------------------------
    public Transform AnclaDe(int idJugador) => idJugador == 1 ? anclaJ1 : anclaJ2;

    public static Vector3 PosicionCasilla(Transform ancla, float tamano, int fila, int col)
    {
        return ancla.position + ancla.right * (col * tamano) + ancla.forward * (fila * tamano);
    }

    public Vector3 CentroGrid(int idJugador)
    {
        Transform ancla = AnclaDe(idJugador);
        if (ancla == null) return transform.position;

        GridTile[] tiles = ancla.GetComponentsInChildren<GridTile>();
        if (tiles.Length > 0)
        {
            Vector3 suma = Vector3.zero;
            foreach (var t in tiles) suma += t.transform.position;
            return suma / tiles.Length;
        }

        return ancla.position
               + ancla.right * ((columnas - 1) * 0.5f * tamanoCasilla)
               + ancla.forward * ((filas - 1) * 0.5f * tamanoCasilla);
    }

    /// <summary>Punto aleatorio de la arena, siempre en la mitad más cercana a la base del jugador.</summary>
    public Vector3 PuntoDeAgrupacion(int idJugador)
    {
        if (!arena.EsValida) return CentroGrid(idJugador == 1 ? 2 : 1);

        Vector3 haciaMiBase = CentroGrid(idJugador) - arena.centro.position;
        haciaMiBase.y = 0f;
        if (haciaMiBase.sqrMagnitude < 0.01f)
            haciaMiBase = idJugador == 1 ? Vector3.left : Vector3.right;
        haciaMiBase.Normalize();

        Vector2 c = Random.insideUnitCircle * arena.radio;
        Vector3 offset = new Vector3(c.x, 0f, c.y);
        if (Vector3.Dot(offset, haciaMiBase) < 0f) offset = -offset;

        return arena.centro.position + offset;
    }

    public Vector3 PuntoMetaVictoria(int idGanador)
    {
        ZonaCircular zona = idGanador == 1 ? metaVictoriaJ1 : metaVictoriaJ2;
        if (zona != null && zona.EsValida) return zona.PuntoAleatorio();
        return CentroGrid(idGanador == 1 ? 2 : 1); // Por defecto: la base rival
    }

    public Vector3 AjustarANavMesh(Vector3 p)
    {
        if (NavMesh.SamplePosition(p, out NavMeshHit hit, distanciaMuestreoNavMesh, NavMesh.AllAreas))
            return hit.position;
        return p;
    }

    // ---------------------------------------------------------
    // Vista previa en el editor (Scene view)
    // ---------------------------------------------------------
    void OnDrawGizmos()
    {
        DibujarGrid(anclaJ1, colorCasillaJ1);
        DibujarGrid(anclaJ2, colorCasillaJ2);
        DibujarZona(arena, new Color(1f, 0.3f, 0.1f));
        DibujarZona(metaVictoriaJ1, new Color(0.2f, 0.8f, 1f));
        DibujarZona(metaVictoriaJ2, new Color(1f, 0.4f, 0.8f));
    }

    void DibujarGrid(Transform ancla, Color color)
    {
        if (ancla == null) return;
        Gizmos.color = color;
        for (int f = 0; f < filas; f++)
        {
            for (int c = 0; c < columnas; c++)
            {
                Vector3 p = PosicionCasilla(ancla, tamanoCasilla, f, c);
                Matrix4x4 old = Gizmos.matrix;
                Gizmos.matrix = Matrix4x4.TRS(p, ancla.rotation, Vector3.one);
                Gizmos.DrawWireCube(Vector3.zero, new Vector3(tamanoCasilla * 0.92f, 0.05f, tamanoCasilla * 0.92f));
                Gizmos.matrix = old;
            }
        }
    }

    void DibujarZona(ZonaCircular zona, Color color)
    {
        if (zona == null || !zona.EsValida) return;
        Gizmos.color = color;
        const int pasos = 48;
        Vector3 centro = zona.centro.position;
        Vector3 anterior = centro + new Vector3(zona.radio, 0f, 0f);
        for (int i = 1; i <= pasos; i++)
        {
            float a = i * Mathf.PI * 2f / pasos;
            Vector3 actual = centro + new Vector3(Mathf.Cos(a) * zona.radio, 0f, Mathf.Sin(a) * zona.radio);
            Gizmos.DrawLine(anterior, actual);
            anterior = actual;
        }
    }
}
