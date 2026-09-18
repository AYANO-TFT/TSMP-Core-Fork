using UnityEngine;
#if UDONSHARP || COMPILER_UDONSHARP
using VRC.Udon;
#endif

#if UDONSHARP || COMPILER_UDONSHARP
using VRC.SDKBase;
using VRC.SDK3.Rendering;
#endif

#if !COMPILER_UDONSHARP
using UnityEngine.Rendering;
#endif

#if UDONSHARP || COMPILER_UDONSHARP
using VRC.Udon.Common.Interfaces;
#endif

namespace K13A.TSMP.Udon
{
    public class TSMPDecoder : TSMPBehaviour
    {
        public const string SourceTextureFieldName = nameof(sourceTexture);
        public const string PayloadByteTextureFieldName = nameof(payloadByteTexture);
        public const string CodecHandlersFieldName = nameof(codecHandlers);
        public const string BindingTargetsFieldName = nameof(bindingTargets);
        public const string BindingUdonTargetsFieldName = "bindingUdonTargets";
        public const string BindingNetworkIdsFieldName = nameof(bindingNetworkIds);
        public const string BindingVariableHashesFieldName = nameof(bindingVariableHashes);
        public const string BindingValueTypesFieldName = nameof(bindingValueTypes);
        public const string BindingFieldNamesFieldName = nameof(bindingFieldNames);
        public const string BindingDirectionsFieldName = nameof(bindingDirections);
        public const string BindingPrioritiesFieldName = nameof(bindingPriorities);
        public const string SourceWidthFieldName = nameof(sourceWidth);
        public const string SourceHeightFieldName = nameof(sourceHeight);
        public const string BlockSizeFieldName = nameof(blockSize);
        public const string SampleSizeFieldName = nameof(sampleSize);
        public const string FlipYFieldName = nameof(flipY);

        public Texture sourceTexture;
        public RenderTexture payloadByteTexture;

        public TSMPCodec[] codecHandlers;

        [HideInInspector] public Component[] bindingTargets;

#if UDONSHARP || COMPILER_UDONSHARP
        [HideInInspector] public UdonBehaviour[] bindingUdonTargets;
#endif
        [HideInInspector] public ushort[] bindingNetworkIds;
        [HideInInspector] public uint[] bindingVariableHashes;
        [HideInInspector] public byte[] bindingValueTypes;
        [HideInInspector] public string[] bindingFieldNames;
        [HideInInspector] public int[] bindingDirections;
        [HideInInspector] public int[] bindingPriorities;

        public bool applyEveryFrame = true;
        public bool skipDuplicateFrames = true;
        [Min(1)] public int frameWindowSize = 256;

        [HideInInspector]
        public int sourceWidth = 640;

        [HideInInspector]
        public int sourceHeight = 360;
        public int blockSize = 8;
        public int sampleSize;
        public bool flipY = true;

        public bool useHeaderPayloadLayout = true;
        public int payloadBytesOverride;
        public int decodeSafetyMode;

        public bool debugLog = true;
        public int debugErrorLogBudget = 32;
        public string lastError;
        [HideInInspector] public bool readbackInFlight;
        [HideInInspector] public bool lastFrameValid;
        [HideInInspector] public bool lastHeaderValid;
        [HideInInspector] public bool lastHeaderFlipY;
        [HideInInspector] public uint lastStreamId;
        [HideInInspector] public uint lastFrameIndex;
        [HideInInspector] public int skippedDuplicateFrameCount;
        [HideInInspector] public int skippedOutOfOrderFrameCount;
        [HideInInspector] public int lastSymbolMode;
        [HideInInspector] public int lastHeaderRow;
        [HideInInspector] public int lastHeaderSource;
        [HideInInspector] public int lastPayloadStartRow;
        [HideInInspector] public int lastPayloadAvailableBytes;
        [HideInInspector] public int lastPayloadSizeFromHeader;
        [HideInInspector] public int lastByteTextureCapacityBytes;
        [HideInInspector] public int lastRequestedByteCount;
        [HideInInspector] public int lastReadbackWidth;
        [HideInInspector] public int lastReadbackHeight;
        [HideInInspector] public int lastReadbackPixelCount;
        [HideInInspector] public int lastAppliedValueType;
        [HideInInspector] public int lastAppliedValueLength;
        [HideInInspector] public int lastNetworkMessageCount;
        [HideInInspector] public int lastAppliedVariableCount;
        [HideInInspector] public int lastRpcCallCount;
        [HideInInspector] public ushort lastRpcNetworkId;
        [HideInInspector] public uint lastRpcHash;
        [HideInInspector] public int lastRpcArgumentCount;
        [HideInInspector] public int lastRpcEventId;
        [HideInInspector] public string lastRpcMethodName;
        [HideInInspector] public int skippedDuplicateRpcCount;
        [HideInInspector] public byte[] lastRpcArgumentTypes;
        [HideInInspector] public int[] lastRpcArgumentOffsets;
        [HideInInspector] public int[] lastRpcArgumentLengths;

