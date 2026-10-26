using System;
using System.IO;
using UnityEngine;
using UnityEditor;
[InitializeOnLoad]
public static class ARCameraCheck
{
    private static double deadline;
    static ARCameraCheck() { EditorApplication.update += Check; }
    public static void Run()
    {
        SessionState.SetBool("ARCameraCheck.Pending", true);
        SessionState.SetString("ARCameraCheck.Deadline", DateTime.UtcNow.AddSeconds(45).ToString("O"));
        File.WriteAllText("C:/Users/kushw/local-gateway/tests/camera-check.txt","Waiting for wireless camera frames...");
        EditorApplication.isPlaying = true;
    }
    private static void Check()
    {
        if (!SessionState.GetBool("ARCameraCheck.Pending",false)) return;
        var capture=UnityEngine.Object.FindFirstObjectByType<FrameCapture>();
        var cam=capture!=null ? capture.ActiveWebCamTexture : null;
        if(EditorApplication.isPlaying && cam!=null && cam.isPlaying && cam.didUpdateThisFrame && cam.width>16 && cam.height>16)
        {
            try
            {
                var bytes=capture.CaptureFrameAsJpg();
                if(bytes!=null && bytes.Length>0)
                {
                    File.WriteAllText("C:/Users/kushw/local-gateway/tests/camera-check.txt","PASS: "+cam.deviceName+" delivers live frames at "+cam.width+"x"+cam.height+". JPEG capture: "+bytes.Length+" bytes.");
                    SessionState.SetBool("ARCameraCheck.Pending",false);
                }
            }
            catch(Exception e) { Finish("Capture failed: "+e.Message); }
        }
        else if(DateTime.UtcNow>DateTime.Parse(SessionState.GetString("ARCameraCheck.Deadline",DateTime.UtcNow.ToString("O"))))
            Finish("Camera selected, but no live frames arrived within 45 seconds. Check the wireless connection and phone camera permissions.");
    }
    private static void Finish(string message)
    {
        File.WriteAllText("C:/Users/kushw/local-gateway/tests/camera-check.txt",message);
        SessionState.SetBool("ARCameraCheck.Pending",false);
    }
}
