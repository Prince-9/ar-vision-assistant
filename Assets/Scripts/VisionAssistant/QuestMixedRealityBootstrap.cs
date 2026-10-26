using System.Collections;
using Meta.XR;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>Turns the Quest build into a tracked mixed-reality voice assistant.</summary>
[DefaultExecutionOrder(-1000)]
public class QuestMixedRealityBootstrap : MonoBehaviour
{
    private void Awake()
    {
        var camera = Camera.main;
        if (camera == null) return;

        bool isQuestApp = Application.platform == RuntimePlatform.Android;
        bool isEditorXrSession = Application.isEditor && XRSettings.isDeviceActive;

        // Keep the world-space chat aligned with the view direction in both
        // the Unity Game view and the Quest build. A desktop-only Game view
        // uses the editor mirror correction; an XR session uses native UI.
        if (Application.isEditor && !isEditorXrSession)
        {
            FaceChatUi(camera);
            FixEditorCanvasMirror();
            return;
        }

        if (!isQuestApp && !isEditorXrSession) return;

        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        camera.transform.localPosition = Vector3.zero;
        camera.transform.localRotation = Quaternion.identity;
        if (camera.GetComponent<QuestHeadPoseDriver>() == null)
            camera.gameObject.AddComponent<QuestHeadPoseDriver>();

        EnablePassthrough();
        if (isQuestApp) EnableCameraCapture();
        StartCoroutine(PrepareWorldUi(camera));

        if (isQuestApp && !Permission.HasUserAuthorizedPermission("horizonos.permission.HEADSET_CAMERA"))
            Permission.RequestUserPermission("horizonos.permission.HEADSET_CAMERA");
    }

    private static void EnablePassthrough()
    {
        var managerObject = new GameObject("Quest XR Runtime");
        managerObject.SetActive(false);
        var manager = managerObject.AddComponent<OVRManager>();
        manager.isInsightPassthroughEnabled = true;

        managerObject.SetActive(true);

        var passthroughObject = new GameObject("Quest Passthrough Underlay");
        passthroughObject.SetActive(false);
        var layer = passthroughObject.AddComponent<OVRPassthroughLayer>();
        layer.hidden = false;
        passthroughObject.SetActive(true);
    }

    private static void EnableCameraCapture()
    {
        if (FindFirstObjectByType<PassthroughCameraAccess>() != null) return;
        var cameraObject = new GameObject("Quest Passthrough Camera Access");
        cameraObject.SetActive(false);
        var access = cameraObject.AddComponent<PassthroughCameraAccess>();
        access.CameraPosition = PassthroughCameraAccess.CameraPositionType.Left;
        access.RequestedResolution = new Vector2Int(1280, 960);
        cameraObject.SetActive(true);
    }

    private IEnumerator PrepareWorldUi(Camera camera)
    {
        // Allow OpenXR to deliver the first tracked head pose before placing
        // the conversation panel in front of the user.
        yield return null;
        yield return null;
        FaceChatUi(camera);

        var chat = FindFirstObjectByType<ChatUIController>();
        if (chat != null)
        {
            var canvas = chat.GetComponent<Canvas>();
            if (canvas != null) canvas.worldCamera = camera;
        }

        var preview = GameObject.Find("CameraPreviewUI");
        if (preview != null) preview.SetActive(false);
        var debug = GameObject.Find("DebugUI");
        if (debug != null) debug.SetActive(false);

        var pointer = camera.GetComponent<QuestControllerUiPointer>();
        if (pointer == null) camera.gameObject.AddComponent<QuestControllerUiPointer>();
    }

