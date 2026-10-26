using System;
using UnityEngine;

/// <summary>Bounded microphone recording with explicit device selection and cleanup.</summary>
public class AudioRecorder : MonoBehaviour
{
    [SerializeField] private string preferredMicrophoneName = "Microphone Array (Realtek(R) Audio)";
    private const int SampleRate = 16000;
    private const int MaxSeconds = 30;
    private AudioClip recordingClip;
    private string microphoneDevice;
    private int lastPosition;
    private float started;
    public bool IsRecording { get; private set; }
    public string LastError { get; private set; }
    public string DeviceName => microphoneDevice;
    public float LastPeak { get; private set; }
    public float Duration => IsRecording ? Time.realtimeSinceStartup - started : 0f;

    public void StartRecording()
    {
        if (IsRecording) return;
        LastError = null;
        microphoneDevice = null;
        var devices = Microphone.devices;
        // On Quest, use the headset's default microphone. In the Editor keep
        // the user's explicitly selected laptop microphone.
        if (Application.platform == RuntimePlatform.Android && devices.Length > 0)
            microphoneDevice = devices[0];
        else
            foreach (string device in devices)
                if (device == preferredMicrophoneName) { microphoneDevice = device; break; }
        if (microphoneDevice == null)
        {
            LastError = Application.platform == RuntimePlatform.Android
                ? "Headset microphone unavailable. Check Quest microphone permission."
                : "Laptop microphone unavailable. Check Windows microphone access.";
            return;
        }
        try
        {
            recordingClip = Microphone.Start(microphoneDevice, false, MaxSeconds, SampleRate);
            if (recordingClip == null) throw new Exception("Microphone did not open.");
            lastPosition = 0;
            started = Time.realtimeSinceStartup;
            IsRecording = true;
        }
        catch (Exception e) { LastError = e.Message; CancelRecording(); }
    }

    private void Update()
    {
        if (!IsRecording) return;
        int position = Microphone.GetPosition(microphoneDevice);
        if (position > lastPosition) lastPosition = position;
    }

    public byte[] StopRecordingAndGetWav()
    {
        if (!IsRecording || recordingClip == null) return null;
        int position = Math.Max(lastPosition, Microphone.GetPosition(microphoneDevice));
        // The non-looping driver may reset its cursor after filling the buffer.
        if (Duration >= MaxSeconds) position = recordingClip.samples;
        Microphone.End(microphoneDevice);
        IsRecording = false;
        AudioClip trimmed = null;
        try
        {
            if (position < SampleRate / 4) { LastError = "Recording too short. Speak for at least a second."; return null; }
            position = Math.Min(position, recordingClip.samples);
            float[] samples = new float[position * recordingClip.channels];
            recordingClip.GetData(samples, 0);
            LastPeak = 0f;
            foreach (var sample in samples) LastPeak = Mathf.Max(LastPeak, Mathf.Abs(sample));
            if (LastPeak < 0.0005f) { LastError = "No microphone signal. Check mute and Windows input volume."; return null; }
            trimmed = AudioClip.Create("Voice question", position, recordingClip.channels, recordingClip.frequency, false);
            trimmed.SetData(samples, 0);
            return WavUtility.ToWav(trimmed);
        }
        finally
        {
            if (trimmed != null) Destroy(trimmed);
            Destroy(recordingClip);
            recordingClip = null;
        }
    }

    public void CancelRecording()
    {
        if (microphoneDevice != null && Microphone.IsRecording(microphoneDevice)) Microphone.End(microphoneDevice);
        IsRecording = false;
        if (recordingClip != null) Destroy(recordingClip);
        recordingClip = null;
    }
    private void OnDisable() { CancelRecording(); }
}