        private const int DecodeSafetyHeaderOnly = 1;
        private const int DecodeSafetyPayloadReadbackOnly = 2;
        private const int DecodeSafetyParseOnly = 3;
        private const string LogPrefix = "[TSMP] ";

        private Color32[] _readbackPixels;
        private RenderTexture _decodeSourceTexture;
        private bool _decodeSuspended;
        private bool _discardReadback;
        private byte[] _headerBytes;
        private byte[] _payloadBytes;
        private int _payloadDataBytes;
        private int _activeWidthBlocks;
        private int _payloadStartBlock;
        private int _payloadSymbolMode;
        private int _payloadCodecId;
        private byte[] _codecOptionBytes;
        private int _expectedPixels;
        private int _currentHeaderRow;
        private bool _decodeFlipY;
        private int _decodeStage;
        private bool _payloadInterleaved;
        private int _payloadType;
        private bool[][] _boolValueArrays;
        private int[][] _intValueArrays;
        private float[][] _floatValueArrays;
        private Vector2[][] _vector2ValueArrays;
        private Vector3[][] _vector3ValueArrays;
        private Quaternion[][] _quaternionValueArrays;
        private string[][] _stringValueArrays;
        private bool _hasAppliedFrame;
        private uint _lastAppliedStreamId;
        private uint _lastAppliedFrameIndex;
#if UDONSHARP || COMPILER_UDONSHARP
        private UdonBehaviour[] _cachedBindingUdonTargets;
#endif
        private Component[] _cachedBindingComponentTargets;
        private string[] _cachedBindingFieldNames;
        private ushort[] _bindingLookupNetworkIds;
        private uint[] _bindingLookupVariableHashes;
        private int[] _bindingLookupBindingIndices;
        private int _bindingLookupCount;
        private int _bindingLookupSignature;
        private byte[][] _rawByteValueArrays;
        private uint[] _crc32Table;
        private int _cachedBindingTargetCount = -1;
        private const int RpcDedupCacheSize = 32;
        private uint[] _recentRpcStreamIds;
        private ushort[] _recentRpcNetworkIds;
        private uint[] _recentRpcHashes;
        private int[] _recentRpcEventIds;
        private int _recentRpcWriteIndex;

        private void Start()
        {
            ResetDecodeDiagnostics();
            InitializeBuffers();
            _crc32Table = Crc32Runtime.EnsureTable(_crc32Table);
        }

        private void Update()
        {
            if (!applyEveryFrame)
                return;

            DecodeNow();
        }

        private void OnEnable()
        {
            _decodeSuspended = false;
        }

        private void OnDisable()
        {
            _decodeSuspended = true;
            _discardReadback = readbackInFlight;
            _decodeStage = 0;
            lastFrameValid = false;
            lastHeaderValid = false;
            DecoderSnapshotRuntime.Release(_decodeSourceTexture);
            _decodeSourceTexture = null;
        }

        private void OnDestroy()
        {
            DecoderSnapshotRuntime.Release(_decodeSourceTexture);
            _decodeSourceTexture = null;
        }

        public void ResetDecodeDiagnostics()
        {
            ResetTSMPLogBudget(debugErrorLogBudget);
        }

        public void DecodeNow()
        {
            lastError = string.Empty;

            if (readbackInFlight || _decodeSuspended)
                return;

            if (!ValidateSetup())
                return;

            SyncSourceDimensions();
            InitializeBuffers();
            _currentHeaderRow = 2;
            _decodeFlipY = flipY;
            _decodeSourceTexture = DecoderSnapshotRuntime.Capture(sourceTexture, _decodeSourceTexture, out lastError);
            if (_decodeSourceTexture == null)
            {
                lastFrameValid = false;
                lastHeaderValid = false;
                LogDecodeError(lastError);
                return;
            }

            RequestHeaderCopy();
        }

#if COMPILER_UDONSHARP
        public override void OnAsyncGpuReadbackComplete(VRCAsyncGPUReadbackRequest request)
        {
            if (!AcceptReadback())
                return;

            if (request.hasError)
            {
                lastFrameValid = false;
                lastError = "Async GPU readback failed.";
                LogDecodeError(lastError);
                return;
            }

            if (!request.TryGetData(_readbackPixels))
            {
                lastFrameValid = false;
                lastError = "TryGetData failed.";
                LogDecodeError(lastError);
                return;
            }

            CompleteReadbackStage();
        }
#else
        private void OnAsyncGpuReadbackComplete(AsyncGPUReadbackRequest request)
        {
            if (this == null || !AcceptReadback())
                return;

            if (request.hasError)
            {
                lastFrameValid = false;
                lastError = "Async GPU readback failed.";
                LogDecodeError(lastError);
                return;
            }

            var pixels = request.GetData<Color32>();
            if (_readbackPixels == null || pixels.Length < lastReadbackPixelCount)
            {
                lastFrameValid = false;
                lastError = "Async GPU readback data is smaller than requested.";
                LogDecodeError(lastError);
                return;
            }

            for (int i = 0; i < lastReadbackPixelCount; i++)
                _readbackPixels[i] = pixels[i];

            CompleteReadbackStage();
        }
#endif

