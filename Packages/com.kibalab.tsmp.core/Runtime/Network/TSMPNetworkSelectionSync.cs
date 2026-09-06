using UnityEngine;

#if UDONSHARP
using UdonSharp;
#endif

namespace K13A.TSMP.Udon
{
    public class TSMPNetworkSelectionSync : TSMPNetworkBehaviour
    {
        public const int EmptySelectionId = 0;
        public const int MaxSelectionId = 65535;
        public const int MaxSlotCount = 255;

        public int slotCount = 2;
        public int[] selectedIds = new int[2];
        public TSMPBehaviour[] selectionChangedTargets;
        public string selectionChangedEventName = "_OnTSMPSelectionChanged";

        [HideInInspector] public int activeSelectionCount;
        [HideInInspector] public int encodedSelectionBytes;
        [HideInInspector] public int changeCounter;
        [HideInInspector] public int notifiedChangeCounter = -1;

        [HideInInspector]
        [TransSync("selection.slots")]
#if UDONSHARP
        [FieldChangeCallback(nameof(SelectionBytes))]
#endif
        public byte[] selectionBytes;

        private const byte SelectionVersion = 1;
        private const int HeaderBytes = 8;
        private const int SelectionIdBytes = 2;

        private int _lastSlotCount = -1;
        private int[] _lastSelectedIds;
        private bool _selectionDirty;
        private bool _changeCounterAdvancedForDirtySelection;

        public byte[] SelectionBytes
        {
            get
            {
                return selectionBytes;
            }
            set
            {
                selectionBytes = value;
            }
        }

        private void Start()
        {
            EnsureSelectedIds(false);
            RefreshActiveSelectionCount();
        }

        public override void TSMPBeforeEncode()
        {
            if (!IsTSMPActive())
                return;

            int count = EnsureSelectedIds(true);
            bool selectionChanged = HasSelectionChangedSinceLastEncode(count);
            if (selectionChanged && !_changeCounterAdvancedForDirtySelection)
                IncrementChangeCounter();

            int requiredBytes = HeaderBytes + count * SelectionIdBytes;
            if (selectionBytes == null || selectionBytes.Length != requiredBytes)
                selectionBytes = new byte[requiredBytes];

            selectionBytes[0] = SelectionVersion;
            selectionBytes[1] = 0;
            Binary.WriteUInt16LE(selectionBytes, 2, (ushort)count);
            Binary.WriteInt32LE(selectionBytes, 4, changeCounter);

            int cursor = HeaderBytes;
            for (int i = 0; i < count; i++)
            {
                int selectionId = SanitizeSelectionId(selectedIds[i]);
                selectedIds[i] = selectionId;
                Binary.WriteUInt16LE(selectionBytes, cursor, (ushort)selectionId);
                cursor += SelectionIdBytes;
            }

            encodedSelectionBytes = requiredBytes;
            activeSelectionCount = CountActiveSelections(count);
            StoreCurrentSelection(count);
            if (selectionChanged)
                NotifySelectionChangedIfNeeded();
        }

        public override void OnTSMPVariableReceived()
        {
            if (receiveInterpolation == ReceiveInterpolationMode.None)
                return;

            bool selectionChanged = ApplySelectionBytes();
            if (selectionChanged)
                NotifySelectionChangedIfNeeded();

            OnTSMPVariableChanged(lastVariableHash);
        }

        public bool SetSlotCount(int count)
        {
            if (count < 0 || count > MaxSlotCount)
                return false;

            if (slotCount == count && selectedIds != null && selectedIds.Length == count)
                return false;

            slotCount = count;
            EnsureSelectedIds(true);
            MarkSelectionChanged();
            return true;
        }

        public bool SetSlotSelection(int slotIndex, int selectionId)
        {
            if (selectionId < EmptySelectionId || selectionId > MaxSelectionId)
                return false;

            int count = EnsureSelectedIds(true);
            if (slotIndex < 0 || slotIndex >= count)
                return false;

            if (selectedIds[slotIndex] == selectionId)
                return false;

            selectedIds[slotIndex] = selectionId;
            MarkSelectionChanged();
            return true;
        }

        public int GetSlotSelection(int slotIndex)
        {
            int count = EnsureSelectedIds(false);
            if (slotIndex < 0 || slotIndex >= count)
                return EmptySelectionId;

            return selectedIds[slotIndex];
        }

        public int GetActiveSelectionCount()
        {
            int count = EnsureSelectedIds(false);
            activeSelectionCount = CountActiveSelections(count);
            return activeSelectionCount;
        }

        public bool IsSelectionActive(int selectionId)
        {
            if (selectionId <= EmptySelectionId || selectionId > MaxSelectionId)
                return false;

            int count = EnsureSelectedIds(false);
            for (int i = 0; i < count; i++)
            {
                if (selectedIds[i] == selectionId)
                    return true;
            }

            return false;
        }

        public void _ClearSelections()
        {
            int count = EnsureSelectedIds(true);
            bool changed = false;
            for (int i = 0; i < count; i++)
            {
                if (selectedIds[i] == EmptySelectionId)
                    continue;

                selectedIds[i] = EmptySelectionId;
                changed = true;
            }

            if (changed)
                MarkSelectionChanged();
            else
                RefreshActiveSelectionCount();
        }

