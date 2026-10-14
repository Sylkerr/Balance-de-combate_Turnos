using UnityEngine;

public class GridTile : MonoBehaviour
{
    public int idPropietarioCasilla = 1; // 1 = Base Aliada, 2 = Base Enemiga
    public int fila;
    public int columna;
    public UnitStats unidadEnCasilla;

    private Renderer miRenderer;
    private Color colorOriginal;

    public bool EstaOcupada => unidadEnCasilla != null && unidadEnCasilla.EstaVivo;

    public void InicializarCasilla(int propietario, int f, int c, Color colorBase)
    {
        idPropietarioCasilla = propietario;
        fila = f;
        columna = c;
        colorOriginal = colorBase;
        miRenderer = GetComponent<Renderer>();
        if (miRenderer != null) miRenderer.material.color = colorOriginal;
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