        private bool AcceptReadback()
        {
            if (!readbackInFlight)
                return false;

            readbackInFlight = false;
            if (_discardReadback || _decodeSuspended)
            {
                _discardReadback = false;
                return false;
            }

            return true;
        }

        private void CompleteReadbackStage()
        {
            if (_decodeStage == 1)
            {
                if (!CopyBytesFromPixels(_headerBytes, FrameHeader.Size))
                    return;

                if (!ReadHeader())
                    return;

                if (ShouldSkipFrame())
                    return;

                if (decodeSafetyMode == DecodeSafetyHeaderOnly)
                {
                    lastFrameValid = true;
                    lastError = "Decode safety mode: header only.";
                    return;
                }

                RequestPayloadReadback();
                return;
            }

            if (!CopyBytesFromPixels(_payloadBytes, _payloadDataBytes))
                return;

            if (decodeSafetyMode == DecodeSafetyPayloadReadbackOnly)
            {
                lastFrameValid = true;
                lastError = "Decode safety mode: payload readback only.";
                return;
            }

            if (!ApplyNetworkFrame())
                return;
            _hasAppliedFrame = true;
            _lastAppliedStreamId = lastStreamId;
            _lastAppliedFrameIndex = lastFrameIndex;

            lastFrameValid = true;
        }

        private bool ValidateSetup()
        {
            return DecoderReadbackRuntime.ValidateSetup(sourceTexture, payloadByteTexture, codecHandlers, out lastError);
        }

        private void SyncSourceDimensions()
        {
            if (sourceTexture == null)
                return;

            sourceWidth = Mathf.Max(1, sourceTexture.width);
            sourceHeight = Mathf.Max(1, sourceTexture.height);
        }

        private void InitializeBuffers()
        {
            if (payloadByteTexture == null)
            {
                lastByteTextureCapacityBytes = 0;
                return;
            }

            _payloadDataBytes = 4096;
            if (payloadBytesOverride > 0)
                _payloadDataBytes = payloadBytesOverride;

            _activeWidthBlocks = blockSize > 0 ? sourceWidth / blockSize : 0;
            if (_activeWidthBlocks <= 0)
                lastError = "Invalid layout: sourceWidth=" + sourceWidth + " blockSize=" + blockSize + " yields zero active blocks.";
            _payloadStartBlock = GetPayloadDataStartRow() * _activeWidthBlocks;
            _expectedPixels = ByteTextureReader.GetRequiredPixelCount(_payloadDataBytes);
            lastByteTextureCapacityBytes = ByteTextureReader.GetCapacityBytes(payloadByteTexture.width, payloadByteTexture.height);

            _headerBytes = DecoderReadbackRuntime.EnsureByteBuffer(_headerBytes, FrameHeader.Size);
            _codecOptionBytes = DecoderReadbackRuntime.EnsureCodecOptionBuffer(_codecOptionBytes);
        }

        private void RequestHeaderCopy()
        {
            int startBlock = _currentHeaderRow * _activeWidthBlocks;
            lastRequestedByteCount = FrameHeader.Size;
            _decodeStage = 1;
            RunByteDecodePass(startBlock, FrameHeader.Size, 0);
            RequestByteReadback(FrameHeader.Size);
        }

        private void RequestPayloadReadback()
        {
            _payloadBytes = DecoderReadbackRuntime.EnsureByteBuffer(_payloadBytes, _payloadDataBytes);
            int symbolMode = _payloadSymbolMode;
            TSMPCodec handler = PrepareDecodeHandler(_payloadCodecId, _payloadDataBytes);
            if (handler == null || handler.selectedDecodeMaterial == null)
            {
                lastFrameValid = false;
                lastError = "Byte decode codec handler/material is not assigned. codecId=" + _payloadCodecId + " symbolMode=" + symbolMode;
                LogDecodeError(lastError);
                return;
            }

            _payloadStartBlock = handler.payloadStartRow * _activeWidthBlocks;
            lastPayloadStartRow = handler.payloadStartRow;
            lastRequestedByteCount = _payloadDataBytes;
            _decodeStage = 2;
            RunByteDecodePass(_payloadStartBlock, _payloadDataBytes, symbolMode);
            RequestByteReadback(_payloadDataBytes);
        }

