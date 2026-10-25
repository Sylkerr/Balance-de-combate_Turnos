using UnityEngine;

/// <summary>
/// Anima los accesorios de la unidad (sombrero, espada, manos, rifle) cuando realiza un ataque.
/// Detecta qué accesorios tiene por el nombre de sus hijos, así que no hace falta configurar nada.
/// </summary>
public class UnitAttackAnimator : MonoBehaviour
{
    private class Parte
    {
        public Transform t;
        public Vector3 pos;
        public Quaternion rot;
        public Vector3 escala;

        public Parte(Transform transform)
        {
            t = transform;
            pos = transform.localPosition;
            rot = transform.localRotation;
            escala = transform.localScale;
        }

        public void Restaurar()
        {
            t.localPosition = pos;
            t.localRotation = rot;
            t.localScale = escala;
        }
    }

    private Parte sombrero, espada, manoIzq, manoDer, rifle;
    private float tiempo = -1f;
    private float duracion = 0.3f;

    void Awake()
    {
        sombrero = Buscar("Sombrero");
        espada = Buscar("Espada");
        manoIzq = Buscar("Mano_Izq");
        manoDer = Buscar("Mano_Der");
        rifle = Buscar("Rifle");
    }

    Parte Buscar(string nombre)
    {
        Transform t = transform.Find(nombre);
        return t != null ? new Parte(t) : null;
    }

    /// <summary>Lanza la animación de ataque. Si se solapa con otra, se reinicia desde la pose de reposo.</summary>
    public void Reproducir(float segundos)
    {
        duracion = Mathf.Clamp(segundos, 0.12f, 0.45f);
        if (tiempo >= 0f) RestaurarTodo();
        tiempo = 0f;
    }

    void Update()
    {
        if (tiempo < 0f) return;

        tiempo += Time.deltaTime;
        float t = tiempo / duracion;

        if (t >= 1f)
        {
            RestaurarTodo();
            tiempo = -1f;
            return;
        }

        AnimarSombrero(t);
        AnimarEspada(t);
        AnimarManos(t);
        AnimarRifle(t);
    }

    // ---------------------------------------------------------
    // Curvas: Suave sube y baja de forma simétrica; Golpe sube rápido (30 %) y vuelve más despacio
    // ---------------------------------------------------------
    static float Suave(float t) => Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);

    static float Golpe(float t)
    {
        t = Mathf.Clamp01(t);
        float x = t < 0.3f ? t / 0.3f : 1f - (t - 0.3f) / 0.7f;
        return Mathf.SmoothStep(0f, 1f, x);
    }

    // ---------------------------------------------------------
    // Mago: el sombrero da un respingo hacia arriba y se inclina hacia delante al lanzar el hechizo
    // ---------------------------------------------------------
    void AnimarSombrero(float t)
    {
        if (sombrero == null) return;
        float s = Suave(t);
        sombrero.t.localPosition = sombrero.pos + Vector3.up * 0.18f * s;
        sombrero.t.localRotation = Quaternion.Euler(28f * s, 0f, 0f) * sombrero.rot;
        sombrero.t.localScale = sombrero.escala * (1f + 0.12f * s);
    }

    // ---------------------------------------------------------
    // Luchador: tajo de arriba abajo girando la espada alrededor del mango
    // ---------------------------------------------------------
    void AnimarEspada(float t)
    {
        if (espada == null) return;
        float g = Golpe(t);

        Quaternion nueva = Quaternion.Euler(110f * g, 0f, 0f) * espada.rot;
        float mitad = espada.escala.y; // Cilindro de Unity: altura 2, así que la mitad es la escala en Y

        Vector3 mango = espada.pos + espada.rot * new Vector3(0f, -mitad, 0f);
        espada.t.localRotation = nueva;
        espada.t.localPosition = mango + nueva * new Vector3(0f, mitad, 0f);
    }

    // ---------------------------------------------------------
    // Tanque: las dos manos embisten hacia delante, la derecha un instante después
    // ---------------------------------------------------------
    void AnimarManos(float t)
    {
        if (manoIzq != null)
        {
            float g = Golpe(t);
            manoIzq.t.localPosition = manoIzq.pos + Vector3.forward * 0.55f * g;
            manoIzq.t.localScale = manoIzq.escala * (1f + 0.35f * g);
        }

        if (manoDer != null)
        {
            float g = Golpe((t - 0.15f) / 0.85f);
            manoDer.t.localPosition = manoDer.pos + Vector3.forward * 0.55f * g;
            manoDer.t.localScale = manoDer.escala * (1f + 0.35f * g);
        }
    }

    // ---------------------------------------------------------
    // Arquero: retroceso del rifle al disparar (empuja hacia atrás y levanta el cañón)
    // ---------------------------------------------------------
    void AnimarRifle(float t)
    {
        if (rifle == null) return;
        float g = Golpe(t);
        rifle.t.localPosition = rifle.pos - Vector3.forward * 0.3f * g;
        rifle.t.localRotation = Quaternion.Euler(-14f * g, 0f, 0f) * rifle.rot;
    }

    void RestaurarTodo()
    {
        sombrero?.Restaurar();
        espada?.Restaurar();
        manoIzq?.Restaurar();
        manoDer?.Restaurar();
        rifle?.Restaurar();
    }
}
