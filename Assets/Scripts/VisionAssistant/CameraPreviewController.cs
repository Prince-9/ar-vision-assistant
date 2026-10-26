using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays the live camera feed FrameCapture is using in a UI RawImage,
/// so you can see exactly what the model is looking at. Waits for the
/// texture to become available since the webcam starts asynchronously.
/// </summary>
public class CameraPreviewController : MonoBehaviour
{
    [SerializeField] private FrameCapture frameCapture;
    [SerializeField] private RawImage previewImage;

    private bool textureAssigned = false;

    private void Update()
    {
        if (textureAssigned || frameCapture == null || previewImage == null) return;

        Texture tex = frameCapture.ActiveCameraTexture;
        var webcam = tex as WebCamTexture;
        if (tex != null && (webcam == null || webcam.isPlaying) && tex.width > 16)
        {
            previewImage.texture = tex;
            textureAssigned = true;
        }
    }
}