    private static void FaceChatUi(Camera camera)
    {
        var chat = FindFirstObjectByType<ChatUIController>();
        if (chat != null)
        {
            var canvas = chat.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.worldCamera = camera;
                if (Application.platform == RuntimePlatform.Android)
                    canvas.transform.position = camera.transform.position + camera.transform.forward * 1.35f;
                canvas.transform.rotation = camera.transform.rotation;
            }
        }
    }

    private static void FixEditorCanvasMirror()
    {
        var chat = FindFirstObjectByType<ChatUIController>();
        var canvas = chat != null ? chat.GetComponent<Canvas>() : null;
        if (canvas == null || canvas.transform.Find("Editor Mirror Correction") != null) return;

        var raycaster = canvas.GetComponent<GraphicRaycaster>();
        if (raycaster != null) raycaster.ignoreReversedGraphics = false;

        // Unity's Game view can render this world-space Canvas from its back
        // side in Editor play mode. Reflecting its content once restores both
        // readable glyphs and the intended left/right button layout, without
        // changing the Canvas plane used by GraphicRaycaster.
        var correction = new GameObject("Editor Mirror Correction", typeof(RectTransform));
        var correctionRect = (RectTransform)correction.transform;
        correctionRect.SetParent(canvas.transform, false);
        correctionRect.anchorMin = Vector2.zero;
        correctionRect.anchorMax = Vector2.one;
        correctionRect.pivot = new Vector2(0.5f, 0.5f);
        correctionRect.offsetMin = Vector2.zero;
        correctionRect.offsetMax = Vector2.zero;
        correctionRect.localScale = new Vector3(-1f, 1f, 1f);

        var content = new System.Collections.Generic.List<Transform>();
        for (int i = 0; i < canvas.transform.childCount; i++)
        {
            var child = canvas.transform.GetChild(i);
            if (child != correctionRect) content.Add(child);
        }
        foreach (var child in content) child.SetParent(correctionRect, false);
        correctionRect.SetAsFirstSibling();
    }
}

/// <summary>Applies the OpenXR head pose to the scene's main camera.</summary>
public class QuestHeadPoseDriver : MonoBehaviour
{
    private InputDevice head;

    private void LateUpdate()
    {
        if (!head.isValid) head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        if (head.isValid)
        {
            if (head.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position))
                transform.localPosition = position;
            if (head.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation))
                transform.localRotation = rotation;
        }
    }
}

/// <summary>Shows a gaze ray and uses either Touch controller trigger to click world-space UI.</summary>
public class QuestControllerUiPointer : MonoBehaviour
{
    private readonly System.Collections.Generic.List<RaycastResult> hits = new System.Collections.Generic.List<RaycastResult>();
    private readonly System.Collections.Generic.List<RaycastResult> raycasterHits = new System.Collections.Generic.List<RaycastResult>();
    private PointerEventData pointerData;
    private GraphicRaycaster[] raycasters;
    private GameObject reticle;
    private LineRenderer laser;
    private bool wasPressed;

    private void Start()
    {
        raycasters = FindObjectsByType<GraphicRaycaster>(FindObjectsSortMode.None);
        pointerData = new PointerEventData(EventSystem.current);
        reticle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        reticle.name = "Quest UI Gaze Reticle";
        reticle.transform.localScale = Vector3.one * 0.012f;
        var collider = reticle.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        var renderer = reticle.GetComponent<Renderer>();
        if (renderer != null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader != null) renderer.material = new Material(shader) { color = new Color(0.25f, 1f, 0.8f, 1f) };
        }
        reticle.SetActive(false);

