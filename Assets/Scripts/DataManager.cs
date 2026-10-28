using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Descarga la hoja "BALANCEO DE SISTEMAS" (Hoja 2) de Google Sheets y la convierte en una lista de unidades.
/// Columnas que se leen (se localizan por el texto de la cabecera): Nombre | Daño de ataque | Vida | Cadencia | Alcance | Poder Combate.
/// </summary>
public class ExcelManager : MonoBehaviour
{
    public static ExcelManager Instance { get; private set; }

    [Header("URL de tu Google Sheets")] [SerializeField]
    private string csvUrl =
        "https://docs.google.com/spreadsheets/d/1skunAahwbC_x-WSd5KPmifCHFmGFpmf_tz8SIGo7gFI/edit?gid=278273004#gid=278273004";

    [Tooltip("gid de la pestaña a leer (Hoja 2). Si se deja vacío se usa el gid que venga en la URL.")] [SerializeField]
    private string gidHoja = "278273004";

    [Serializable]
    public class UnitData
    {
        public string nombre;
        public float danoAtaque;
        public float vida;
        public float cadencia;    // Segundos entre ataque y ataque
        public float alcance;     // Metros de Unity
        public float poderExcel;  // Columna "Poder Combate" tal cual viene en el Excel (0 si no hay dato)

        public float DanoPorSegundo => danoAtaque / Mathf.Max(0.01f, cadencia);

        /// <summary>Misma fórmula que el Excel: Daño × Vida × Alcance / Cadencia.</summary>
        public float PoderCalculado => danoAtaque * vida * alcance / Mathf.Max(0.01f, cadencia);

        public float Poder => poderExcel > 0f ? poderExcel : PoderCalculado;
    }

    public Dictionary<string, UnitData> UnidadesPorNombre = new Dictionary<string, UnitData>();
    public List<UnitData> ListaUnidades = new List<UnitData>();

    public bool DatosCargados { get; private set; } = false;
    public event Action OnDatosCargados;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        StartCoroutine(DownloadAndAssignCSV());
    }

    [ContextMenu("Recargar Excel Ahora")]
    public void RecargarDatosExcel()
    {
        StartCoroutine(DownloadAndAssignCSV());
    }

    private string ObtenerUrlExportacionCSV(string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl)) return "";
        if (rawUrl.Contains("/export?format=csv")) return rawUrl;

        string gid = gidHoja;
        if (string.IsNullOrWhiteSpace(gid))
        {
            Match m = Regex.Match(rawUrl, @"gid=(\d+)");
            gid = m.Success ? m.Groups[1].Value : "0";
        }

        int editIndex = rawUrl.IndexOf("/edit");
        if (editIndex > 0)
            return rawUrl.Substring(0, editIndex) + "/export?format=csv&gid=" + gid.Trim();

        return rawUrl;
    }

    IEnumerator DownloadAndAssignCSV()
    {
        DatosCargados = false;
        string finalUrl = ObtenerUrlExportacionCSV(csvUrl);

        UnityWebRequest www = UnityWebRequest.Get(finalUrl);
        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Error descargando CSV: " + www.error);
        }
        else
        {
            ParseAndAssignValues(www.downloadHandler.text);
            DatosCargados = true;
            OnDatosCargados?.Invoke();
        }
    }

    void ParseAndAssignValues(string csvData)
    {
        UnidadesPorNombre.Clear();
        ListaUnidades.Clear();

        string[] lines = csvData.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        List<List<string>> filas = new List<List<string>>();
        foreach (string l in lines) filas.Add(SplitCSVLine(l));

        // Columnas por defecto (B..G) por si no se encuentra la cabecera
        int colNombre = 1, colDano = 2, colVida = 3, colCadencia = 4, colAlcance = 5, colPoder = 6;
        foreach (var f in filas)
        {
            int idxNombre = f.FindIndex(c => c.Trim().Equals("Nombre", StringComparison.OrdinalIgnoreCase));
            if (idxNombre < 0) continue;

            colNombre = idxNombre;
            colDano = BuscarColumna(f, colDano, "daño", "dano");
            colVida = BuscarColumna(f, colVida, "vida");
            colCadencia = BuscarColumna(f, colCadencia, "cadencia");
            colAlcance = BuscarColumna(f, colAlcance, "alcance");
            colPoder = BuscarColumna(f, colPoder, "poder");
            break;
        }

        foreach (var f in filas)
        {
            if (f.Count <= Mathf.Max(colNombre, colAlcance)) continue;

            string nombre = f[colNombre].Trim();
            if (string.IsNullOrEmpty(nombre)) continue;

            // Solo son unidades las filas con daño y vida numéricos (descarta títulos y cabeceras)
            if (!TryParseNumero(f[colDano], out float dano) || !TryParseNumero(f[colVida], out float vida) || vida <= 0f)
                continue;

            TryParseNumero(f[colCadencia], out float cadencia);
            TryParseNumero(f[colAlcance], out float alcance);
            float poder = 0f;
            if (colPoder < f.Count) TryParseNumero(f[colPoder], out poder);

            UnitData data = new UnitData
            {
                nombre = nombre,
                danoAtaque = dano,
                vida = vida,
                cadencia = cadencia > 0f ? cadencia : 1f,
                alcance = alcance > 0f ? alcance : 1f,
                poderExcel = poder
            };

            string key = nombre.ToLowerInvariant();
            if (UnidadesPorNombre.ContainsKey(key)) continue;
            UnidadesPorNombre[key] = data;
            ListaUnidades.Add(data);

            if (poder > 0f && Mathf.Abs(poder - data.PoderCalculado) > 1f)
                Debug.LogWarning($"[ExcelManager] {nombre}: el Excel dice Poder {poder:0.##} pero Daño×Vida×Alcance/Cadencia = {data.PoderCalculado:0.##}");
        }

        Debug.Log($"[ExcelManager] {ListaUnidades.Count} unidades cargadas desde Google Sheets.");
    }

    private int BuscarColumna(List<string> cabecera, int porDefecto, params string[] claves)
    {
        for (int c = 0; c < cabecera.Count; c++)
        {
            string h = cabecera[c].Trim().ToLowerInvariant();
            foreach (string k in claves)
                if (h.Contains(k)) return c;
        }

        return porDefecto;
    }

    private List<string> SplitCSVLine(string line)
    {
        List<string> result = new List<string>();
        bool inQuotes = false;
        string current = "";

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"') inQuotes = !inQuotes;
            else if (c == ',' && !inQuotes)
            {
                result.Add(current);
                current = "";
            }
            else current += c;
        }

        result.Add(current);
        return result;
    }

    /// <summary>
    /// Lee números con formato español: "1,6" = 1.6 y "1.250" = 1250 (el punto es separador de miles).
    /// </summary>
    public static bool TryParseNumero(string raw, out float value)
    {
        value = 0f;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        string s = raw.Trim().Replace("%", "").Replace(" ", "");

        if (s.Contains(","))
            s = s.Replace(".", "").Replace(",", ".");
        else if (Regex.IsMatch(s, @"^-?\d{1,3}(\.\d{3})+$"))
            s = s.Replace(".", "");

        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
