using UnityEngine;

#if UDONSHARP || COMPILER_UDONSHARP
using UdonSharp;
#endif

namespace K13A.TSMP.Udon
{
    public enum AnimatorParameterType : byte
    {
        Float = 1,
        Int = 2,
        Bool = 3
    }

    public class TSMPNetworkAnimatorSync : TSMPNetworkBehaviour
    {
        public const int MaxEncodedParameters = 255;
        public const int MaxEncodedLayers = 255;
        public const int MaxLayerIndex = 255;

        public Animator animator;
        public string[] parameterNames;
        public byte[] parameterTypes;
        public int[] layerIndices;
        public float layerFadeDuration;
        public float normalizedTimeApplyThreshold = 0.02f;
        [HideInInspector] public int encodedParameterCount;
        [HideInInspector] public int encodedLayerCount;
        [HideInInspector] public int encodedAnimatorBytes;

        [HideInInspector]
        [TransSync("animator.packed")]
#if UDONSHARP || COMPILER_UDONSHARP
        [FieldChangeCallback(nameof(AnimatorBytes))]
#endif
        public byte[] animatorBytes;

        private const byte PacketVersion = 1;
        private const int HeaderBytes = 5;
        private const byte FlagHasParameters = 1 << 0;
        private const byte FlagHasLayers = 1 << 1;
        private const int ParameterHeaderBytes = 5;
        private const int BoolValueBytes = 1;
        private const int IntValueBytes = 4;
        private const int FloatValueBytes = 4;
        private const int LayerBytes = 13;

        private int[] _parameterHashes;
        private string[] _cachedParameterNames;
        private int _parameterHashLength = -1;
        private int[] _selectedParameterIndices;
        private int[] _selectedLayerIndices;

        public byte[] AnimatorBytes
        {
            get => animatorBytes;
            set
            {
                animatorBytes = value;
            }
        }

        private void Start()
        {
            ResolveAnimator();
        }

        public override void TSMPBeforeEncode()
        {
            if (!IsTSMPActive())
                return;

            ResolveAnimator();
            if (animator == null)
                return;

            EnsureParameterHashes();

            int parameterCount;
            int parameterBytes = CollectParameters(out parameterCount);
            int layerCount = CollectLayers();
            byte flags = 0;
            if (parameterCount > 0)
                flags |= FlagHasParameters;
            if (layerCount > 0)
                flags |= FlagHasLayers;

            int requiredBytes = HeaderBytes + parameterBytes + layerCount * LayerBytes;
            if (animatorBytes == null || animatorBytes.Length != requiredBytes)
                animatorBytes = new byte[requiredBytes];

            animatorBytes[0] = PacketVersion;
            animatorBytes[1] = flags;
            animatorBytes[2] = (byte)parameterCount;
            animatorBytes[3] = (byte)layerCount;
            animatorBytes[4] = 0;

            int cursor = HeaderBytes;
            for (int i = 0; i < parameterCount; i++)
            {
                int index = _selectedParameterIndices[i];
                int hash = _parameterHashes[index];
                byte type = parameterTypes[index];
                Binary.WriteInt32LE(animatorBytes, cursor, hash);
                cursor += 4;
                animatorBytes[cursor++] = type;

                if (type == (byte)AnimatorParameterType.Bool)
                {
                    animatorBytes[cursor++] = animator.GetBool(parameterNames[index]) ? (byte)1 : (byte)0;
                }
                else if (type == (byte)AnimatorParameterType.Int)
                {
                    Binary.WriteInt32LE(animatorBytes, cursor, animator.GetInteger(parameterNames[index]));
                    cursor += 4;
                }
                else
                {
                    cursor = Binary.WriteFloat32LE(animatorBytes, cursor, animator.GetFloat(parameterNames[index]));
                }
            }

            for (int i = 0; i < layerCount; i++)
            {
                int layer = _selectedLayerIndices[i];
                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(layer);
                animatorBytes[cursor++] = (byte)layer;
                Binary.WriteInt32LE(animatorBytes, cursor, state.fullPathHash);
                cursor += 4;
                cursor = Binary.WriteFloat32LE(animatorBytes, cursor, state.normalizedTime);
                cursor = Binary.WriteFloat32LE(animatorBytes, cursor, animator.GetLayerWeight(layer));
            }

            encodedParameterCount = parameterCount;
            encodedLayerCount = layerCount;
            encodedAnimatorBytes = cursor;
        }

