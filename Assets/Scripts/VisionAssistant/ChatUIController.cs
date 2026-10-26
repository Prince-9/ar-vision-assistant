using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

public class ChatUIController : MonoBehaviour
{
    [SerializeField] private FrameCapture frameCapture;
    [SerializeField] private GatewayVisionClient gatewayVisionClient;
    [SerializeField] private InputField questionInput;
    [SerializeField] private Text historyText;
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private Text requestStatus;
    [SerializeField] private Button sendButton;
    [SerializeField] private Button scanTextButton;
    [SerializeField] private Button clearChatButton;
    [SerializeField] private GatewaySpeechClient speechClient;
    [SerializeField] private AudioSource speechOutput;

    [Serializable] public class Message
    {
        public string role;
        public string content;
        public Message(string role, string content) { this.role = role; this.content = content; }
    }
    [Serializable] private class History { public List<Message> messages = new List<Message>(); }
    private readonly History memory = new History();
    private string sessionId = Guid.NewGuid().ToString();
    private bool isBusy;
    private bool speakNextResponse;
    public bool IsBusy => isBusy;

    public void SetRecognizedQuestion(string text)
    {
        if (questionInput != null) questionInput.text = Limit(text);
    }
    private int displayedEntries;
    private const int MaxMessageChars = 1500;

    private void Start()
    {
        if (speechClient == null) speechClient = GetComponent<GatewaySpeechClient>();
        if (speechOutput == null) speechOutput = GetComponent<AudioSource>();
        if (speechOutput == null) speechOutput = gameObject.AddComponent<AudioSource>();
        speechOutput.spatialBlend = 0f;
        if (historyText != null && string.IsNullOrWhiteSpace(historyText.text))
            ShowWelcome();
        SetBusy(false, "Ready - ask about your view or scan text");
    }

    public void OnSendClicked()
    {
        if (isBusy || questionInput == null || string.IsNullOrWhiteSpace(questionInput.text)) return;
        if (Submit(questionInput.text.Trim())) questionInput.text = "";
    }

    public void SubmitSpokenQuestion(string question)
    {
        if (!isBusy && !string.IsNullOrWhiteSpace(question))
        {
            speakNextResponse = true;
            Submit(question.Trim());
        }
    }

    private byte[] Capture(bool required = true)
    {
        if (gatewayVisionClient == null || GatewayConfig.Instance == null)
        {
            Fail("The assistant connection is not configured.");
            return null;
        }
        try
        {
            var frame = frameCapture != null ? frameCapture.CaptureFrameAsJpg() : null;
            if (frame == null || frame.Length == 0)
            {
                if (required) Fail("Camera is not ready. Check the preview and try again.");
                return null;
            }
            return frame;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            if (required) Fail("Could not capture the camera. Please try again.");
            return null;
        }
    }

    private bool Submit(string question)
    {
        bool hasMemory = gatewayVisionClient != null && gatewayVisionClient.RememberedViewCount > 0;
        byte[] frame = Capture(!hasMemory);
        if (frame == null && !hasMemory)
        {
            speakNextResponse = false;
            return false;
        }
        question = Limit(question);
        bool speakThisResponse = speakNextResponse;
        speakNextResponse = false;
        AppendLine("You", question, "#8ECBFF");
        SetBusy(true, "Thinking about your question...");
        try
        {
            gatewayVisionClient.Ask(question, frame,
                answer =>
                {
                    AppendLine("Assistant", answer, "#7CE5CD");
                    Remember(question, answer);
                    if (speakThisResponse) SpeakAnswer(answer);
                    string source = gatewayVisionClient.LastSource == "memory" ? "Used an earlier view" :
                        gatewayVisionClient.LastSource == "clarification" ? "Choose a saved view by number" : "Ready";
                    SetBusy(false, source + " - " + gatewayVisionClient.RememberedViewCount + " saved views this session");
                },
                Fail,
                null, sessionId);
        }
        catch (Exception e) { Debug.LogException(e); Fail("Could not send the question. Please try again."); }
        return true;
    }

    private void SpeakAnswer(string answer)
    {
        if (speechClient == null || speechOutput == null) return;
        speechClient.Speak(answer, clip =>
        {
            if (clip == null) return;
            speechOutput.PlayOneShot(clip);
            Destroy(clip, clip.length + 0.25f);
        }, error => Debug.LogWarning("Assistant voice playback failed: " + error));
    }

