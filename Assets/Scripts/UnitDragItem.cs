using UnityEngine;
using UnityEngine.EventSystems;

public class UnitDragItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public string nombreUnidadKey;
    public int idJugadorPropietario; // 1 = Aliada (Izquierda), 2 = Enemiga (Derecha)

    private GameObject objetoFantasma3D;
    private GridTile ultimaCasillaResaltada;

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (BattlePrototypeManager.Instance == null ||
            BattlePrototypeManager.Instance.EstadoActual != BattlePrototypeManager.EstadoPartida.Colocacion)
            return;

        // Crear una cápsula semitransparente que sigue al ratón sobre el tablero
        objetoFantasma3D = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        objetoFantasma3D.name = "Preview_Arrastre";
        Destroy(objetoFantasma3D.GetComponent<Collider>()); // Sin collider para no bloquear el Raycast
        objetoFantasma3D.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);

        Renderer rend = objetoFantasma3D.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.material.color = idJugadorPropietario == 1 ? new Color(0f, 0.8f, 1f, 0.6f) : new Color(1f, 0.3f, 0.6f, 0.6f);
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (objetoFantasma3D == null) return;

        Ray ray = Camera.main.ScreenPointToRay(eventData.position);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            objetoFantasma3D.transform.position = hit.point + Vector3.up * 0.6f;

            GridTile tile = hit.collider.GetComponent<GridTile>();
            if (ultimaCasillaResaltada != null && ultimaCasillaResaltada != tile)
            {
                ultimaCasillaResaltada.Resaltar(false, false);
            }

            if (tile != null)
            {
                bool esValida = (tile.idPropietarioCasilla == idJugadorPropietario) && !tile.EstaOcupada;
                tile.Resaltar(true, esValida);
                ultimaCasillaResaltada = tile;
            }
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (ultimaCasillaResaltada != null)
        {
            ultimaCasillaResaltada.Resaltar(false, false);
            ultimaCasillaResaltada = null;
        }

        if (objetoFantasma3D != null)
        {
            Destroy(objetoFantasma3D);
        }

        if (BattlePrototypeManager.Instance == null ||
            BattlePrototypeManager.Instance.EstadoActual != BattlePrototypeManager.EstadoPartida.Colocacion)
            return;

        Ray ray = Camera.main.ScreenPointToRay(eventData.position);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            GridTile tile = hit.collider.GetComponent<GridTile>();
            if (tile != null)
            {
                BattlePrototypeManager.Instance.IntentarColocarUnidadPorDrag(tile, nombreUnidadKey, idJugadorPropietario);
            }
        }
    }
}