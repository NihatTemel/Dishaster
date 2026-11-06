using System;
using System.Collections.Generic;
using Mirror;
using TMPro;
using UnityEngine;

namespace Dishaster
{
    /// <summary>Konuşanın mikrofonundan 40 ms'lik parça (16 kHz, μ-law).</summary>
    public struct VoiceMessage : NetworkMessage
    {
        public ArraySegment<byte> data;
    }

    /// <summary>Sunucunun diğerlerine dağıttığı parça: kimin konuştuğu ile.</summary>
    public struct VoiceRelayMessage : NetworkMessage
    {
        public uint speaker;
        public ArraySegment<byte> data;
    }

    /// <summary>
    /// Oyun içi sesli sohbet (prefab). Herkesin mikrofonu açık; ses eşiği geçilince konuşma gidiyor. Ses konuşanın
    /// kafasından, mesafeye göre geliyor. Steam'e bağlı değil: Unity mikrofonu + Mirror mesajı, local'de de çalışıyor.
    /// Roller: Görmedim ve Duymadım normal konuşuyor; Bilmiyorum'un ağzı bağlı, sesi herkese boğuk (sadece "hı ıı")
    /// geliyor; Duymadım kimseyi duymuyor. Bunu dinleyen taraf uyguluyor: konuşanın elinde değil.
    /// </summary>
    public class VoiceChat : MonoBehaviour
    {
        public const int SampleRate = 16000;
        const int ChunkSamples = 640;
        const int MicClipSeconds = 1;

        static VoiceChat s_instance;

        [Header("Mikrofon")]
        [Tooltip("Bu seviyenin (RMS) üstü konuşma sayılıyor.")]
        public float gateThreshold = 0.012f;
        [Tooltip("Konuşma bitince bu kadar daha gönderiliyor: kelime sonları kesilmesin.")]
        public float gateHoldSeconds = 0.35f;
        [Tooltip("Otomatik kazanç: konuşmanın tepe seviyesi buraya çekiliyor (kısık mikrofonlar da duyulsun).")]
        [Range(0.1f, 1f)] public float targetPeak = 0.6f;
        [Tooltip("Otomatik kazancın en fazla kaç kat yükseltebileceği.")]
        public float maxGain = 8f;

        [Header("Dinleme")]
        [Range(0f, 1f)] public float volume = 1f;
        [Range(0f, 1f)] public float spatialBlend = 0.6f;
        [Tooltip("Bu mesafeye kadar tam ses; sonrası yavaşça azalıyor (oda küçük, herkes duysun).")]
        public float minDistance = 6f;
        public float maxDistance = 40f;

        [Header("Roller")]
        [Tooltip("Bilmiyorum'un sesi bu frekansın altına kesiliyor: kelimeler anlaşılmıyor, sadece mırıltı.")]
        public float muffleCutoff = 350f;
        [Tooltip("Filtre sesi kıstığı için Bilmiyorum'un sesi bu kadar yükseltiliyor.")]
        public float muffleGain = 2.2f;
        [Tooltip("Kapalıyken Duymadım kimsenin sesini duymuyor.")]
        public bool deafHearsVoice;

        [Header("UI")]
        public TMP_Text status;

        readonly Dictionary<uint, VoiceSpeaker> _speakers = new Dictionary<uint, VoiceSpeaker>();
        readonly List<float> _pending = new List<float>();
        readonly float[] _chunk = new float[ChunkSamples];
        readonly byte[] _encoded = new byte[ChunkSamples];

        DishasterPlayer _local;
        bool _canHear = true;
        AudioClip _mic;
        int _micReadPosition;
        float[] _micRead = new float[0];
        double _resampleStep = 1.0;
        double _resamplePosition;
        float _lastMicSample;
        float _gateUntil = float.NegativeInfinity;
        float _gain = 2f;
        bool _shownSpeaking;

        void Awake()
        {
            s_instance = this;
            if (status != null) status.text = "";
        }

        void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
            if (_mic != null) Microphone.End(null);
        }

        void Update()
        {
            if (_local == null)
            {
                if (NetworkClient.localPlayer == null || !NetworkClient.localPlayer.TryGetComponent(out _local)) return;

                _canHear = _local.role != MonkeyRoles.Deaf || deafHearsVoice;
                StartMicrophone();
            }

            if (_mic != null) CaptureMicrophone();

            bool speaking = Time.time < _gateUntil;
            if (status != null && speaking != _shownSpeaking)
            {
                _shownSpeaking = speaking;
                status.text = speaking ? "<color=#6FD86F>●</color> Konuşuyorsun" : "";
            }
        }

        // ── MİKROFON ────────────────────────────────────────────────────────

        void StartMicrophone()
        {
            if (Microphone.devices.Length == 0)
            {
                if (status != null) status.text = "<color=#FF7060>Mikrofon bulunamadı</color>";
                return;
            }

            // Cihaz 16 kHz vermiyorsa kendi hızında kaydediyor; parçalar 16 kHz'e çevriliyor.
            _mic = Microphone.Start(null, true, MicClipSeconds, SampleRate);
            _micReadPosition = 0;
            _resampleStep = (double)_mic.frequency / SampleRate;
            _resamplePosition = 0.0;
        }

