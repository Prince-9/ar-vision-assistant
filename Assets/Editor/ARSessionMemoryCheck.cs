using System;
using System.IO;
using UnityEngine;
using UnityEditor;

[InitializeOnLoad]
public static class ARSessionMemoryCheck
{
    private const string Flag="ARSessionMemoryCheck.Run";
    private const string Report="C:/Users/kushw/local-gateway/tests/unity-memory-check.txt";
    private static bool started;
    private static double deadline;
    static ARSessionMemoryCheck(){EditorApplication.update+=Tick;}
    [MenuItem("Tools/AR Vision/Test Session Memory")]
    public static void Run()
    {
        SessionState.SetBool(Flag,true);started=false;
        File.WriteAllText(Report,"RUNNING");
        EditorApplication.isPlaying=true;
    }
    private static void Done(string result)
    {
        SessionState.SetBool(Flag,false);
        File.WriteAllText(Report,result);
        Debug.Log(result);
    }
    private static void Tick()
    {
        if(!SessionState.GetBool(Flag,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        if(started){if(EditorApplication.timeSinceStartup>deadline)Done("FAIL: session test timed out");return;}
        var client=UnityEngine.Object.FindFirstObjectByType<GatewayVisionClient>();
        var ui=UnityEngine.Object.FindFirstObjectByType<ChatUIController>();
        if(client==null||ui==null)return;
        started=true;deadline=EditorApplication.timeSinceStartup+240;
        var sid=Guid.NewGuid().ToString();
        var book=File.ReadAllBytes("C:/Users/kushw/local-gateway/tests/book-memory.png");
        var blank=File.ReadAllBytes("C:/Users/kushw/local-gateway/tests/empty-view.png");
        Action<string> fail=error=>{client.ClearSession(sid,()=>{},e=>{});Done("FAIL: "+error);};
        client.Ask("What is this book? Read its title.",book,first=>{
            if(client.RememberedViewCount!=1){fail("Initial view not saved");return;}
            client.Ask("What book did I show you earlier?",blank,recall=>{
                if(client.LastSource!="memory"||!recall.ToLowerInvariant().Contains("space")){fail("Book recall failed: "+recall);return;}
                client.Ask("Read its title again.",null,title=>{
                    if(client.LastSource!="memory"||!title.ToLowerInvariant().Contains("atlas")){fail("Title recall failed: "+title);return;}
                    client.ClearSession(sid,()=>{
                        client.Ask("What book did I show earlier?",null,missing=>{
                            if(client.RememberedViewCount!=0||!missing.Contains("don't have")){fail("Cleared image still accessible");return;}
                            client.ClearSession(sid,()=>{},e=>{});
                            ui.OnClearChatClicked();
                            EditorApplication.CallbackFunction finish=null;
                            finish=()=>{
                                if(ui==null){EditorApplication.update-=finish;Done("FAIL: chat closed during test");return;}
                                if(ui.IsBusy)return;
                                EditorApplication.update-=finish;
                                Done("PASS: Unity saved book view, recalled it after a blank current frame, read saved title without camera, deleted session memory, and completed Clear chat.\n"+title);
                            };
                            EditorApplication.update+=finish;
                        },fail,null,sid);
                    },fail);
                },fail,null,sid);
            },fail,null,sid);
        },fail,null,sid);
    }
}
