using UnityEngine;
using UnityEngine.EventSystems;

public class UnitDragItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public string nombreUnidadKey;
    public int idJugadorPropietario; // 1 = Aliada, 2 = Enemiga

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
            rend.material.color = idJugadorPropietario == 1 ? new Color(1f, 1f, 1f, 0.6f) : new Color(0.08f, 0.08f, 0.08f, 0.6f);
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (objetoFantasma3D == null || BattlePrototypeManager.Instance == null) return;

        Ray ray = Camera.main.ScreenPointToRay(eventData.position);
        int mascara = BattlePrototypeManager.Instance.MascaraCasillas;

        // 1) Casilla bajo el cursor (solo cuenta la capa de casillas: el decorado no estorba)
        GridTile tile = null;
        if (Physics.Raycast(ray, out RaycastHit hitTile, 200f, mascara, QueryTriggerInteraction.Collide))
        {
            tile = hitTile.collider.GetComponentInParent<GridTile>();
            objetoFantasma3D.transform.position = hitTile.point + Vector3.up * 0.6f;
        }
        // 2) Si no hay casilla, el fantasma sigue al ratón sobre lo que haya
        else if (Physics.Raycast(ray, out RaycastHit hitAny, 200f))
        {
            objetoFantasma3D.transform.position = hitAny.point + Vector3.up * 0.6f;
        }

        if (ultimaCasillaResaltada != null && ultimaCasillaResaltada != tile)
        {
            ultimaCasillaResaltada.Resaltar(false, false);
            ultimaCasillaResaltada = null;
        }

        if (tile != null)
        {
            bool esValida = (tile.idPropietarioCasilla == idJugadorPropietario) && !tile.EstaOcupada;
            tile.Resaltar(true, esValida);
            ultimaCasillaResaltada = tile;
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
        if (Physics.Raycast(ray, out RaycastHit hit, 200f, BattlePrototypeManager.Instance.MascaraCasillas, QueryTriggerInteraction.Collide))
        {
            GridTile tile = hit.collider.GetComponentInParent<GridTile>();
            if (tile != null)
            {
                BattlePrototypeManager.Instance.IntentarColocarUnidadPorDrag(tile, nombreUnidadKey, idJugadorPropietario);
            }
        }
    }
}