    public void OnScanTextClicked()
    {
        if (isBusy) return;
        byte[] frame = Capture();
        if (frame == null) return;
        AppendLine("You", "Scan the text in this view.", "#8ECBFF");
        SetBusy(true, "Reading text from the camera...");
        try
        {
            gatewayVisionClient.RunOcr(frame,
                text =>
                {
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        AppendLine("Scan text", "No readable text found. Move closer, improve the lighting, and try again.", "#F2CF87");
                        SetBusy(false, "No text found - try a clearer view");
                        return;
                    }
                    AppendLine("Extracted text", text, "#7CE5CD");
                    Remember("Scan the text in this view.", "Text extracted from the previous image: " + text);
                    SetBusy(false, "Scan saved - ask about this text later (" + gatewayVisionClient.RememberedViewCount + " views)");
                }, Fail, sessionId);
        }
        catch (Exception e) { Debug.LogException(e); Fail("Could not scan text. Please try again."); }
    }

    public void OnClearChatClicked()
    {
        if (isBusy) return;
        SetBusy(true, "Clearing conversation and saved images...");
        gatewayVisionClient.ClearSession(sessionId, () =>
        {
            sessionId = Guid.NewGuid().ToString();
            memory.messages.Clear();
            displayedEntries = 0;
            ShowWelcome();
            SetBusy(false, "Conversation and saved images cleared");
        }, error => Fail("Could not clear saved memory. Check the gateway and retry: " + error));
    }

    private void Remember(string question, string answer)
    {
        memory.messages.Add(new Message("user", Limit(question)));
        memory.messages.Add(new Message("assistant", Limit(answer)));
        while (memory.messages.Count > 6 || MemoryLength() > 5000)
            memory.messages.RemoveRange(0, Math.Min(2, memory.messages.Count));
    }

    private int MemoryLength()
    {
        int length = 0;
        foreach (var message in memory.messages) length += message.content.Length;
        return length;
    }

    private static string Limit(string text)
    {
        text = text ?? "";
        return text.Length <= MaxMessageChars ? text : text.Substring(0, MaxMessageChars);
    }

    private void Fail(string error)
    {
        AppendLine("Could not complete request", error, "#FFB4AE");
        SetBusy(false, "Please check the message and try again");
    }

    private void SetBusy(bool busy, string status)
    {
        isBusy = busy;
        if (sendButton != null) sendButton.interactable = !busy;
        if (scanTextButton != null) scanTextButton.interactable = !busy;
        if (clearChatButton != null) clearChatButton.interactable = !busy;
        if (requestStatus != null) requestStatus.text = status;
    }

    private void ShowWelcome()
    {
        bool isHeadsetSession = Application.platform == RuntimePlatform.Android ||
                                (Application.isEditor && XRSettings.isDeviceActive);
        if (historyText != null)
            historyText.text = isHeadsetSession
                ? "<color=#7CE5CD><b>AR Vision Assistant</b></color>\n\nLook at an object and use Microphone to ask about it. I will answer aloud and remember what you showed me.\n\nTo type, point a controller at the message box and squeeze the trigger. The Quest keyboard should open. Aim at a button and squeeze the trigger to select it."
                : "<color=#7CE5CD><b>AR Vision Assistant</b></color>\n\nShow an object and ask about it. Later, ask about that book or poster even when it is out of view.\n\nUp to 12 views stay in session memory. Clear chat removes the conversation and saved images.";
    }

    private void OnDestroy()
    {
        if (gatewayVisionClient != null) gatewayVisionClient.ReleaseSession(sessionId);
    }

    private static string SafeText(string text)
    {
        // Legacy Unity Text renders rich text. Keep model/user input from becoming markup.
        return (text ?? "").Replace("<", "‹").Replace(">", "›");
    }

    private void AppendLine(string label, string text, string color)
    {
        if (historyText == null) return;
        if (displayedEntries == 0) historyText.text = "";
        if (historyText.text.Length > 24000) { historyText.text = ""; displayedEntries = 0; }
        historyText.text += (displayedEntries++ == 0 ? "" : "\n\n") +
            "<color=" + color + "><b>" + label + "</b></color>\n" + SafeText(text);
        Canvas.ForceUpdateCanvases();
        if (scrollRect != null) scrollRect.verticalNormalizedPosition = 0f;
    }
}