        private void RequestByteReadback(int byteCount)
        {
            int pixelCount;
            int readWidth;
            int readHeight;
            int readbackPixelCount;
            if (!DecoderReadbackRuntime.TryGetReadbackRegion(payloadByteTexture, byteCount, out pixelCount, out readWidth, out readHeight, out readbackPixelCount, out lastError))
            {
                lastFrameValid = false;
                LogDecodeError(lastError);
                return;
            }

            _readbackPixels = DecoderReadbackRuntime.EnsurePixelBuffer(_readbackPixels, readbackPixelCount);

            lastReadbackWidth = readWidth;
            lastReadbackHeight = readHeight;
            lastReadbackPixelCount = readbackPixelCount;

            IssueByteReadback(byteCount, readWidth, readHeight);
        }

        private void IssueByteReadback(int byteCount, int readWidth, int readHeight)
        {
            readbackInFlight = true;
#if COMPILER_UDONSHARP
            VRCAsyncGPUReadback.Request(payloadByteTexture, 0, 0, readWidth, 0, readHeight, 0, 1, (IUdonEventReceiver)this);
#else
            AsyncGPUReadback.Request(payloadByteTexture, 0, OnAsyncGpuReadbackComplete);
#endif
        }

        private void RunByteDecodePass(int startBlock, int byteCount, int symbolMode)
        {
            int codecId = _payloadCodecId;
            if (symbolMode == 0)
                codecId = 0;

            TSMPCodec handler = PrepareDecodeHandler(codecId, byteCount);
            Material material = handler != null ? handler.selectedDecodeMaterial : null;
            if (material == null)
                return;

            Texture decodeSource = _decodeSourceTexture;
            material.SetTexture(ShaderProperties.MainTex, decodeSource);
            material.SetFloat(ShaderProperties.BlockSize, blockSize);
            material.SetFloat(ShaderProperties.SampleSize, sampleSize);
            material.SetFloat(ShaderProperties.StartBlock, startBlock);
            material.SetFloat(ShaderProperties.ByteCount, byteCount);
            material.SetFloat(ShaderProperties.ActiveWidthBlocks, _activeWidthBlocks);
            material.SetFloat(ShaderProperties.SourceWidth, sourceWidth);
            material.SetFloat(ShaderProperties.SourceHeight, sourceHeight);
            material.SetFloat(ShaderProperties.OutputWidth, payloadByteTexture.width);
            material.SetFloat(ShaderProperties.OutputHeight, payloadByteTexture.height);
            material.SetFloat(ShaderProperties.FlipY, _decodeFlipY ? 1f : 0f);

            handler.PrepareDecode(decodeSource, material);
            GraphicsBridge.Blit(decodeSource, payloadByteTexture, material);
        }

        private TSMPCodec PrepareDecodeHandler(int codecId, int byteCount)
        {
            return CodecBridge.PrepareDecodeHandler(codecHandlers, codecId, _codecOptionBytes, _activeWidthBlocks, _decodeStage, _payloadInterleaved, byteCount);
        }

        private bool CopyBytesFromPixels(byte[] destination, int byteCount)
        {
            if (!DecoderReadbackRuntime.TryCopyBytes(_readbackPixels, destination, byteCount, out _expectedPixels, out lastError))
            {
                lastFrameValid = false;
                LogDecodeError(lastError);
                return false;
            }

            return true;
        }

