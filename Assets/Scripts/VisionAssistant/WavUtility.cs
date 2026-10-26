using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Converts between Unity AudioClip and raw 16-bit PCM WAV bytes.
/// Needed because Microphone gives you an AudioClip (must encode to WAV to
/// upload to /listen), and /speak returns raw WAV bytes (must decode to an
/// AudioClip to play through an AudioSource).
/// </summary>
public static class WavUtility
{
    public static byte[] ToWav(AudioClip clip)
    {
        int sampleCount = clip.samples * clip.channels;
        float[] samples = new float[sampleCount];
        clip.GetData(samples, 0);

        short[] intData = new short[sampleCount];
        byte[] bytesData = new byte[sampleCount * 2];
        for (int i = 0; i < sampleCount; i++)
        {
            intData[i] = (short)(samples[i] * short.MaxValue);
            byte[] byteArr = BitConverter.GetBytes(intData[i]);
            byteArr.CopyTo(bytesData, i * 2);
        }

        using (var stream = new MemoryStream())
        {
            WriteWavHeader(stream, clip.channels, clip.frequency, bytesData.Length);
            stream.Write(bytesData, 0, bytesData.Length);
            return stream.ToArray();
        }
    }

    private static void WriteWavHeader(Stream stream, int channels, int sampleRate, int dataLength)
    {
        int byteRate = sampleRate * channels * 2;

        void WriteString(string s) { foreach (char c in s) stream.WriteByte((byte)c); }
        void WriteInt(int v) { stream.Write(BitConverter.GetBytes(v), 0, 4); }
        void WriteShort(short v) { stream.Write(BitConverter.GetBytes(v), 0, 2); }

        WriteString("RIFF");
        WriteInt(36 + dataLength);
        WriteString("WAVE");
        WriteString("fmt ");
        WriteInt(16); // fmt chunk size
        WriteShort(1); // PCM
        WriteShort((short)channels);
        WriteInt(sampleRate);
        WriteInt(byteRate);
        WriteShort((short)(channels * 2)); // block align
        WriteShort(16); // bits per sample
        WriteString("data");
        WriteInt(dataLength);
    }

    /// <summary>
    /// Parses raw WAV bytes (16-bit PCM) into a playable AudioClip.
    /// </summary>
    public static AudioClip FromWav(byte[] wavBytes, string clipName = "GatewayAudio")
    {
        int channels = BitConverter.ToInt16(wavBytes, 22);
        int sampleRate = BitConverter.ToInt32(wavBytes, 24);

        int dataChunkOffset = FindChunk(wavBytes, "data");
        if (dataChunkOffset < 0)
        {
            Debug.LogError("WavUtility: could not find 'data' chunk in WAV bytes.");
            return null;
        }

        int dataSize = BitConverter.ToInt32(wavBytes, dataChunkOffset + 4);
        int sampleStart = dataChunkOffset + 8;
        int sampleCount = dataSize / 2; // 16-bit = 2 bytes per sample

        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            short raw = BitConverter.ToInt16(wavBytes, sampleStart + i * 2);
            samples[i] = raw / (float)short.MaxValue;
        }

        AudioClip clip = AudioClip.Create(clipName, sampleCount / channels, channels, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private static int FindChunk(byte[] wavBytes, string chunkId)
    {
        byte[] idBytes = System.Text.Encoding.ASCII.GetBytes(chunkId);
        for (int i = 12; i < wavBytes.Length - 4; i++)
        {
            if (wavBytes[i] == idBytes[0] && wavBytes[i + 1] == idBytes[1] &&
                wavBytes[i + 2] == idBytes[2] && wavBytes[i + 3] == idBytes[3])
            {
                return i;
            }
        }
        return -1;
    }
}