        public override void OnTSMPVariableReceived()
        {
            if (receiveInterpolation == ReceiveInterpolationMode.None)
                return;

            ApplyAnimatorBytes();
            OnTSMPVariableChanged(lastVariableHash);
        }

        private void ApplyAnimatorBytes()
        {
            if (!IsTSMPActive() || animatorBytes == null || animatorBytes.Length < HeaderBytes)
                return;

            ResolveAnimator();
            if (animator == null || animatorBytes[0] != PacketVersion)
                return;

            EnsureParameterHashes();

            byte flags = animatorBytes[1];
            int parameterCount = animatorBytes[2];
            int layerCount = animatorBytes[3];
            int cursor = HeaderBytes;

            if ((flags & FlagHasParameters) != 0)
            {
                for (int i = 0; i < parameterCount; i++)
                {
                    if (cursor + ParameterHeaderBytes > animatorBytes.Length)
                        return;

                    int hash = Binary.ReadInt32LE(animatorBytes, cursor);
                    cursor += 4;
                    byte type = animatorBytes[cursor++];

                    if (type == (byte)AnimatorParameterType.Bool)
                    {
                        if (cursor + BoolValueBytes > animatorBytes.Length)
                            return;
                        int index = FindParameterIndex(hash, (byte)AnimatorParameterType.Bool);
                        bool value = animatorBytes[cursor++] != 0;
                        if (index >= 0)
                            animator.SetBool(parameterNames[index], value);
                    }
                    else if (type == (byte)AnimatorParameterType.Int)
                    {
                        if (cursor + IntValueBytes > animatorBytes.Length)
                            return;
                        int index = FindParameterIndex(hash, (byte)AnimatorParameterType.Int);
                        int value = Binary.ReadInt32LE(animatorBytes, cursor);
                        cursor += 4;
                        if (index >= 0)
                            animator.SetInteger(parameterNames[index], value);
                    }
                    else if (type == (byte)AnimatorParameterType.Float)
                    {
                        if (cursor + FloatValueBytes > animatorBytes.Length)
                            return;
                        int index = FindParameterIndex(hash, (byte)AnimatorParameterType.Float);
                        float value = Binary.ReadFloat32LE(animatorBytes, cursor);
                        cursor += 4;
                        if (index >= 0)
                            animator.SetFloat(parameterNames[index], value);
                    }
                    else
                    {
                        return;
                    }
                }
            }

            if ((flags & FlagHasLayers) != 0)
            {
                for (int i = 0; i < layerCount; i++)
                {
                    if (cursor + LayerBytes > animatorBytes.Length)
                        return;

                    int layer = animatorBytes[cursor++];
                    int stateHash = Binary.ReadInt32LE(animatorBytes, cursor);
                    cursor += 4;
                    float normalizedTime = Binary.ReadFloat32LE(animatorBytes, cursor);
                    cursor += 4;
                    float weight = Binary.ReadFloat32LE(animatorBytes, cursor);
                    cursor += 4;

                    if (layer < 0 || layer >= animator.layerCount || !IsSelectedLayer(layer))
                        continue;

                    animator.SetLayerWeight(layer, weight);
                    AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(layer);
                    float currentTime = current.normalizedTime;
                    float delta = Mathf.Abs(currentTime - normalizedTime);
                    if (current.loop)
                    {
                        delta = Mathf.Abs((currentTime - Mathf.Floor(currentTime)) - (normalizedTime - Mathf.Floor(normalizedTime)));
                        delta = Mathf.Min(delta, 1f - delta);
                    }
                    if (current.fullPathHash != stateHash || delta > normalizedTimeApplyThreshold)
                    {
                        if (layerFadeDuration > 0f)
                            animator.CrossFade(stateHash, layerFadeDuration, layer, normalizedTime);
                        else
                            animator.Play(stateHash, layer, normalizedTime);
                    }
                }
            }
        }

