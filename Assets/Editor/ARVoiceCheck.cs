using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
[InitializeOnLoad]
public static class ARVoiceCheck
{
    static bool started;
    static double deadline;
    static ARVoiceCheck(){EditorApplication.update+=Tick;}
    public static void Run()
    {
        SessionState.SetBool("ARVoiceCheck.Run",true);started=false;
        File.WriteAllText("C:/Users/kushw/local-gateway/tests/voice-check.txt","RUNNING");
        EditorApplication.isPlaying=true;
    }
    static void Done(string text){SessionState.SetBool("ARVoiceCheck.Run",false);File.WriteAllText("C:/Users/kushw/local-gateway/tests/voice-check.txt",text);}
    static void Tick()
    {
        if(!SessionState.GetBool("ARVoiceCheck.Run",false)||!EditorApplication.isPlaying)return;
        if(started){if(EditorApplication.timeSinceStartup>deadline)Done("FAIL: voice pipeline timed out");return;}
        var capture=UnityEngine.Object.FindFirstObjectByType<FrameCapture>();
        if(capture==null||capture.ActiveWebCamTexture==null||capture.ActiveWebCamTexture.width<=16)return;
        started=true;deadline=EditorApplication.timeSinceStartup+210;
        var voice=UnityEngine.Object.FindFirstObjectByType<VoiceInputController>();
        var recorder=voice.GetComponent<AudioRecorder>();
        recorder.StartRecording();
        if(!recorder.IsRecording){Done("FAIL laptop mic: "+recorder.LastError);return;}
        double stop=EditorApplication.timeSinceStartup+2;
        EditorApplication.CallbackFunction poll=null;
        poll=()=>{
            if(EditorApplication.timeSinceStartup<stop)return;
            EditorApplication.update-=poll;
            int samples=Microphone.GetPosition(recorder.DeviceName);
            recorder.CancelRecording();
            if(samples<=0){Done("FAIL: laptop mic produced no samples");return;}
            voice.GetComponent<GatewaySpeechClient>().Listen(File.ReadAllBytes("C:/Users/kushw/local-gateway/tests/voice-fixture.wav"),text=>{
                if(string.IsNullOrWhiteSpace(text)){Done("FAIL: no recognized test speech");return;}
                var method=typeof(VoiceInputController).GetMethod("SubmitWhenReady",BindingFlags.NonPublic|BindingFlags.Instance);
                var field=typeof(VoiceInputController).GetField("requestVersion",BindingFlags.NonPublic|BindingFlags.Instance);
                voice.StartCoroutine((System.Collections.IEnumerator)method.Invoke(voice,new object[]{text,(int)field.GetValue(voice)}));
                Done("PASS: laptop microphone samples="+samples+"; Unity speech client recognized: "+text+"; automatic voice submission invoked.");
            },err=>Done("FAIL speech client: "+err));
        };
        EditorApplication.update+=poll;
    }
}
