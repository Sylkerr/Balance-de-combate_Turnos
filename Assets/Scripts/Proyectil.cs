using UnityEngine;

/// <summary>Bala/flecha/hechizo que persigue a su objetivo y aplica el daño al llegar.</summary>
public class Proyectil : MonoBehaviour
{
    public UnitStats objetivo;
    public Color color = Color.white;
    public float velocidad = 14f;
    public System.Action alImpactar;

    private float rastro;

    void Update()
    {
        if (objetivo == null || !objetivo.gameObject.activeInHierarchy)
        {
            Destroy(gameObject); // El objetivo murió por otro golpe: el disparo se pierde
            return;
        }

        Vector3 destino = objetivo.transform.position + Vector3.up * objetivo.transform.localScale.y * 0.4f;
        transform.position = Vector3.MoveTowards(transform.position, destino, velocidad * Time.deltaTime);

        // Estela: bolitas que se encogen detrás del proyectil
        rastro -= Time.deltaTime;
        if (rastro <= 0f)
        {
            rastro = 0.03f;
            GameObject estela = CombatVFX.CrearPrimitivaVisual(PrimitiveType.Sphere, transform.position, color);
            EfectoTemporal e = estela.AddComponent<EfectoTemporal>();
            e.duracion = 0.2f;
            e.escalaInicial = transform.localScale * 0.8f;
            e.escalaFinal = Vector3.zero;
            e.rend = estela.GetComponent<Renderer>();
        }

        if ((transform.position - destino).sqrMagnitude < 0.01f)
        {
            CombatVFX.Impacto(transform.position, color, 0.5f);
            alImpactar?.Invoke();
            Destroy(gameObject);
        }
    }

    void OnDestroy()
    {
        Renderer r = GetComponent<Renderer>();
        if (r != null) Destroy(r.material);
    }
}