        private bool ReadHeader()
        {
            _crc32Table = Crc32Runtime.EnsureTable(_crc32Table);
            int headerStatus;
            uint decodedMagic;
            ushort decodedHeaderSize;
            int versionMajor;
            uint expectedCrc;
            uint actualCrc;
            if (!FrameHeaderReader.TryValidate(_headerBytes, 0, _crc32Table, out headerStatus, out decodedMagic, out decodedHeaderSize, out versionMajor, out expectedCrc, out actualCrc))
            {
                if (headerStatus == FrameHeaderReader.StatusMagicMismatch)
                    return FailHeaderRead("Header magic mismatch. magic=" + decodedMagic + " row=" + _currentHeaderRow + " startBlock=" + (_currentHeaderRow * _activeWidthBlocks) + " source=" + sourceWidth + "x" + sourceHeight + " blockSize=" + blockSize + " flipY=" + _decodeFlipY);
                if (headerStatus == FrameHeaderReader.StatusHeaderSizeMismatch)
                    return FailHeaderRead("Header size mismatch. expected=" + FrameHeader.Size + " actual=" + decodedHeaderSize);
                if (headerStatus == FrameHeaderReader.StatusVersionMismatch)
                    return FailHeaderRead("Unsupported TSMP header major version. version=" + versionMajor);
                if (headerStatus == FrameHeaderReader.StatusCrcMismatch)
                {
                    lastHeaderValid = false;
                    lastFrameValid = false;
                    lastError = "Header CRC mismatch. expected=" + expectedCrc + " actual=" + actualCrc + " frame discarded.";
                    LogHeaderCrcWarning(lastError);
                    return false;
                }

                return FailHeaderRead("Header parse failed.");
            }

            lastHeaderValid = true;
            lastHeaderSource = 0;
            lastHeaderRow = _currentHeaderRow;
            lastHeaderFlipY = _decodeFlipY;

            ushort headerBlockSize;
            ushort headerActiveWidthBlocks;
            ushort payloadType;
            ushort payloadSize;
            int headerSampleSize;
            uint streamId;
            uint decodedFrameIndex;
            _codecOptionBytes = DecoderReadbackRuntime.EnsureCodecOptionBuffer(_codecOptionBytes);
            DecoderHeaderRuntime.ReadHeaderFields(_headerBytes, _codecOptionBytes, out headerBlockSize, out headerActiveWidthBlocks, out payloadType, out payloadSize, out headerSampleSize, out _payloadSymbolMode, out _payloadCodecId, out streamId, out decodedFrameIndex);

            TSMPCodec headerCodecHandler = PrepareDecodeHandler(_payloadCodecId, payloadSize);
            if (headerCodecHandler == null)
            {
                lastFrameValid = false;
                lastError = "Unsupported codec id. codecId=" + _payloadCodecId;
                LogDecodeError(lastError);
                return false;
            }

            _payloadType = payloadType;
            _payloadInterleaved = false;
            lastSymbolMode = _payloadSymbolMode;
            lastStreamId = streamId;
            lastFrameIndex = decodedFrameIndex;
            lastPayloadSizeFromHeader = payloadSize;

            if (_payloadType != NetworkFrameProtocol.PayloadTypeNetworkFrame)
            {
                lastFrameValid = false;
                lastError = "Unsupported payload type. expected NetworkFrame=" + NetworkFrameProtocol.PayloadTypeNetworkFrame + " actual=" + _payloadType;
                LogDecodeError(lastError);
                return false;
            }

            DecoderHeaderRuntime.ResolvePayloadLayout(useHeaderPayloadLayout, sourceWidth, headerBlockSize, headerActiveWidthBlocks, headerSampleSize, payloadSize, headerCodecHandler.payloadStartRow, blockSize, sampleSize, _activeWidthBlocks, _payloadDataBytes, _payloadBytes, out blockSize, out sampleSize, out _activeWidthBlocks, out _payloadDataBytes, out _payloadBytes, out _payloadStartBlock, out lastPayloadStartRow);

            if (_payloadDataBytes < NetworkFrameProtocol.NetworkHeaderBytes)
                return FailHeaderRead("Payload is too small for NetworkFrame.");

            return true;
        }

        private bool ShouldSkipFrame()
        {
            if (!skipDuplicateFrames || !_hasAppliedFrame || lastStreamId != _lastAppliedStreamId)
                return false;

            if (lastFrameIndex == _lastAppliedFrameIndex)
            {
                skippedDuplicateFrameCount++;
                lastFrameValid = true;
                lastError = "Duplicate frame skipped.";
                return true;
            }

            long distance = (long)lastFrameIndex - (long)_lastAppliedFrameIndex;
            if (distance < 0L)
                distance += 4294967296L;
            if (distance < 2147483648L)
                return false;

            if (lastFrameIndex == 0u && (long)_lastAppliedFrameIndex >= (long)Mathf.Max(1, frameWindowSize))
                return false;

            skippedOutOfOrderFrameCount++;
            lastFrameValid = true;
            lastError = "Out-of-order frame skipped.";
            return true;
        }

        private int GetPayloadDataStartRow()
        {
            TSMPCodec handler = PrepareDecodeHandler(_payloadCodecId, _payloadDataBytes);
            return handler != null ? handler.payloadStartRow : Luma4Raster.PayloadStartRow;
        }

        private bool FailHeaderRead(string error)
        {
            lastHeaderValid = false;
            lastFrameValid = false;
            lastError = error;
            LogDecodeError(lastError);
            return false;
        }

        private void LogDecodeError(string error)
        {
            LogTSMPError(LogPrefix, error, debugLog);
        }

        private void LogHeaderCrcWarning(string error)
        {
            LogTSMPRateLimitedWarning(LogPrefix, error, 1f);
        }

