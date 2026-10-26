using UnityEngine;
using UnityEngine.Android;
using UnityEngine.UI;

/// <summary>Click to record with the laptop mic, click again to transcribe locally.</summary>
[RequireComponent(typeof(AudioRecorder), typeof(GatewaySpeechClient))]
public class VoiceInputController : MonoBehaviour
{
    [SerializeField] private ChatUIController chatUIController;
    [SerializeField] private Text statusLabel;
    [SerializeField] private Button microphoneButton;
    private AudioRecorder recorder;
    private GatewaySpeechClient speech;
    private Text buttonLabel;
    private bool transcribing;
    private int requestVersion;

    private void Awake()
    {
        recorder = GetComponent<AudioRecorder>();
        speech = GetComponent<GatewaySpeechClient>();
        if (microphoneButton == null)
        {
            var child = transform.Find("MicButton");
            if (child != null) microphoneButton = child.GetComponent<Button>();
        }
        if (microphoneButton != null) buttonLabel = microphoneButton.GetComponentInChildren<Text>();
        SetStatus(Application.platform == RuntimePlatform.Android ? "Quest mic ready" : "Laptop mic ready");
    }

    public void ToggleListening()
    {
        if (transcribing) return;
        if (recorder.IsRecording) { FinishRecording(); return; }
        if (chatUIController == null || chatUIController.IsBusy)
        {
            SetStatus("Wait for the answer");
            return;
        }
        if (Application.platform == RuntimePlatform.Android && !Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Permission.RequestUserPermission(Permission.Microphone);
            SetStatus("Allow microphone access in Quest, then tap Microphone again");
            return;
        }
        recorder.StartRecording();
        if (!recorder.IsRecording) { SetStatus(recorder.LastError ?? "Could not start microphone"); return; }
        if (buttonLabel != null) buttonLabel.text = "Send voice question";
        SetStatus("Listening - tap Send voice question when done");
    }

    private void Update()
    {
        if (recorder != null && recorder.IsRecording)
        {
            if (recorder.Duration >= 29f) FinishRecording();
            else SetStatus("Listening " + Mathf.FloorToInt(recorder.Duration) + "s - tap to send");
        }
    }

    private void FinishRecording()
    {
        var wav = recorder.StopRecordingAndGetWav();
        if (buttonLabel != null) buttonLabel.text = "Microphone";
        if (wav == null) { SetStatus(recorder.LastError ?? "No audio captured"); return; }
        if (GatewayConfig.Instance == null) { SetStatus("Gateway not configured"); return; }
        transcribing = true;
        int version = ++requestVersion;
        if (microphoneButton != null) microphoneButton.interactable = false;
        SetStatus("Understanding your voice...");
        speech.Listen(wav, text =>
        {
            if (version != requestVersion || !isActiveAndEnabled) return;
            Complete();
            if (string.IsNullOrWhiteSpace(text)) { SetStatus("No speech heard - try again"); return; }
            StartCoroutine(SubmitWhenReady(text, version));
        }, error =>
        {
            if (version != requestVersion || !isActiveAndEnabled) return;
            Complete();
            SetStatus("Voice unavailable - try again");
            Debug.LogWarning("Local speech transcription failed: " + error);
        });
    }

    private System.Collections.IEnumerator SubmitWhenReady(string text, int version)
    {
        transcribing = true;
        if (microphoneButton != null) microphoneButton.interactable = false;
        SetStatus("Sending your question...");
        while (chatUIController != null && chatUIController.IsBusy)
            yield return null;
        if (version != requestVersion || chatUIController == null) yield break;
        chatUIController.SubmitSpokenQuestion(text);
        Complete();
        SetStatus("Voice sent - assistant is answering");
    }

    private void Complete()
    {
        transcribing = false;
        if (microphoneButton != null) microphoneButton.interactable = true;
    }
    private void SetStatus(string text) { if (statusLabel != null) statusLabel.text = text; }
    private void OnDisable()
    {
        requestVersion++;
        if (recorder != null) recorder.CancelRecording();
        if (speech != null) speech.StopAllCoroutines();
        Complete();
        if (buttonLabel != null) buttonLabel.text = "Microphone";
    }
}
