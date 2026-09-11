using UnityEngine;
using UnityEngine.Playables;

#if UDONSHARP || COMPILER_UDONSHARP
using UdonSharp;
#endif

namespace K13A.TSMP.Udon
{
    public class TSMPNetworkTimelineSync : TSMPNetworkBehaviour
    {
        public PlayableDirector director;
        public float timeApplyThreshold = 0.05f;
        [HideInInspector] public int encodedTimelineBytes;

        [HideInInspector]
        [TransSync("timeline.packed")]
#if UDONSHARP || COMPILER_UDONSHARP
        [FieldChangeCallback(nameof(TimelineBytes))]
#endif
        public byte[] timelineBytes;

        private const byte PacketVersion = 1;
        private const int PacketBytes = 10;
        private const byte StateStopped = 0;
        private const byte StatePaused = 1;
        private const byte StatePlaying = 2;
        private PlayableDirector _resolvedDirector;
        private byte _directorState;

        public byte[] TimelineBytes
        {
            get => timelineBytes;
            set
            {
                timelineBytes = value;
            }
        }

        private void Start()
        {
            ResolveDirector();
        }

        public override void TSMPBeforeEncode()
        {
            if (!IsTSMPActive())
                return;

            ResolveDirector();
            if (director == null)
                return;

            if (timelineBytes == null || timelineBytes.Length != PacketBytes)
                timelineBytes = new byte[PacketBytes];

            timelineBytes[0] = PacketVersion;
            timelineBytes[1] = GetDirectorState();
            Binary.WriteFloat32LE(timelineBytes, 2, (float)director.time);
            Binary.WriteFloat32LE(timelineBytes, 6, (float)director.duration);
            encodedTimelineBytes = PacketBytes;
        }

        public override void OnTSMPVariableReceived()
        {
            if (receiveInterpolation == ReceiveInterpolationMode.None)
                return;

            ApplyTimelineBytes();
            OnTSMPVariableChanged(lastVariableHash);
        }

        private void ApplyTimelineBytes()
        {
            if (!IsTSMPActive() || timelineBytes == null || timelineBytes.Length < PacketBytes || timelineBytes[0] != PacketVersion)
                return;

            ResolveDirector();
            if (director == null)
                return;

            byte state = timelineBytes[1];
            float time = Binary.ReadFloat32LE(timelineBytes, 2);
            float currentTime = (float)director.time;
            if (Mathf.Abs(currentTime - time) > timeApplyThreshold || state != StatePlaying)
            {
                director.time = time;
                director.Evaluate();
            }

            if (state == StatePlaying)
            {
                Play();
            }
            else if (state == StatePaused)
            {
                Pause();
            }
            else
            {
                Stop();
            }
        }

        private void ResolveDirector()
        {
            if (director == null)
                director = GetComponent<PlayableDirector>();
            if (_resolvedDirector == director)
                return;

            _resolvedDirector = director;
            _directorState = director != null && director.playOnAwake ? StatePlaying : StateStopped;
#if !COMPILER_UDONSHARP
            if (!Application.isPlaying)
                _directorState = StateStopped;
#endif
        }

        private byte GetDirectorState()
        {
#if !COMPILER_UDONSHARP
            if (director.state == PlayState.Playing)
                return StatePlaying;
            return director.playableGraph.IsValid() ? StatePaused : StateStopped;
#else
            return _directorState;
#endif
        }

        public void Play()
        {
            ResolveDirector();
            if (director == null)
                return;
            director.Play();
            _directorState = StatePlaying;
        }

        public void Pause()
        {
            ResolveDirector();
            if (director == null)
                return;
            director.Pause();
            _directorState = StatePaused;
        }

        public void Stop()
        {
            ResolveDirector();
            if (director == null)
                return;
            director.Stop();
            _directorState = StateStopped;
        }

        public void Resume()
        {
            ResolveDirector();
            if (director == null)
                return;
            director.Resume();
            _directorState = StatePlaying;
        }

    }
}