        private bool ApplyNetworkFrame()
        {
            lastAppliedVariableCount = 0;
            lastNetworkMessageCount = 0;
            lastRpcCallCount = 0;
            lastRpcMethodName = string.Empty;
            skippedDuplicateRpcCount = 0;
            lastPayloadAvailableBytes = _payloadBytes != null ? Mathf.Clamp(_payloadDataBytes, 0, _payloadBytes.Length) : 0;

            if (_payloadBytes == null || _payloadDataBytes < NetworkFrameProtocol.NetworkHeaderBytes)
            {
                lastFrameValid = false;
                lastError = "Payload is too small for NetworkFrame.";
                LogDecodeError(lastError);
                return false;
            }

            if (_payloadDataBytes > _payloadBytes.Length)
                return FailNetworkFrame("Payload length exceeds buffer capacity.");

            int cursor;
            int messageCount;
            if (!NetworkFrameReader.TryReadNetworkFrameHeader(_payloadBytes, out messageCount, out cursor))
                return FailNetworkFrame("NetworkFrame header is malformed.");

            lastNetworkMessageCount = messageCount;
            if (decodeSafetyMode != DecodeSafetyParseOnly)
                EnsureBindingTargetCache(GetBindingTargetCount());

            for (int i = 0; i < messageCount; i++)
            {
                ushort networkId;
                int messageType;
                int bodyStart;
                int bodyEnd;
                int nextMessageOffset;
                if (!NetworkFrameReader.TryReadMessageHeader(_payloadBytes, cursor, _payloadDataBytes, out networkId, out messageType, out bodyStart, out bodyEnd, out nextMessageOffset))
                    return FailNetworkFrame("NetworkFrame message is malformed.");

                if (NetworkFrameReader.IsVariableStateMessage(messageType))
                {
                    if (!ApplyVariableState(networkId, bodyStart, bodyEnd))
                        return false;
                }
                else if (NetworkFrameReader.IsRpcCallMessage(messageType))
                {
                    if (decodeSafetyMode != DecodeSafetyParseOnly && !ReadRpcCall(networkId, bodyStart, bodyEnd))
                        return false;
                }

                cursor = nextMessageOffset;
            }

            if (cursor != _payloadDataBytes)
                return FailNetworkFrame("NetworkFrame has trailing bytes.");

            return true;
        }

        private bool ReadRpcCall(ushort networkId, int bodyStart, int bodyEnd)
        {
            uint rpcHash;
            int argumentCount;
            int eventId;
            string methodName;
            string error;
            if (!DecoderRpcRuntime.TryReadRpcCall(
                    _payloadBytes,
                    bodyStart,
                    bodyEnd,
                    lastRpcArgumentTypes,
                    lastRpcArgumentOffsets,
                    lastRpcArgumentLengths,
                    out lastRpcArgumentTypes,
                    out lastRpcArgumentOffsets,
                    out lastRpcArgumentLengths,
                    out rpcHash,
                    out argumentCount,
                    out methodName,
                    out eventId,
                    out error))
                return FailNetworkFrame(error);

            int visibleArgumentCount = argumentCount;
            if (!string.IsNullOrEmpty(methodName) && visibleArgumentCount > 0)
                visibleArgumentCount--;
            if (eventId > 0 && visibleArgumentCount > 0)
                visibleArgumentCount--;

            lastRpcNetworkId = networkId;
            lastRpcHash = rpcHash;
            lastRpcArgumentCount = visibleArgumentCount;
            lastRpcEventId = eventId;
            lastRpcMethodName = methodName;

            if (IsDuplicateRpcEvent(networkId, rpcHash, eventId))
            {
                skippedDuplicateRpcCount++;
                return true;
            }

            lastRpcCallCount++;
            DispatchRpc(networkId, rpcHash, visibleArgumentCount, methodName);
            return true;
        }

        private bool IsDuplicateRpcEvent(ushort networkId, uint rpcHash, int eventId)
        {
            if (eventId <= 0)
                return false;

            EnsureRpcDedupCache();
            for (int i = 0; i < RpcDedupCacheSize; i++)
            {
                if (_recentRpcEventIds[i] != eventId)
                    continue;
                if (_recentRpcStreamIds[i] != lastStreamId)
                    continue;
                if (_recentRpcNetworkIds[i] != networkId)
                    continue;
                if (_recentRpcHashes[i] != rpcHash)
                    continue;

                return true;
            }

            _recentRpcStreamIds[_recentRpcWriteIndex] = lastStreamId;
            _recentRpcNetworkIds[_recentRpcWriteIndex] = networkId;
            _recentRpcHashes[_recentRpcWriteIndex] = rpcHash;
            _recentRpcEventIds[_recentRpcWriteIndex] = eventId;
            _recentRpcWriteIndex++;
            if (_recentRpcWriteIndex >= RpcDedupCacheSize)
                _recentRpcWriteIndex = 0;

            return false;
        }

