using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;

public static class ARChatPanelSetup
{
    private static readonly Color Background = new Color(0.035f, 0.065f, 0.11f, 0.97f);
    private static readonly Color Surface = new Color(0.07f, 0.12f, 0.18f, 1f);
    private static readonly Color Accent = new Color(0.28f, 0.85f, 0.74f);
    private static Font font;

    [MenuItem("Tools/AR Vision/Polish Chat Panel")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play mode first.");
        var root = GameObject.Find("ChatUI");
        if (root == null) throw new System.InvalidOperationException("Open MainScene first.");
        Undo.RegisterFullObjectHierarchyUndo(root, "Polish AR chat");
        font = root.GetComponentInChildren<Text>(true).font;
        var controller = root.GetComponent<ChatUIController>();
        var rt = root.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(900, 720);
        rt.localScale = Vector3.one * 0.0012f;
        if (Camera.main != null)
        {
            rt.position = Camera.main.transform.position + Camera.main.transform.forward * 1.4f;
            rt.rotation = Camera.main.transform.rotation;
            root.GetComponent<Canvas>().worldCamera = Camera.main;
        }
        var bg = root.GetComponent<Image>() ?? Undo.AddComponent<Image>(root);
        bg.color = Background;
        bg.raycastTarget = true;
        Label(root.transform, "PanelTitle", "AR Vision Assistant", 24, 14, 620, 42, 30, Color.white);
        Label(root.transform, "PanelSubtitle", "Ask about your view or extract text from a poster", 24, 58, 850, 30, 20, new Color(.64f,.75f,.83f));
        var history = root.transform.Find("HistoryPanel");
        Place(history.GetComponent<RectTransform>(), 24, 108, 852, 390);
        history.GetComponent<Image>().color = Surface;
        var scroll = history.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.scrollSensitivity = 35;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        var viewport = history.Find("Viewport").GetComponent<RectTransform>();
        viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(16,16); viewport.offsetMax = new Vector2(-16,-16);
        var content = viewport.Find("Content").GetComponent<RectTransform>();
        content.anchorMin = new Vector2(0,1); content.anchorMax = Vector2.one;
        content.pivot = new Vector2(.5f,1); content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0,0);
        var txt = content.GetComponent<Text>();
        txt.fontSize = 24; txt.color = new Color(.9f,.94f,.98f); txt.supportRichText = true;
        txt.alignment = TextAnchor.UpperLeft;
        txt.horizontalOverflow = HorizontalWrapMode.Wrap;
        txt.verticalOverflow = VerticalWrapMode.Overflow;
        txt.lineSpacing = 1.15f;
        txt.text = "";
        var fitter = content.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content; scroll.viewport = viewport;
        var input = root.transform.Find("QuestionInput").GetComponent<InputField>();
        Place(input.GetComponent<RectTransform>(),24, 516, 686, 62);
        input.GetComponent<Image>().color = Surface;
        input.characterLimit = 1500;
        input.textComponent.fontSize = 24;
        input.textComponent.color = Color.white;
        input.textComponent.supportRichText = false;
        var placeholder = input.placeholder as Text;
        if (placeholder != null) { placeholder.text = "Ask about what you see..."; placeholder.fontSize = 23; placeholder.color = new Color(.6f,.7f,.78f); }
        var send = Button(root.transform, "SendButton", "Send", 724,516,152,62);
        var scan = Button(root.transform, "ScanTextButton", "Scan Text",24,594,210,52);
        var mic = Button(root.transform,"MicButton","Microphone",248,594,210,52);
        var clear = Button(root.transform,"ClearChatButton","Clear chat",666,594,210,52);
        send.image.color = Accent; scan.image.color = Accent;
        send.GetComponentInChildren<Text>().color = Background;
        scan.GetComponentInChildren<Text>().color = Background;
        Wire(send, controller.OnSendClicked);
        Wire(scan, controller.OnScanTextClicked);
        Wire(clear, controller.OnClearChatClicked);
        // The existing microphone keeps its Windows dictation binding.
        var status = Label(root.transform,"RequestStatus","Ready - ask about your view or scan text",24,660,852,32,18,new Color(.62f,.84f,.79f));
        var micStatus = root.transform.Find("StatusLabel");
        if (micStatus != null) { Place(micStatus.GetComponent<RectTransform>(),472,594,180,52); var t=micStatus.GetComponent<Text>();t.fontSize=17;t.text="Mic off";t.color=Color.white; }
        var so = new SerializedObject(controller);
        so.FindProperty("requestStatus").objectReferenceValue = status;
        so.FindProperty("sendButton").objectReferenceValue = send;
        so.FindProperty("scanTextButton").objectReferenceValue = scan;
        so.FindProperty("clearChatButton").objectReferenceValue = clear;
        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        Selection.activeGameObject=root;
        Debug.Log("AR chat panel updated and saved.");
    }

    private static void Wire(Button button, UnityEngine.Events.UnityAction action)
    {
        for (int i=button.onClick.GetPersistentEventCount()-1;i>=0;i--) UnityEventTools.RemovePersistentListener(button.onClick,i);
        UnityEventTools.AddPersistentListener(button.onClick,action);
    }
    private static void Place(RectTransform rt,float x,float y,float width,float height)
    {
        rt.anchorMin=rt.anchorMax=new Vector2(0,1);
        rt.pivot=new Vector2(0,1); rt.anchoredPosition=new Vector2(x,-y); rt.sizeDelta=new Vector2(width,height);
    }
    private static Text Label(Transform parent,string name,string value,float x,float y,float w,float h,int size,Color color)
    {
        var child=parent.Find(name);
        if(child==null) { var go=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Text));Undo.RegisterCreatedObjectUndo(go,"Create label");go.transform.SetParent(parent,false);child=go.transform; }
        var text=child.GetComponent<Text>();
        Place(child.GetComponent<RectTransform>(),x,y,w,h);
        text.font=font;text.fontSize=size;text.text=value;text.color=color;text.raycastTarget=false;text.alignment=TextAnchor.MiddleLeft;
        return text;
    }
    private static Button Button(Transform parent,string name,string label,float x,float y,float w,float h)
    {
        var child=parent.Find(name);
        if(child==null) { var go=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image),typeof(Button));Undo.RegisterCreatedObjectUndo(go,"Create button");go.transform.SetParent(parent,false);child=go.transform; }
        Place(child.GetComponent<RectTransform>(),x,y,w,h);
        var button=child.GetComponent<Button>(); button.image.color=Surface;
        var colors=button.colors;colors.highlightedColor=new Color(.8f,.95f,1f);colors.pressedColor=new Color(.65f,.8f,.85f);colors.disabledColor=new Color(.35f,.4f,.45f);button.colors=colors;
        var existing=child.GetComponentInChildren<Text>();
        if(existing==null)existing=Label(child,"Label",label,0,0,w,h,23,Color.white);
        Place(existing.rectTransform,0,0,w,h);existing.text=label;existing.fontSize=23;existing.color=Color.white;existing.alignment=TextAnchor.MiddleCenter;
        return button;
    }
}
