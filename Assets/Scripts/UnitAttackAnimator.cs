using UnityEngine;

/// <summary>
/// Anima los accesorios de la unidad cuando ataca y le da un ligero balanceo al caminar.
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

    private Parte sombrero, espada, manoIzq, manoDer, rifle, arco, baston, escudo;
    private Parte[] todas;
    private float tiempo = -1f;
    private float duracion = 0.3f;

    // Balanceo al caminar (se aplica a los accesorios, no al cuerpo, para no pelearse con el NavMeshAgent)
    private Vector3 posicionAnterior;
    private float faseCaminar;

    void Awake()
    {
        sombrero = Buscar("Sombrero") ?? Buscar("Capucha");
        espada = Buscar("Espada");
        manoIzq = Buscar("Mano_Izq");
        manoDer = Buscar("Mano_Der");
        rifle = Buscar("Rifle");
        arco = Buscar("Arco");
        baston = Buscar("Baston");
        escudo = Buscar("Escudo");
        todas = new[] { sombrero, espada, manoIzq, manoDer, rifle, arco, baston, escudo };
        posicionAnterior = transform.position;
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
        Vector3 desplazamiento = transform.position - posicionAnterior;
        desplazamiento.y = 0f;
        float velocidad = desplazamiento.magnitude / Mathf.Max(0.0001f, Time.deltaTime);
        posicionAnterior = transform.position;

        if (tiempo < 0f)
        {
            AnimarCaminar(velocidad);
            return;
        }

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
        AnimarRetroceso(rifle, t, 0.3f, -14f);
        AnimarRetroceso(arco, t, 0.2f, 0f);
        AnimarBaston(t);
        AnimarEscudo(t);
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
    // Caminar: los accesorios suben y bajan al ritmo de los pasos
    // ---------------------------------------------------------
    void AnimarCaminar(float velocidad)
    {
        if (velocidad < 0.2f)
        {
            if (faseCaminar != 0f) { faseCaminar = 0f; RestaurarTodo(); }
            return;
        }

        faseCaminar += Time.deltaTime * velocidad * 4f;
        float bote = Mathf.Abs(Mathf.Sin(faseCaminar)) * 0.08f;
        float balanceo = Mathf.Sin(faseCaminar) * 8f;

        foreach (Parte p in todas)
        {
            if (p == null) continue;
            p.t.localPosition = p.pos + Vector3.up * bote;
            p.t.localRotation = Quaternion.Euler(0f, 0f, balanceo) * p.rot;
        }
    }

    // ---------------------------------------------------------
    // Mago / Cultista: el sombrero da un respingo hacia arriba y se inclina hacia delante al lanzar el hechizo
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
    // Caballero / Paladín / Cultista: tajo de arriba abajo girando la espada alrededor del mango
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
    // Ogro: las dos manos embisten hacia delante, la derecha un instante después
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
    // Mosquetero / Arquero: retroceso del arma al disparar
    // ---------------------------------------------------------
    void AnimarRetroceso(Parte arma, float t, float distancia, float cabeceo)
    {
        if (arma == null) return;
        float g = Golpe(t);
        arma.t.localPosition = arma.pos - Vector3.forward * distancia * g;
        arma.t.localRotation = Quaternion.Euler(cabeceo * g, 0f, 0f) * arma.rot;
    }

    // ---------------------------------------------------------
    // Mago / Sacerdote: el bastón se alza y apunta hacia delante
    // ---------------------------------------------------------
    void AnimarBaston(float t)
    {
        if (baston == null) return;
        float s = Suave(t);
        baston.t.localPosition = baston.pos + new Vector3(0f, 0.25f, 0.3f) * s;
        baston.t.localRotation = Quaternion.Euler(50f * s, 0f, 0f) * baston.rot;
    }

    // ---------------------------------------------------------
    // Caballero / Paladín: empujón con el escudo
    // ---------------------------------------------------------
    void AnimarEscudo(float t)
    {
        if (escudo == null) return;
        float g = Golpe((t - 0.1f) / 0.9f);
        escudo.t.localPosition = escudo.pos + Vector3.forward * 0.2f * g;
    }

    void RestaurarTodo()
    {
        foreach (Parte p in todas) p?.Restaurar();
    }
}
