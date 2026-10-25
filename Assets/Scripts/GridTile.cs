using UnityEngine;

public class GridTile : MonoBehaviour
{
    public int idPropietarioCasilla = 1; // 1 = Base Aliada, 2 = Base Enemiga
    public int fila;
    public int columna;
    public UnitStats unidadEnCasilla;

    private Renderer miRenderer;
    private Color colorOriginal = Color.white;

    public bool EstaOcupada => unidadEnCasilla != null && unidadEnCasilla.EstaVivo;

    /// <param name="tintar">Si es false se respeta el material/color que ya tenga la casilla.</param>
    public void InicializarCasilla(int propietario, int f, int c, Color colorBase, bool tintar = true)
    {
        idPropietarioCasilla = propietario;
        fila = f;
        columna = c;
        miRenderer = GetComponentInChildren<Renderer>();

        if (miRenderer == null) return;

        if (tintar)
        {
            colorOriginal = colorBase;
            miRenderer.material.color = colorOriginal;
        }
        else
        {
            colorOriginal = miRenderer.material.color;
        }
    }

    public void Resaltar(bool activo, bool esValida)
    {
        if (miRenderer == null) return;
        if (!activo)
        {
            miRenderer.material.color = colorOriginal;
        }
        else
        {
            miRenderer.material.color = esValida ? Color.yellow : Color.red;
        }
    }
}
