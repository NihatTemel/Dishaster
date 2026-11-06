using System;
using UnityEngine;

namespace Dishaster
{
    /// <summary>
    /// Bir konuşanın sesi (VoiceChat yaratıyor, konuşanın kafasına bağlı). Gelen parçalar halka tampona yazılıyor,
    /// akış klibi oradan çalıyor. Ağ gecikmesi dalgalansa da kesilmesin diye önce biraz biriktiriliyor.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class VoiceSpeaker : MonoBehaviour
    {
        const int BufferSamples = VoiceChat.SampleRate * 2;
        // Çalmaya başlamadan önce (ve her kesintiden sonra) biriken: ~120 ms.
        const int PrebufferSamples = VoiceChat.SampleRate * 12 / 100;
        // Bundan fazla birikirse gecikme büyümesin diye eskiler atılıyor: ~500 ms.
        const int MaxLatencySamples = VoiceChat.SampleRate / 2;

        readonly float[] _buffer = new float[BufferSamples];
        readonly object _lock = new object();
        int _read;
        int _count;
        bool _playing;

        public void Setup(float volume, float spatialBlend, float minDistance, float maxDistance, float muffleCutoff)
        {
            var source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.volume = volume;
            source.spatialBlend = spatialBlend;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.dopplerLevel = 0f;
            source.clip = AudioClip.Create("Voice", VoiceChat.SampleRate, 1, VoiceChat.SampleRate, true, OnAudioRead);

            // Ağzı bağlı: yüksek frekanslar gidiyor, sadece mırıltı ve tonlama kalıyor.
            if (muffleCutoff > 0f)
            {
                var lowPass = gameObject.AddComponent<AudioLowPassFilter>();
                lowPass.cutoffFrequency = muffleCutoff;
                lowPass.lowpassResonanceQ = 1f;
            }

            source.Play();
        }

        void OnDestroy()
        {
            var source = GetComponent<AudioSource>();
            if (source != null && source.clip != null) Destroy(source.clip);
        }

        public void Push(ArraySegment<byte> data)
        {
            lock (_lock)
            {
                for (int i = 0; i < data.Count; i++)
                {
                    int write = (_read + _count) % BufferSamples;
                    _buffer[write] = MuLaw.Decode(data.Array[data.Offset + i]);
                    if (_count < BufferSamples) _count++;
                    else _read = (_read + 1) % BufferSamples;
                }

                if (_count > MaxLatencySamples)
                {
                    int drop = _count - PrebufferSamples;
                    _read = (_read + drop) % BufferSamples;
                    _count -= drop;
                }
            }
        }

        // Ses motoru istedikçe çağırıyor (ana iş parçacığında olmayabilir).
        void OnAudioRead(float[] data)
        {
            lock (_lock)
            {
                if (!_playing && _count >= PrebufferSamples) _playing = true;

                for (int i = 0; i < data.Length; i++)
                {
                    if (_playing && _count > 0)
                    {
                        data[i] = _buffer[_read];
                        _read = (_read + 1) % BufferSamples;
                        _count--;
                    }
                    else
                    {
                        data[i] = 0f;
                    }
                }

                // Konuşma bitti ya da ağ geç kaldı: tekrar biraz biriktirip başlıyor.
                if (_count == 0) _playing = false;
            }
        }
    }
}
