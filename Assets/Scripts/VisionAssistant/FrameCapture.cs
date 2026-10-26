using UnityEngine;
using Meta.XR;
using UnityEngine.XR;

/// <summary>
/// Grabs a still frame from the camera as JPEG bytes.
/// Quest 3: requires Meta's Passthrough Camera Access API package
/// (com.meta.xr.sdk.passthroughcamera or similar, check current Meta docs) -
/// assign webCamTextureManager once that's installed.
/// HoloLens 2: replace WebCamTextureManager reference with
/// UnityEngine.Windows.WebCam.PhotoCapture instead - same public method signature.
///
/// Editor/PC testing: if webCamTextureManager is left unassigned, this
/// automatically falls back to your PC's regular webcam via Unity's
/// WebCamTexture API, so you can test the whole pipeline in Play mode
/// before any headset-specific package is installed.
/// </summary>
public class FrameCapture : MonoBehaviour
{
    [Tooltip("Assign the WebCamTextureManager component from Meta's passthrough camera sample/package. Leave empty to use your PC's regular webcam for Editor testing.")]
    [SerializeField] private MonoBehaviour webCamTextureManager;

    [Tooltip("Exact Windows camera name. If this device disconnects, capture stops instead of switching to a different camera. Leave blank to use the first camera.")]
    [SerializeField] private string preferredCameraName = "Integrated Camera";

    private WebCamTexture editorWebCam;
    private PassthroughCameraAccess questCamera;

    private void Start()
    {
        // In Editor, use the selected PC webcam even during a Quest Link XR
        // session. Quest passthrough remains the headset's display background;
        // Android builds continue to capture directly from Quest cameras.
        if (Application.isEditor)
        {
            StartEditorWebcam();
        }
        else if (Application.platform == RuntimePlatform.Android && webCamTextureManager == null)
        {
            questCamera = FindFirstObjectByType<PassthroughCameraAccess>();
        }
    }

    private void StartEditorWebcam()
    {
        var devices = WebCamTexture.devices;
        if (devices.Length == 0)
        {
            Debug.LogError("No webcam devices found on this PC.");
            return;
        }

        string selected = devices[0].name;
        if (!string.IsNullOrWhiteSpace(preferredCameraName))
        {
            bool found = false;
            foreach (var device in devices)
            {
                if (device.name != preferredCameraName) continue;
                selected = device.name;
                found = true;
                break;
            }
            if (!found)
            {
                Debug.LogError("Selected camera is unavailable: " + preferredCameraName + ". Reconnect it and restart Play mode.");
                return;
            }
        }
        editorWebCam = new WebCamTexture(selected);
        editorWebCam.Play();
        Debug.Log("Editor webcam started: " + selected);
    }

    /// <summary>Exposes the live camera feed for UI preview purposes.</summary>
    public Texture ActiveCameraTexture => GetActiveCameraTexture();
    // Retained for existing Editor diagnostics that inspect the PC webcam.
    public WebCamTexture ActiveWebCamTexture => editorWebCam;

    public byte[] CaptureFrameAsJpg()
    {
        Texture source = GetActiveCameraTexture();
        var webCam = source as WebCamTexture;
        if (source == null || (webCam != null && !webCam.isPlaying) || source.width <= 16 || source.height <= 16)
        {
            Debug.LogWarning("Camera not ready.");
            return null;
        }

        Texture2D tex = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
        if (questCamera != null)
        {
            var colors = questCamera.GetColors();
            if (!colors.IsCreated)
            {
                Destroy(tex);
                return null;
            }
            tex.SetPixelData(colors, 0);
        }
        else if (webCam != null)
        {
            tex.SetPixels32(webCam.GetPixels32());
        }
        else
        {
            var previous = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }
        tex.Apply();

        // Downscale before sending - full-res frames are large and slow to
        // upload; ~1024px on the long edge is plenty for text extraction.
        Texture2D resized = Downscale(tex, 1024);
        byte[] jpg = resized.EncodeToJPG(85);

        Destroy(tex);
        if (resized != tex) Destroy(resized);
        return jpg;
    }

    private Texture GetActiveCameraTexture()
    {
        if (Application.isEditor && editorWebCam != null) return editorWebCam;

        bool useQuestCamera = Application.platform == RuntimePlatform.Android;
        if (questCamera == null && useQuestCamera)
            questCamera = FindFirstObjectByType<PassthroughCameraAccess>();
        if (questCamera != null) return questCamera.GetTexture();

        // Keep support for older Meta camera sample components.
        var property = webCamTextureManager?.GetType().GetProperty("WebCamTexture");
        return property?.GetValue(webCamTextureManager) as Texture;
    }

    private Texture2D Downscale(Texture2D source, int maxDimension)
    {
        float scale = (float)maxDimension / Mathf.Max(source.width, source.height);
        if (scale >= 1f) return source;

        int newWidth = Mathf.RoundToInt(source.width * scale);
        int newHeight = Mathf.RoundToInt(source.height * scale);

        var previous = RenderTexture.active;
        RenderTexture rt = RenderTexture.GetTemporary(newWidth, newHeight);
        Graphics.Blit(source, rt);

        Texture2D result = new Texture2D(newWidth, newHeight, TextureFormat.RGB24, false);
        RenderTexture.active = rt;
        result.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
        result.Apply();

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        return result;
    }

    private void OnDestroy()
    {
        if (editorWebCam != null && editorWebCam.isPlaying)
        {
            editorWebCam.Stop();
        }
    }
}
