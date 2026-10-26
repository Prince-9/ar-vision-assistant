using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Talks to the gateway's /listen (speech-to-text) and /speak (text-to-speech)
/// endpoints.
/// </summary>
public class GatewaySpeechClient : MonoBehaviour
{
    /// <summary>POSTs WAV audio bytes to /listen, returns transcribed text.</summary>
    public void Listen(byte[] wavBytes, Action<string> onSuccess, Action<string> onError)
    {
        StartCoroutine(ListenCoroutine(wavBytes, onSuccess, onError));
    }

    private IEnumerator ListenCoroutine(byte[] wavBytes, Action<string> onSuccess, Action<string> onError)
    {
        string url = GatewayConfig.Instance.BaseUrl + "/listen";

        WWWForm form = new WWWForm();
        form.AddBinaryData("audio", wavBytes, "recording.wav", "audio/wav");

        using (var req = UnityWebRequest.Post(url, form))
        {
            req.timeout = 180; // First Whisper request may load the model.
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                string text;
                try
                {
                    var parsed = SimpleJsonParser.Parse(req.downloadHandler.text) as System.Collections.Generic.Dictionary<string, object>;
                    if (parsed == null || !parsed.TryGetValue("text", out object t) || !(t is string))
                        throw new FormatException("Missing transcription text.");
                    text = (string)t;
                }
                catch (Exception e) { onError?.Invoke("Invalid transcription: " + e.Message); yield break; }
                onSuccess?.Invoke(text ?? "");
            }
            else
            {
                onError?.Invoke($"{req.error}: {req.downloadHandler.text}");
            }
        }
    }

    /// <summary>POSTs text to /speak, returns a playable AudioClip.</summary>
    public void Speak(string text, Action<AudioClip> onSuccess, Action<string> onError)
    {
        StartCoroutine(SpeakCoroutine(text, onSuccess, onError));
    }

    private IEnumerator SpeakCoroutine(string text, Action<AudioClip> onSuccess, Action<string> onError)
    {
        string url = GatewayConfig.Instance.BaseUrl + "/speak";

        WWWForm form = new WWWForm();
        form.AddField("text", text);

        using (var req = UnityWebRequest.Post(url, form))
        {
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = 30;
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                AudioClip clip = WavUtility.FromWav(req.downloadHandler.data);
                if (clip != null) onSuccess?.Invoke(clip);
                else onError?.Invoke("Failed to parse returned audio.");
            }
            else
            {
                onError?.Invoke($"{req.error}");
            }
        }
    }
}
