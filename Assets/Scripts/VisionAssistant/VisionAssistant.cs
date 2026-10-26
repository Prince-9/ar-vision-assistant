using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Sends a frame + prompt to a vision-capable model via OpenRouter's
/// OpenAI-compatible chat/completions endpoint. Supports two modes:
///  - ExtractInfoFromFrame: fixed prompt asking for structured JSON (used by
///    the document/object extraction feature)
///  - Ask: free-form conversational question with the current frame attached
///    (used by the chat feature) - returns natural language, not JSON.
/// </summary>
public class VisionAssistant : MonoBehaviour
{
    private const string ApiUrl = "https://openrouter.ai/api/v1/chat/completions";

    [Header("Get a key at https://openrouter.ai - paste it here, never commit it")]
    [SerializeField] private string apiKey;

    [Tooltip("OpenRouter model string. Check https://openrouter.ai/models for current free vision-capable options - free-tier models and their exact IDs change over time, so verify this is still valid/free before relying on it.")]
    [SerializeField] private string model = "google/gemini-2.0-flash-exp:free";

    private const string ExtractionPrompt =
        "You are looking through an AR/MR headset's passthrough camera. " +
        "If the image shows a document, form, label, or template: extract ALL " +
        "visible text and organize it into JSON fields you can infer from context " +
        "(title, dates, names, amounts, addresses, table rows, headings - whatever " +
        "applies). If the image shows a physical object instead: return JSON with " +
        "\"object\", \"description\", and \"notable_details\" (array of strings). " +
        "If you're not confident anything readable or identifiable is in frame, " +
        "return {\"error\": \"nothing clear in view\"}. Respond with ONLY the JSON, " +
        "no markdown fences, no extra commentary.";

    private const string ChatSystemPrompt =
        "You are a helpful voice assistant embedded in an AR/MR headset. You can " +
        "see through the user's camera. Answer their questions conversationally " +
        "and concisely, as if speaking out loud. If an image is attached, use it " +
        "to inform your answer - describe what's relevant to their question. If " +
        "no image is relevant to the question, just answer normally.";

    /// <summary>Extraction mode: fixed prompt, expects structured JSON back.</summary>
    public void ExtractInfoFromFrame(byte[] jpgBytes, Action<string> onSuccess, Action<string> onError)
    {
        if (jpgBytes == null)
        {
            onError?.Invoke("No frame captured.");
            return;
        }
        StartCoroutine(SendRequest(null, ExtractionPrompt, jpgBytes, onSuccess, onError));
    }

    /// <summary>
    /// Chat mode: free-form question, optionally with an image attached.
    /// Returns natural conversational text, not JSON.
    /// </summary>
    public void Ask(string question, byte[] jpgBytesOrNull, Action<string> onSuccess, Action<string> onError)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            onError?.Invoke("Empty question.");
            return;
        }
        StartCoroutine(SendRequest(ChatSystemPrompt, question, jpgBytesOrNull, onSuccess, onError));
    }

    private IEnumerator SendRequest(string systemPromptOrNull, string userText, byte[] jpgBytesOrNull, Action<string> onSuccess, Action<string> onError)
    {
        var sb = new StringBuilder();
        sb.Append("{");
        sb.Append("\"model\":\"").Append(EscapeJson(model)).Append("\",");
        sb.Append("\"messages\":[");

        if (!string.IsNullOrEmpty(systemPromptOrNull))
        {
            sb.Append("{\"role\":\"system\",\"content\":\"").Append(EscapeJson(systemPromptOrNull)).Append("\"},");
        }

        sb.Append("{\"role\":\"user\",\"content\":[");
        sb.Append("{\"type\":\"text\",\"text\":\"").Append(EscapeJson(userText)).Append("\"}");

        if (jpgBytesOrNull != null)
        {
            string base64Image = Convert.ToBase64String(jpgBytesOrNull);
            string dataUri = "data:image/jpeg;base64," + base64Image;
            sb.Append(",{\"type\":\"image_url\",\"image_url\":{\"url\":\"").Append(EscapeJson(dataUri)).Append("\"}}");
        }

        sb.Append("]}");
        sb.Append("]");
        sb.Append("}");

        string jsonBody = sb.ToString();

        using (var req = new UnityWebRequest(ApiUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + apiKey);
            req.timeout = 30;

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                onSuccess?.Invoke(req.downloadHandler.text);
            }
            else
            {
                string detail = req.downloadHandler != null ? req.downloadHandler.text : req.error;
                onError?.Invoke($"{req.error}: {detail}");
            }
        }
    }

    /// <summary>
    /// Escapes a string for safe embedding inside a JSON string value.
    /// </summary>
    private string EscapeJson(string s)
    {
        var sb = new StringBuilder(s.Length + 16);
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        return sb.ToString();
    }
}