        private void EnsureRpcDedupCache()
        {
            if (_recentRpcStreamIds == null || _recentRpcStreamIds.Length != RpcDedupCacheSize)
                _recentRpcStreamIds = new uint[RpcDedupCacheSize];
            if (_recentRpcNetworkIds == null || _recentRpcNetworkIds.Length != RpcDedupCacheSize)
                _recentRpcNetworkIds = new ushort[RpcDedupCacheSize];
            if (_recentRpcHashes == null || _recentRpcHashes.Length != RpcDedupCacheSize)
                _recentRpcHashes = new uint[RpcDedupCacheSize];
            if (_recentRpcEventIds == null || _recentRpcEventIds.Length != RpcDedupCacheSize)
                _recentRpcEventIds = new int[RpcDedupCacheSize];
        }

        private void DispatchRpc(ushort networkId, uint rpcHash, int argumentCount, string methodName)
        {
            int targetCount = GetBindingTargetCount();
            if (targetCount <= 0)
                return;

#if UDONSHARP || COMPILER_UDONSHARP
            DecoderRpcDispatcher.Dispatch(_cachedBindingUdonTargets, targetCount, bindingNetworkIds, networkId, rpcHash, argumentCount, methodName);
#else
            DecoderRpcDispatcher.Dispatch(_cachedBindingComponentTargets, targetCount, bindingNetworkIds, networkId, rpcHash, argumentCount, methodName);
#endif
        }

        private bool ApplyVariableState(ushort networkId, int bodyStart, int bodyEnd)
        {
            int variableCount;
            int cursor;
            if (!NetworkFrameReader.TryReadVariableStateHeader(_payloadBytes, bodyStart, bodyEnd, out variableCount, out cursor))
                return FailNetworkFrame("VariableState body is too small.");

            for (int i = 0; i < variableCount; i++)
            {
                uint variableHash;
                int valueType;
                int valueOffset;
                int valueLength;
                int nextEntryOffset;
                if (!NetworkFrameReader.TryReadVariableEntry(_payloadBytes, cursor, bodyEnd, out variableHash, out valueType, out valueOffset, out valueLength, out nextEntryOffset))
                    return FailNetworkFrame("VariableState value is malformed.");

                if (decodeSafetyMode != DecodeSafetyParseOnly)
                    ApplyVariableValue(networkId, variableHash, valueType, valueOffset, valueLength);
                cursor = nextEntryOffset;
            }

            if (cursor != bodyEnd)
                return FailNetworkFrame("VariableState body has trailing bytes.");

            return true;
        }

        private void ApplyVariableValue(ushort networkId, uint variableHash, int valueType, int valueOffset, int valueLength)
        {
            int bindingCount = GetReadableBindingCount();
            if (bindingCount <= 0)
                return;

            int rejectedValueTypeCount;
#if UDONSHARP || COMPILER_UDONSHARP
            int appliedCount = DecoderVariableRuntime.ApplyVariableValue(
                _payloadBytes,
                networkId,
                variableHash,
                valueType,
                valueOffset,
                valueLength,
                bindingCount,
                _cachedBindingUdonTargets,
                bindingNetworkIds,
                bindingVariableHashes,
                bindingValueTypes,
                bindingFieldNames,
                _bindingLookupNetworkIds,
                _bindingLookupVariableHashes,
                _bindingLookupBindingIndices,
                _bindingLookupCount,
                _rawByteValueArrays,
                _boolValueArrays,
                _intValueArrays,
                _floatValueArrays,
                _vector2ValueArrays,
                _vector3ValueArrays,
                _quaternionValueArrays,
                _stringValueArrays,
                out rejectedValueTypeCount);
#else
            int appliedCount = DecoderVariableRuntime.ApplyVariableValue(
                _payloadBytes,
                networkId,
                variableHash,
                valueType,
                valueOffset,
                valueLength,
                bindingCount,
                _cachedBindingComponentTargets,
                bindingNetworkIds,
                bindingVariableHashes,
                bindingValueTypes,
                bindingFieldNames,
                _bindingLookupNetworkIds,
                _bindingLookupVariableHashes,
                _bindingLookupBindingIndices,
                _bindingLookupCount,
                _rawByteValueArrays,
                _boolValueArrays,
                _intValueArrays,
                _floatValueArrays,
                _vector2ValueArrays,
                _vector3ValueArrays,
                _quaternionValueArrays,
                _stringValueArrays,
                out rejectedValueTypeCount);
#endif

            if (rejectedValueTypeCount > 0)
            {
                lastError = "Skipped " + rejectedValueTypeCount + " TSMP variable value(s) with mismatched value type.";
                LogTSMPWarning(LogPrefix, lastError, debugLog, 1f);
            }

            if (appliedCount <= 0)
                return;

            lastAppliedValueType = valueType;
            lastAppliedValueLength = valueLength;
            lastAppliedVariableCount += appliedCount;
        }

