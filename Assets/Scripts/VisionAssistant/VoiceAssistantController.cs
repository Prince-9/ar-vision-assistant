using UnityEngine;

/// <summary>
/// Full push-to-talk voice assistant loop:
///   button down  -> start recording
///   button up    -> stop recording -> /listen (speech-to-text)
///                 -> /ask (with current camera frame attached) -> LLM answer
///                 -> show answer on result panel + /speak it aloud
///
/// Wire OnTriggerPressed/OnTriggerReleased to your controller's input events.
/// </summary>
public class VoiceAssistantController : MonoBehaviour
{
    [SerializeField] private AudioRecorder audioRecorder;
    [SerializeField] private GatewaySpeechClient speechClient;
    [SerializeField] private GatewayVisionClient visionClient;
    [SerializeField] private FrameCapture frameCapture;
    [SerializeField] private ResultPanelController resultPanel;
    [SerializeField] private AudioSource audioSource;

    [Tooltip("If true, attaches the current camera frame to every question (needed for 'what am I looking at' style questions, but slower/heavier for plain questions). If false, only sends text.")]
    [SerializeField] private bool alwaysAttachImage = false;

    [SerializeField] private Transform anchorOverride;

    private bool isBusy = false;

    public void OnTriggerPressed()
    {
        if (isBusy) return;
        audioRecorder.StartRecording();
    }

    public void OnTriggerReleased()
    {
        if (isBusy || !audioRecorder.IsRecording) return;

        byte[] wav = audioRecorder.StopRecordingAndGetWav();
        if (wav == null)
        {
            Debug.LogWarning("No audio captured - nothing to send.");
            return;
        }

        isBusy = true;
        speechClient.Listen(
            wav,
            onSuccess: OnTranscribed,
            onError: (err) =>
            {
                Debug.LogError("STT failed: " + err);
                isBusy = false;
            }
        );
    }

    private void OnTranscribed(string questionText)
    {
        if (string.IsNullOrWhiteSpace(questionText))
        {
            Debug.LogWarning("Empty transcription, nothing to ask.");
            isBusy = false;
            return;
        }

        Debug.Log("Heard: " + questionText);

        byte[] imageJpg = alwaysAttachImage ? frameCapture.CaptureFrameAsJpg() : null;

        visionClient.Ask(
            questionText,
            imageJpg,
            onSuccess: OnAnswerReceived,
            onError: (err) =>
            {
                Debug.LogError("LLM ask failed: " + err);
                ShowAndSpeak("Sorry, I couldn't reach the assistant.");
                isBusy = false;
            }
        );
    }

    private void OnAnswerReceived(string answer)
    {
        ShowAndSpeak(answer);
    }

    private void ShowAndSpeak(string text)
    {
        Vector3 anchorPos = anchorOverride != null ? anchorOverride.position : Camera.main.transform.position + Camera.main.transform.forward * 0.5f;
        Quaternion anchorRot = anchorOverride != null ? anchorOverride.rotation : Camera.main.transform.rotation;

        // Wrap as a minimal fake "content" envelope so ResultPanelController's
        // existing unwrap logic (built for the Claude API shape) still works
        // unchanged with plain gateway text.
        string escaped = text.Replace("\\", "\\\\").Replace("\"", "\\\"");
        string fakeEnvelope = "{\"content\":[{\"text\":\"{\\\"answer\\\": \\\"" + escaped + "\\\"}\"}]}";
        resultPanel.ShowResult(fakeEnvelope, anchorPos, anchorRot);

        speechClient.Speak(
            text,
            onSuccess: (clip) =>
            {
                audioSource.clip = clip;
                audioSource.Play();
                isBusy = false;
            },
            onError: (err) =>
            {
                Debug.LogError("TTS failed: " + err);
                isBusy = false;
            }
        );
    }
}