        private bool ApplySelectionBytes()
        {
            if (!IsTSMPActive() || selectionBytes == null || selectionBytes.Length < HeaderBytes)
                return false;

            if (selectionBytes[0] != SelectionVersion)
                return false;

            int count = Binary.ReadUInt16LE(selectionBytes, 2);
            if (count < 0 || count > MaxSlotCount)
                return false;

            int requiredBytes = HeaderBytes + count * SelectionIdBytes;
            if (selectionBytes.Length < requiredBytes)
                return false;

            int receivedChangeCounter = Binary.ReadInt32LE(selectionBytes, 4);
            bool selectionChanged = IsIncomingSelectionChanged(count, receivedChangeCounter);
            slotCount = count;
            EnsureSelectedIds(false);

            int cursor = HeaderBytes;
            for (int i = 0; i < count; i++)
            {
                selectedIds[i] = Binary.ReadUInt16LE(selectionBytes, cursor);
                cursor += SelectionIdBytes;
            }

            changeCounter = receivedChangeCounter;
            encodedSelectionBytes = requiredBytes;
            activeSelectionCount = CountActiveSelections(count);
            StoreCurrentSelection(count);
            return selectionChanged;
        }

        private bool IsIncomingSelectionChanged(int count, int receivedChangeCounter)
        {
            if (changeCounter != receivedChangeCounter)
                return true;

            if (slotCount != count)
                return true;

            if (selectedIds == null || selectedIds.Length < count)
                return true;

            int cursor = HeaderBytes;
            for (int i = 0; i < count; i++)
            {
                int selectionId = Binary.ReadUInt16LE(selectionBytes, cursor);
                if (selectedIds[i] != selectionId)
                    return true;

                cursor += SelectionIdBytes;
            }

            return false;
        }

        private void MarkSelectionChanged()
        {
            _selectionDirty = true;
            if (!_changeCounterAdvancedForDirtySelection)
            {
                IncrementChangeCounter();
                _changeCounterAdvancedForDirtySelection = true;
            }

            RefreshActiveSelectionCount();
            notifiedChangeCounter = changeCounter;
            NotifySelectionChanged();
        }

        private void NotifySelectionChangedIfNeeded()
        {
            if (notifiedChangeCounter == changeCounter)
                return;

            notifiedChangeCounter = changeCounter;
            NotifySelectionChanged();
        }

        private void NotifySelectionChanged()
        {
            if (selectionChangedTargets == null || string.IsNullOrEmpty(selectionChangedEventName))
                return;

            for (int i = 0; i < selectionChangedTargets.Length; i++)
            {
                TSMPBehaviour target = selectionChangedTargets[i];
                if (target == null)
                    continue;

#if UDONSHARP
                target.SendCustomEvent(selectionChangedEventName);
#else
                TSMPBehaviour.SendCustomEvent(target, selectionChangedEventName);
#endif
            }
        }

        private int EnsureSelectedIds(bool markDirty)
        {
            int count = ClampSlotCount(slotCount);
            if (slotCount != count)
            {
                slotCount = count;
                if (markDirty)
                    _selectionDirty = true;
            }

            if (selectedIds == null || selectedIds.Length != count)
            {
                int[] nextSelectedIds = new int[count];
                if (selectedIds != null)
                {
                    int copyCount = selectedIds.Length < count ? selectedIds.Length : count;
                    for (int i = 0; i < copyCount; i++)
                        nextSelectedIds[i] = SanitizeSelectionId(selectedIds[i]);
                }

                selectedIds = nextSelectedIds;
                if (markDirty)
                    _selectionDirty = true;
            }

            for (int i = 0; i < count; i++)
            {
                int selectionId = SanitizeSelectionId(selectedIds[i]);
                if (selectedIds[i] == selectionId)
                    continue;

                selectedIds[i] = selectionId;
                if (markDirty)
                    _selectionDirty = true;
            }

            return count;
        }

        private int ClampSlotCount(int count)
        {
            if (count < 0)
                return 0;

            if (count > MaxSlotCount)
                return MaxSlotCount;

            return count;
        }

        private int SanitizeSelectionId(int selectionId)
        {
            if (selectionId <= EmptySelectionId)
                return EmptySelectionId;

            if (selectionId > MaxSelectionId)
                return EmptySelectionId;

            return selectionId;
        }

        private bool HasSelectionChangedSinceLastEncode(int count)
        {
            if (_selectionDirty || _lastSlotCount != count || _lastSelectedIds == null || _lastSelectedIds.Length != count)
                return true;

            for (int i = 0; i < count; i++)
            {
                if (_lastSelectedIds[i] != selectedIds[i])
                    return true;
            }

            return false;
        }

        private void StoreCurrentSelection(int count)
        {
            if (_lastSelectedIds == null || _lastSelectedIds.Length != count)
                _lastSelectedIds = new int[count];

            for (int i = 0; i < count; i++)
                _lastSelectedIds[i] = selectedIds[i];

            _lastSlotCount = count;
            _selectionDirty = false;
            _changeCounterAdvancedForDirtySelection = false;
        }

        private void IncrementChangeCounter()
        {
            if (changeCounter >= int.MaxValue)
                changeCounter = 1;
            else
                changeCounter++;
        }

        private void RefreshActiveSelectionCount()
        {
            int count = EnsureSelectedIds(false);
            activeSelectionCount = CountActiveSelections(count);
        }

        private int CountActiveSelections(int count)
        {
            if (selectedIds == null)
                return 0;

            int activeCount = 0;
            for (int i = 0; i < count; i++)
            {
                if (selectedIds[i] != EmptySelectionId)
                    activeCount++;
            }

            return activeCount;
        }
    }
}