        private bool IsSelectedLayer(int layer)
        {
            if (layerIndices == null)
                return false;

            for (int i = 0; i < layerIndices.Length; i++)
            {
                if (layerIndices[i] == layer)
                    return true;
            }

            return false;
        }

        private void ResolveAnimator()
        {
            if (animator == null)
                animator = GetComponent<Animator>();
        }

        private void EnsureParameterHashes()
        {
            int length = parameterNames != null ? parameterNames.Length : 0;
            if (_parameterHashes == null || _parameterHashLength != length || _cachedParameterNames == null || _cachedParameterNames.Length != length)
            {
                _parameterHashes = new int[length];
                _cachedParameterNames = new string[length];
            }
            for (int i = 0; i < length; i++)
            {
                if (_cachedParameterNames[i] == parameterNames[i])
                    continue;
                _parameterHashes[i] = string.IsNullOrEmpty(parameterNames[i]) ? 0 : Animator.StringToHash(parameterNames[i]);
                _cachedParameterNames[i] = parameterNames[i];
            }
            _parameterHashLength = length;
        }

        private int CollectParameters(out int count)
        {
            count = 0;
            if (parameterNames == null || parameterTypes == null)
                return 0;

            if (_selectedParameterIndices == null || _selectedParameterIndices.Length != MaxEncodedParameters)
                _selectedParameterIndices = new int[MaxEncodedParameters];

            int length = parameterNames.Length < parameterTypes.Length ? parameterNames.Length : parameterTypes.Length;
            int bytes = 0;
            for (int i = 0; i < length && count < MaxEncodedParameters; i++)
            {
                if (!IsValidParameter(i))
                    continue;

                _selectedParameterIndices[count++] = i;
                bytes += ParameterHeaderBytes;
                bytes += parameterTypes[i] == (byte)AnimatorParameterType.Bool ? BoolValueBytes : IntValueBytes;
            }

            return bytes;
        }

        private int CollectLayers()
        {
            if (layerIndices == null || animator == null)
                return 0;

            if (_selectedLayerIndices == null || _selectedLayerIndices.Length != MaxEncodedLayers)
                _selectedLayerIndices = new int[MaxEncodedLayers];

            int layerCount = animator.layerCount;
            int count = 0;
            for (int i = 0; i < layerIndices.Length && count < MaxEncodedLayers; i++)
            {
                int layer = layerIndices[i];
                if (layer < 0 || layer >= layerCount || layer > MaxLayerIndex)
                    continue;

                _selectedLayerIndices[count++] = layer;
            }

            return count;
        }

        private bool IsValidParameter(int index)
        {
            if (parameterNames == null || parameterTypes == null || index < 0 || index >= parameterNames.Length || index >= parameterTypes.Length)
                return false;

            if (string.IsNullOrEmpty(parameterNames[index]))
                return false;

            byte type = parameterTypes[index];
            return type == (byte)AnimatorParameterType.Bool || type == (byte)AnimatorParameterType.Int || type == (byte)AnimatorParameterType.Float;
        }

        private int FindParameterIndex(int hash, byte type)
        {
            if (_parameterHashes == null || parameterTypes == null)
                return -1;

            int count = _parameterHashes.Length < parameterTypes.Length ? _parameterHashes.Length : parameterTypes.Length;
            for (int i = 0; i < count; i++)
            {
                if (_parameterHashes[i] == hash && parameterTypes[i] == type)
                    return i;
            }

            return -1;
        }

    }
}
