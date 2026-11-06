using System;
using UnityEngine;

namespace Dishaster
{
    /// <summary>
    /// Bir konuşanın sesi (VoiceChat yaratıyor, konuşanın kafasına bağlı). Gelen parçalar halka tampona yazılıyor;
    /// ses motoru OnAudioFilterRead'de doğrudan buradan okuyor (akış klibi ileriyi okuyup gecikme ekliyordu).
    /// AudioSource sabit 1'lerden oluşan bir klip çalıyor, ses onunla çarpılıyor: mesafe ve yön aynen kalıyor.
    /// Ağ gecikmesi dalgalansa da kesilmesin diye önce biraz biriktiriliyor; fazla birikirse atlanıyor.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class VoiceSpeaker : MonoBehaviour
    {
        const int BufferSamples = VoiceChat.SampleRate * 2;
        // Çalmaya başlamadan önce (ve her kesintiden sonra) biriken: ~60 ms.
        const int PrebufferSamples = VoiceChat.SampleRate * 6 / 100;
        // Bundan fazla birikirse gecikme büyümesin diye ön tampona kadar atlanıyor: ~160 ms.
        const int MaxLatencySamples = VoiceChat.SampleRate * 16 / 100;

        readonly float[] _buffer = new float[BufferSamples];
        readonly object _lock = new object();
        int _read;
        int _count;
        double _fraction;
        double _step = 1.0 / 3.0;
        float _gain = 1f;
        bool _playing;
        AudioClip _carrier;

        public void Setup(float volume, float gain, float spatialBlend, float minDistance, float maxDistance, float muffleCutoff)
        {
            int outputRate = AudioSettings.outputSampleRate;
            _step = (double)VoiceChat.SampleRate / outputRate;
            _gain = gain;

            // Taşıyıcı: hep 1. Ses filtrede bununla çarpılıyor.
            _carrier = AudioClip.Create("VoiceCarrier", outputRate, 1, outputRate, false);
            var ones = new float[outputRate];
            for (int i = 0; i < ones.Length; i++) ones[i] = 1f;
            _carrier.SetData(ones, 0);

            var source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.volume = volume;
            source.spatialBlend = spatialBlend;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.dopplerLevel = 0f;
            source.clip = _carrier;

            // Ağzı bağlı: yüksek frekanslar gidiyor, sadece mırıltı ve tonlama kalıyor.
            // Bu bileşenden sonra eklendiği için sesi filtreliyor.
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
            if (_carrier != null) Destroy(_carrier);
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

        // Ses iş parçacığında çağrılıyor; data taşıyıcının (1) çıkış kanallarına dağılmış hali.
        void OnAudioFilterRead(float[] data, int channels)
        {
            lock (_lock)
            {
                if (!_playing && _count >= PrebufferSamples) _playing = true;

                for (int frame = 0; frame < data.Length; frame += channels)
                {
                    float sample = 0f;
                    if (_playing && _count >= 2)
                    {
                        // 16 kHz'ten çıkış hızına doğrusal ara değerle.
                        float a = _buffer[_read];
                        float b = _buffer[(_read + 1) % BufferSamples];
                        sample = Mathf.Clamp((a + (b - a) * (float)_fraction) * _gain, -1f, 1f);

                        _fraction += _step;
                        while (_fraction >= 1.0 && _count > 1)
                        {
                            _fraction -= 1.0;
                            _read = (_read + 1) % BufferSamples;
                            _count--;
                        }
                    }
                    else
                    {
                        // Konuşma bitti ya da ağ geç kaldı: tekrar biraz biriktirip başlıyor.
                        _playing = false;
                    }

                    for (int c = 0; c < channels; c++)
                        data[frame + c] *= sample;
                }
            }
        }
    }
}