        void CaptureMicrophone()
        {
            int position = Microphone.GetPosition(null);
            if (position < 0 || position == _micReadPosition) return;

            int length = _mic.samples;
            int available = (position - _micReadPosition + length) % length;

            // Halka sonuna denk geliyorsa iki parça halinde okunuyor.
            int first = Mathf.Min(available, length - _micReadPosition);
            ReadMic(_micReadPosition, first);
            if (available > first) ReadMic(0, available - first);
            _micReadPosition = position;

            while (_pending.Count >= ChunkSamples)
            {
                _pending.CopyTo(0, _chunk, 0, ChunkSamples);
                _pending.RemoveRange(0, ChunkSamples);
                SendChunk();
            }
        }

        void ReadMic(int offset, int count)
        {
            if (count <= 0) return;

            // GetData dizinin boyu kadar okuyor.
            if (_micRead.Length != count) _micRead = new float[count];
            float[] read = _micRead;
            _mic.GetData(read, offset);

            // Doğrusal ara değerle 16 kHz'e.
            for (int i = 0; i < count; i++)
            {
                float sample = read[i];
                while (_resamplePosition < 1.0)
                {
                    _pending.Add(Mathf.Lerp(_lastMicSample, sample, (float)_resamplePosition));
                    _resamplePosition += _resampleStep;
                }

                _resamplePosition -= 1.0;
                _lastMicSample = sample;
            }
        }

        void SendChunk()
        {
            float sum = 0f;
            for (int i = 0; i < ChunkSamples; i++)
                sum += _chunk[i] * _chunk[i];

            if (Mathf.Sqrt(sum / ChunkSamples) > gateThreshold) _gateUntil = Time.time + gateHoldSeconds;
            if (Time.time >= _gateUntil) return;

            // Otomatik kazanç: yüksek sesde hemen kısılıyor, sessizleşince yavaşça açılıyor; tepeler yine de kırpılıyor.
            float peak = 0f;
            for (int i = 0; i < ChunkSamples; i++)
                peak = Mathf.Max(peak, Mathf.Abs(_chunk[i]));

            float wanted = Mathf.Clamp(targetPeak / Mathf.Max(peak, 0.001f), 1f, maxGain);
            _gain = wanted < _gain ? wanted : Mathf.MoveTowards(_gain, wanted, 0.15f);

            for (int i = 0; i < ChunkSamples; i++)
                _encoded[i] = MuLaw.Encode(Mathf.Clamp(_chunk[i] * _gain, -1f, 1f));

            NetworkClient.Send(new VoiceMessage { data = new ArraySegment<byte>(_encoded) }, Channels.Unreliable);
        }

        // ── DİNLEME ─────────────────────────────────────────────────────────

        void Receive(uint speakerId, ArraySegment<byte> data)
        {
            if (!_canHear || data.Count == 0) return;
            if (!NetworkClient.spawned.TryGetValue(speakerId, out NetworkIdentity identity) || identity == null) return;
            if (!identity.TryGetComponent(out DishasterPlayer speaker)) return;

            if (!_speakers.TryGetValue(speakerId, out VoiceSpeaker voice) || voice == null)
            {
                voice = CreateSpeaker(speaker);
                _speakers[speakerId] = voice;
            }

            voice.Push(data);
        }

        VoiceSpeaker CreateSpeaker(DishasterPlayer speaker)
        {
            // Kafada: konuşan döndükçe ve yürüdükçe ses de onunla.
            var go = new GameObject("Voice");
            go.transform.SetParent(speaker.headPivot, false);

            var voice = go.AddComponent<VoiceSpeaker>();
            bool muffled = MonkeyRoles.IsMouthTied(speaker.role);
            voice.Setup(volume, muffled ? muffleGain : 1f, spatialBlend, minDistance, maxDistance, muffled ? muffleCutoff : 0f);
            return voice;
        }

        // ── AĞ ──────────────────────────────────────────────────────────────

        public static void RegisterServerHandlers()
        {
            NetworkServer.RegisterHandler<VoiceMessage>(OnServerVoice);
        }

        public static void RegisterClientHandlers()
        {
            NetworkClient.RegisterHandler<VoiceRelayMessage>(OnClientVoice);
        }

        static void OnServerVoice(NetworkConnectionToClient sender, VoiceMessage message)
        {
            if (sender.identity == null || message.data.Count == 0 || message.data.Count > ChunkSamples) return;

            var relay = new VoiceRelayMessage { speaker = sender.identity.netId, data = message.data };
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
                if (connection != sender && connection.isReady)
                    connection.Send(relay, Channels.Unreliable);
        }

        static void OnClientVoice(VoiceRelayMessage message)
        {
            if (s_instance != null) s_instance.Receive(message.speaker, message.data);
        }
    }
}