        private int GetReadableBindingCount()
        {
#if UDONSHARP || COMPILER_UDONSHARP
            return BindingTable.GetWritableBindingCount(bindingTargets, bindingUdonTargets, true, bindingNetworkIds, bindingVariableHashes, bindingValueTypes, bindingFieldNames);
#else
            return BindingTable.GetWritableBindingCount(bindingTargets, bindingNetworkIds, bindingVariableHashes, bindingValueTypes, bindingFieldNames);
#endif
        }

        private int GetBindingTargetCount()
        {
#if UDONSHARP || COMPILER_UDONSHARP
            return BindingTable.GetTargetCount(bindingTargets, bindingUdonTargets, true);
#else
            return BindingTable.GetTargetCount(bindingTargets);
#endif
        }

        private void EnsureBindingTargetCache(int targetCount)
        {
            if (targetCount < 0)
                targetCount = 0;

            bool cacheValid = true;
            if (_cachedBindingTargetCount != targetCount)
                cacheValid = false;
#if UDONSHARP || COMPILER_UDONSHARP
            if (!BindingTable.MatchesUdonTargets(_cachedBindingUdonTargets, bindingTargets, bindingUdonTargets, targetCount))
                cacheValid = false;
#else
            if (!BindingTable.MatchesComponentTargets(_cachedBindingComponentTargets, bindingTargets, targetCount))
                cacheValid = false;
#endif
            bool valueCacheValid = DecoderBindingRuntime.IsArrayValueCacheValid(
                targetCount, _rawByteValueArrays, _boolValueArrays, _intValueArrays, _floatValueArrays,
                _vector2ValueArrays, _vector3ValueArrays, _quaternionValueArrays, _stringValueArrays);
            if (!valueCacheValid)
                cacheValid = false;
            if (_cachedBindingFieldNames == null || _cachedBindingFieldNames.Length != targetCount)
                cacheValid = false;
            for (int i = 0; cacheValid && i < targetCount; i++)
            {
                string fieldName = bindingFieldNames != null && i < bindingFieldNames.Length ? bindingFieldNames[i] : null;
                if (_cachedBindingFieldNames[i] != fieldName)
                    cacheValid = false;
            }

            if (cacheValid)
            {
                EnsureBindingLookupCache(targetCount);
                return;
            }

            _cachedBindingTargetCount = targetCount;
            _rawByteValueArrays = new byte[targetCount][];
            _boolValueArrays = new bool[targetCount][];
            _intValueArrays = new int[targetCount][];
            _floatValueArrays = new float[targetCount][];
            _vector2ValueArrays = new Vector2[targetCount][];
            _vector3ValueArrays = new Vector3[targetCount][];
            _quaternionValueArrays = new Quaternion[targetCount][];
            _stringValueArrays = new string[targetCount][];
            _cachedBindingFieldNames = new string[targetCount];
            for (int i = 0; i < targetCount; i++)
            {
                _cachedBindingFieldNames[i] = bindingFieldNames != null && i < bindingFieldNames.Length ? bindingFieldNames[i] : null;
            }
#if UDONSHARP || COMPILER_UDONSHARP
            _cachedBindingUdonTargets = BindingTable.BuildUdonTargetCache(bindingTargets, bindingUdonTargets, targetCount);
#else
            _cachedBindingComponentTargets = BindingTable.BuildComponentTargetCache(bindingTargets, targetCount);
#endif

            EnsureBindingLookupCache(targetCount);
        }

        private void EnsureBindingLookupCache(int targetCount)
        {
            DecoderBindingRuntime.EnsureBindingLookup(
                targetCount,
                bindingNetworkIds,
                bindingVariableHashes,
                _bindingLookupNetworkIds,
                _bindingLookupVariableHashes,
                _bindingLookupBindingIndices,
                _bindingLookupCount,
                _bindingLookupSignature,
                out _bindingLookupNetworkIds,
                out _bindingLookupVariableHashes,
                out _bindingLookupBindingIndices,
                out _bindingLookupCount,
                out _bindingLookupSignature);
        }

        private bool FailNetworkFrame(string error)
        {
            lastFrameValid = false;
            lastError = error;
            LogDecodeError(lastError);
            return false;
        }

    }
}
