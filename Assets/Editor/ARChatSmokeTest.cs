using UnityEditor;

/// <summary>The chat integration menu now verifies the session-memory flow.</summary>
public static class ARChatSmokeTest
{
    [MenuItem("Tools/AR Vision/Test Chat Integration")]
    public static void Run(){ ARSessionMemoryCheck.Run(); }
}