        var laserObject = new GameObject("Quest Controller UI Laser");
        laserObject.transform.SetParent(transform, false);
        laser = laserObject.AddComponent<LineRenderer>();
        laser.useWorldSpace = true;
        laser.positionCount = 2;
        laser.startWidth = 0.003f;
        laser.endWidth = 0.0015f;
        laser.enabled = false;
        var lineShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (lineShader == null) lineShader = Shader.Find("Sprites/Default");
        if (lineShader != null)
        {
            laser.material = new Material(lineShader);
            laser.startColor = new Color(0.25f, 1f, 0.8f, 0.9f);
            laser.endColor = new Color(0.25f, 1f, 0.8f, 0.2f);
        }
    }

    private void Update()
    {
        if (EventSystem.current == null) return;
        raycasters ??= FindObjectsByType<GraphicRaycaster>(FindObjectsSortMode.None);
        bool controllerRay = TryGetControllerRay(XRNode.RightHand, out Ray ray);
        if (!controllerRay) controllerRay = TryGetControllerRay(XRNode.LeftHand, out ray);
        if (!controllerRay) ray = new Ray(transform.position, transform.forward);

        bool hasTarget = TryRaycastUi(ray, out GameObject target, out Vector3 targetPosition,
            out RaycastResult targetHit, out Vector2 targetScreenPosition);
        if (reticle != null)
        {
            reticle.SetActive(hasTarget);
            if (hasTarget)
            {
                reticle.transform.position = targetPosition - ray.direction * 0.008f;
                reticle.transform.rotation = Quaternion.LookRotation(ray.direction);
            }
        }
        if (laser != null)
        {
            laser.enabled = controllerRay;
            if (controllerRay)
            {
                laser.SetPosition(0, ray.origin);
                laser.SetPosition(1, hasTarget ? targetPosition : ray.origin + ray.direction * 5f);
            }
        }

        bool pressed = ReadTrigger(XRNode.RightHand) || ReadTrigger(XRNode.LeftHand);
        // OVRInput is a fallback for Meta runtimes that don't expose the
        // OpenXR trigger as a Unity CommonUsages feature on this controller profile.
        pressed |= OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) ||
                   OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch);
        if (pressed && !wasPressed && hasTarget)
        {
            pointerData.position = targetScreenPosition;
            pointerData.delta = Vector2.zero;
            pointerData.pointerCurrentRaycast = targetHit;
            // Populate the same pointer state Unity's UI input module normally
            // provides. InputField needs a left-button click with a valid hit
            // position to take focus and open the Quest system keyboard.
            pointerData.button = PointerEventData.InputButton.Left;
            pointerData.pointerId = -1;
            pointerData.pressPosition = targetScreenPosition;
            pointerData.pointerPressRaycast = pointerData.pointerCurrentRaycast;
            pointerData.pointerEnter = target;
            pointerData.eligibleForClick = true;
            pointerData.pointerPress = target;
            pointerData.pointerClick = target;
            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerClickHandler);
            ExecuteEvents.ExecuteHierarchy(target, pointerData, ExecuteEvents.pointerUpHandler);
            if (target.GetComponentInParent<Selectable>() != null)
                EventSystem.current.SetSelectedGameObject(target);
            pointerData.eligibleForClick = false;
            pointerData.pointerPress = null;
            pointerData.pointerClick = null;
        }
        wasPressed = pressed;
    }

    private bool TryRaycastUi(Ray ray, out GameObject target, out Vector3 targetPosition,
        out RaycastResult targetHit, out Vector2 targetScreenPosition)
    {
        target = null;
        targetPosition = default;
        targetHit = default;
        targetScreenPosition = default;
        float nearest = float.MaxValue;
        foreach (var raycaster in raycasters)
        {
            if (raycaster == null || !raycaster.isActiveAndEnabled) continue;
            var canvas = raycaster.GetComponent<Canvas>();
            if (canvas == null || canvas.renderMode != RenderMode.WorldSpace) continue;
            var eventCamera = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
            if (eventCamera == null) continue;

            var plane = new Plane(canvas.transform.forward, canvas.transform.position);
            if (!plane.Raycast(ray, out float distance) || distance < 0f || distance > 8f) continue;
            Vector3 planeHit = ray.GetPoint(distance);
            pointerData.Reset();
            pointerData.position = RectTransformUtility.WorldToScreenPoint(eventCamera, planeHit);
            raycasterHits.Clear();
            raycaster.Raycast(pointerData, raycasterHits);
            foreach (var hit in raycasterHits)
            {
                float hitDistance = Vector3.Distance(ray.origin, hit.worldPosition);
                if (hitDistance >= nearest) continue;
                nearest = hitDistance;
                target = hit.gameObject;
                targetPosition = hit.worldPosition;
                targetHit = hit;
                targetScreenPosition = pointerData.position;
            }
        }
        return target != null;
    }

    private bool TryGetControllerRay(XRNode node, out Ray ray)
    {
        ray = default;
        var device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid ||
            !device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 controllerPosition) ||
            !device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion controllerRotation)) return false;

        var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        if (!head.isValid ||
            !head.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 headPosition) ||
            !head.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion headRotation)) return false;

        Quaternion trackingRotation = transform.rotation * Quaternion.Inverse(headRotation);
        Vector3 trackingPosition = transform.position - trackingRotation * headPosition;
        Vector3 worldPosition = trackingPosition + trackingRotation * controllerPosition;
        Quaternion worldRotation = trackingRotation * controllerRotation;
        ray = new Ray(worldPosition, worldRotation * Vector3.forward);
        return true;
    }

    private static bool ReadTrigger(XRNode node)
    {
        var device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid) return false;
        if (device.TryGetFeatureValue(CommonUsages.triggerButton, out bool pressed) && pressed) return true;
        return device.TryGetFeatureValue(CommonUsages.trigger, out float value) && value > 0.6f;
    }
}
