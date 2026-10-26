using UnityEngine;

/// <summary>
/// Compatibility entry point for the original extraction button and input bindings.
/// Uses the same local OCR flow, busy guard and chat memory as Scan Text.
/// </summary>
public class ExtractionTrigger : MonoBehaviour
{
    [SerializeField] private ChatUIController chatUIController;

    public void TriggerExtraction()
    {
        if (chatUIController == null)
            chatUIController = FindFirstObjectByType<ChatUIController>();
        if (chatUIController == null)
        {
            Debug.LogError("Text extraction needs the ChatUIController in the active scene.");
            return;
        }
        chatUIController.OnScanTextClicked();
    }
}
