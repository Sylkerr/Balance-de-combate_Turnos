using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Networking;

public class ExcelManager : MonoBehaviour
{
    public static ExcelManager Instance { get; private set; }

    [Header("URL de tu Google Sheets")]
    [SerializeField] private string csvUrl = "https://docs.google.com/spreadsheets/d/1skunAahwbC_x-WSd5KPmifCHFmGFpmf_tz8SIGo7gFI/edit?gid=0#gid=0";

    [Serializable]
    public class UnitData
    {
        public string faccion;
        public string nombre;
        public string rol;
        public string tipoAtaque;
        public float danoAtaque;
        public float vida;
        public float velocidadAtaque;
        public float alcance;
        public float defensaFisica;
        public float defensaMagica;
    }

    public Dictionary<string, UnitData> UnidadesPorNombre = new Dictionary<string, UnitData>();
    public List<UnitData> ListaUnidades = new List<UnitData>();

    private Dictionary<string, Dictionary<string, float>> matrizFacciones = new Dictionary<string, Dictionary<string, float>>();

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

        int editIndex = rawUrl.IndexOf("/edit");
        if (editIndex > 0)
        {
            return rawUrl.Substring(0, editIndex) + "/export?format=csv&gid=0";
        }
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
        matrizFacciones.Clear();

        Dictionary<string, UnitData> respaldoPorRol = new Dictionary<string, UnitData>();
        string[] lines = csvData.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        List<string> ordenColumnasFacciones = new List<string>();
        bool leyendoTablaFacciones = false;

        for (int i = 0; i < lines.Length; i++)
        {
            List<string> fields = SplitCSVLine(lines[i]);
            if (fields.Count < 6) continue;

            string colB = fields[1].Trim();
            string colC = fields[2].Trim();

            if (colB.Equals("FACCIONES", StringComparison.OrdinalIgnoreCase))
            {
                leyendoTablaFacciones = true;
                continue;
            }

            if (!leyendoTablaFacciones)
            {
                if (string.IsNullOrEmpty(colB) || colB.Equals("Facción", StringComparison.OrdinalIgnoreCase) || colB.Contains("BALANCEO"))
                    continue;

                if (fields.Count >= 11 && !string.IsNullOrEmpty(colC))
                {
                    string rol = fields[3].Trim();
                    string rolKey = rol.ToLowerInvariant();

                    bool tieneDano = TryParseFloat(fields[5], out float dano);
                    bool tieneVida = TryParseFloat(fields[6], out float vida);
                    bool tieneStats = tieneDano && tieneVida && vida > 0f;
                    TryParseFloat(fields[7], out float velAtaque);
                    TryParseFloat(fields[8], out float alcance);
                    TryParsePercentage(fields[9], out float defFisica);
                    TryParsePercentage(fields[10], out float defMagica);

                    // Si las celdas aún están vacías en el Excel, usa temporalmente las del mismo Rol
                    if (!tieneStats && respaldoPorRol.TryGetValue(rolKey, out UnitData baseRol))
                    {
                        dano = baseRol.danoAtaque;
                        vida = baseRol.vida;
                        velAtaque = baseRol.velocidadAtaque;
                        alcance = baseRol.alcance;
                        defFisica = baseRol.defensaFisica;
                        defMagica = baseRol.defensaMagica;
                    }

                    UnitData data = new UnitData
                    {
                        faccion = colB,
                        nombre = colC,
                        rol = rol,
                        tipoAtaque = fields[4].Trim(),
                        danoAtaque = dano > 0 ? dano : 10f,
                        vida = vida > 0 ? vida : 50f,
                        velocidadAtaque = velAtaque > 0 ? velAtaque : 1f,
                        alcance = alcance > 0 ? alcance : 1f,
                        defensaFisica = defFisica,
                        defensaMagica = defMagica
                    };

                    if (tieneStats && !respaldoPorRol.ContainsKey(rolKey))
                        respaldoPorRol[rolKey] = data;

                    string key = colC.ToLowerInvariant();
                    UnidadesPorNombre[key] = data;
                    ListaUnidades.Add(data);
                }
            }
            else
            {
                if (string.IsNullOrEmpty(colB) && !string.IsNullOrEmpty(colC) && !colC.Equals("Defensores", StringComparison.OrdinalIgnoreCase))
                {
                    ordenColumnasFacciones.Clear();
                    for (int c = 2; c <= 5 && c < fields.Count; c++)
                        ordenColumnasFacciones.Add(fields[c].Trim().ToLowerInvariant());
                }
                else if (!string.IsNullOrEmpty(colB) && ordenColumnasFacciones.Count > 0)
                {
                    string atacanteKey = colB.ToLowerInvariant();
                    if (!matrizFacciones.ContainsKey(atacanteKey))
                        matrizFacciones[atacanteKey] = new Dictionary<string, float>();

                    for (int c = 0; c < ordenColumnasFacciones.Count; c++)
                    {
                        int colIndex = c + 2;
                        if (colIndex < fields.Count && TryParseFloat(fields[colIndex], out float mult))
                            matrizFacciones[atacanteKey][ordenColumnasFacciones[c]] = mult;
                    }
                }
            }
        }

        Debug.Log($"[ExcelManager] {ListaUnidades.Count} unidades cargadas desde Google Sheets.");
    }

    public float ObtenerMultiplicadorFaccion(string faccionAtacante, string faccionDefensora)
    {
        if (string.IsNullOrEmpty(faccionAtacante) || string.IsNullOrEmpty(faccionDefensora)) return 1f;
        string atk = faccionAtacante.Trim().ToLowerInvariant();
        string def = faccionDefensora.Trim().ToLowerInvariant();

        if (matrizFacciones.TryGetValue(atk, out var dictDef) && dictDef.TryGetValue(def, out float mult))
            return mult;

        return 1f;
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
            else if (c == ',' && !inQuotes) { result.Add(current); current = ""; }
            else current += c;
        }
        result.Add(current);
        return result;
    }

    private bool TryParseFloat(string raw, out float value)
    {
        value = 0f;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        string normalized = raw.Trim().Replace("%", "").Replace(",", ".");
        return float.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    private bool TryParsePercentage(string raw, out float value)
    {
        if (TryParseFloat(raw, out float num))
        {
            value = num > 1f ? num / 100f : num;
            return true;
        }
        value = 0f;
        return false;
    }
}