using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Talks to the gateway's /ask (LLM), /ocr, and /detect endpoints.
/// This replaces cloud VisionAssistant.cs for the fully-local setup - same
/// role, different backend.
/// </summary>
public class GatewayVisionClient : MonoBehaviour
{
    public string LastSource { get; private set; }
    public int RememberedViewCount { get; private set; }
    /// <summary>
    /// Asks the local LLM a question, optionally with an image attached
    /// (pass null for imageJpg for a plain text question - uses the faster
    /// text model on the gateway side instead of the vision model).
    /// </summary>
    public void Ask(string question, byte[] imageJpg, Action<string> onSuccess, Action<string> onError, string historyJson = null, string sessionId = null)
    {
        StartCoroutine(AskCoroutine(question, imageJpg, onSuccess, onError, historyJson, sessionId));
    }

    private IEnumerator AskCoroutine(string question, byte[] imageJpg, Action<string> onSuccess, Action<string> onError, string historyJson, string sessionId)
    {
        string url = GatewayConfig.Instance.BaseUrl + "/ask";

        WWWForm form = new WWWForm();
        form.AddField("question", question);
        if (!string.IsNullOrEmpty(sessionId)) form.AddField("session_id", sessionId);
        if (!string.IsNullOrEmpty(historyJson)) form.AddField("history", historyJson);
        if (imageJpg != null)
        {
            form.AddBinaryData("image", imageJpg, "frame.jpg", "image/jpeg");
        }

        using (var req = UnityWebRequest.Post(url, form))
        {
            req.timeout = 250; // Model retry plus optional OCR for visual memory.
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                string answer;
                try
                {
                    var parsed = SimpleJsonParser.Parse(req.downloadHandler.text) as Dictionary<string, object>;
                    answer = parsed != null && parsed.TryGetValue("answer", out object a) ? a as string : null;
                    ReadMemoryStatus(parsed);
                    if (string.IsNullOrWhiteSpace(answer)) throw new FormatException("The model returned an empty answer.");
                }
                catch (Exception e) { onError?.Invoke("Invalid assistant response: " + e.Message); yield break; }
                onSuccess?.Invoke(answer);
            }
            else
            {
                onError?.Invoke($"{req.error}: {req.downloadHandler.text}");
            }
        }
    }

    /// <summary>Runs OCR on an image via the gateway's /ocr endpoint.</summary>
    public void RunOcr(byte[] imageJpg, Action<string> onSuccess, Action<string> onError, string sessionId = null)
    {
        StartCoroutine(OcrCoroutine(imageJpg, onSuccess, onError, sessionId));
    }

    private IEnumerator OcrCoroutine(byte[] imageJpg, Action<string> onSuccess, Action<string> onError, string sessionId)
    {
        string url = GatewayConfig.Instance.BaseUrl + "/ocr";

        WWWForm form = new WWWForm();
        if (!string.IsNullOrEmpty(sessionId)) form.AddField("session_id", sessionId);
        form.AddBinaryData("image", imageJpg, "frame.jpg", "image/jpeg");

        using (var req = UnityWebRequest.Post(url, form))
        {
            req.timeout = 60;
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                string text;
                try
                {
                    var parsed = SimpleJsonParser.Parse(req.downloadHandler.text) as Dictionary<string, object>;
                    if (parsed == null || !parsed.TryGetValue("text", out object t) || !(t is string))
                        throw new FormatException("Missing text in OCR response.");
                    text = (string)t;
                    ReadMemoryStatus(parsed);
                }
                catch (Exception e) { onError?.Invoke("Invalid scan response: " + e.Message); yield break; }
                onSuccess?.Invoke(text);
            }
            else
            {
                onError?.Invoke($"{req.error}: {req.downloadHandler.text}");
            }
        }
    }

    private void ReadMemoryStatus(Dictionary<string, object> data)
    {
        LastSource = data != null && data.TryGetValue("source", out object source) ? source as string : "current";
        RememberedViewCount = data != null && data.TryGetValue("memory_count", out object count) ? Convert.ToInt32(count) : 0;
    }

    public void ClearSession(string sessionId, Action onSuccess, Action<string> onError)
    {
        StartCoroutine(ClearSessionCoroutine(sessionId, onSuccess, onError));
    }

    public void ReleaseSession(string sessionId)
    {
        // Best-effort cleanup on scene exit without relying on a destroyed coroutine.
        // The gateway also expires abandoned sessions automatically.
        if (GatewayConfig.Instance == null) return;
        var request = UnityWebRequest.Delete(GatewayConfig.Instance.BaseUrl + "/session/" + sessionId);
        request.timeout = 5;
        request.SendWebRequest().completed += operation => request.Dispose();
    }

    private IEnumerator ClearSessionCoroutine(string sessionId, Action onSuccess, Action<string> onError)
    {
        using (var req = UnityWebRequest.Delete(GatewayConfig.Instance.BaseUrl + "/session/" + sessionId))
        {
            req.timeout = 10;
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                RememberedViewCount = 0;
                LastSource = null;
                onSuccess?.Invoke();
            }
            else onError?.Invoke(req.error);
        }
    }

    /// <summary>Runs object detection on an image via the gateway's /detect endpoint.</summary>
    public void RunDetect(byte[] imageJpg, Action<List<object>> onSuccess, Action<string> onError)
    {
        StartCoroutine(DetectCoroutine(imageJpg, onSuccess, onError));
    }

    private IEnumerator DetectCoroutine(byte[] imageJpg, Action<List<object>> onSuccess, Action<string> onError)
    {
        string url = GatewayConfig.Instance.BaseUrl + "/detect";

        WWWForm form = new WWWForm();
        form.AddBinaryData("image", imageJpg, "frame.jpg", "image/jpeg");

        using (var req = UnityWebRequest.Post(url, form))
        {
            req.timeout = 30;
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                var parsed = SimpleJsonParser.Parse(req.downloadHandler.text) as Dictionary<string, object>;
                List<object> objects = parsed != null && parsed.TryGetValue("objects", out object o) ? o as List<object> : new List<object>();
                onSuccess?.Invoke(objects ?? new List<object>());
            }
            else
            {
                onError?.Invoke($"{req.error}: {req.downloadHandler.text}");
            }
        }
    }
}
