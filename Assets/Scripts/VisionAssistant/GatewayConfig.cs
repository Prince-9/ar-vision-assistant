using UnityEngine;

/// <summary>
/// Single place for the backend URL used by all Unity gateway clients.
/// The Quest talks to the backend; the backend talks to Ollama.
/// </summary>
public class GatewayConfig : MonoBehaviour
{
    [Tooltip("HTTPS backend URL reachable from the Quest headset. The backend calls Ollama server-side (Ollama tunnel: https://6z7xkwds-11434.inc1.devtunnels.ms); Unity sends all requests through this backend.")]
    [SerializeField] private string baseUrl = "https://6z7xkwds-8000.inc1.devtunnels.ms";

    public string BaseUrl => baseUrl;

    private static GatewayConfig _instance;
    public static GatewayConfig Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindFirstObjectByType<GatewayConfig>();
            return _instance;
        }
    }

    private void Awake()
    {
        _instance = this;
    }
}
