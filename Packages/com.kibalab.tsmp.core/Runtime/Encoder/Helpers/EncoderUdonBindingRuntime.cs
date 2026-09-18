#if UDONSHARP || COMPILER_UDONSHARP
using UnityEngine;
using VRC.Udon;
using K13A.TSMP.Udon;

namespace K13A.TSMP
{
    public static class EncoderUdonBindingRuntime
    {
        public static int GetWritableBindingCount(
            Component[] bindingTargets,
            UdonBehaviour[] bindingUdonTargets,
            ushort[] bindingNetworkIds,
            uint[] bindingVariableHashes,
            byte[] bindingValueTypes,
            string[] bindingFieldNames)
        {
            return BindingTable.GetWritableBindingCount(bindingTargets, bindingUdonTargets, true, bindingNetworkIds, bindingVariableHashes, bindingValueTypes, bindingFieldNames);
        }

        public static UdonBehaviour[] EnsureBindingTargetCache(
            Component[] bindingTargets,
            UdonBehaviour[] bindingUdonTargets,
            int count,
            UdonBehaviour[] cachedTargets,
            int cachedCount,
            out int nextCachedCount)
        {
            if (count < 0)
                count = 0;

            nextCachedCount = cachedCount;

            bool cacheValid = true;
            if (cachedCount != count)
                cacheValid = false;
            if (!BindingTable.MatchesUdonTargets(cachedTargets, bindingTargets, bindingUdonTargets, count))
                cacheValid = false;

            if (cacheValid)
                return cachedTargets;

            nextCachedCount = count;
            return BindingTable.BuildUdonTargetCache(bindingTargets, bindingUdonTargets, count);
        }

        public static UdonBehaviour[] EnsureBeforeEncodeTargetCache(UdonBehaviour[] targets, int count)
        {
            if (count < 0)
                count = 0;

            return BindingTable.EnsureUdonTargetArray(targets, count);
        }

        public static bool CanWriteBinding(string[] bindingFieldNames, int[] bindingDirections, UdonBehaviour target, int index)
        {
            return BindingTable.CanWriteBindingEntry(bindingDirections, bindingFieldNames, target, index);
        }

        public static int SendBeforeEncodeOnce(UdonBehaviour target, UdonBehaviour[] targets, int targetCount)
        {
            int sendMode;
            return SendBeforeEncodeOnce(target, targets, targetCount, null, out sendMode);
        }

        public static int SendBeforeEncodeOnce(UdonBehaviour target, UdonBehaviour[] targets, int targetCount, int[] sendModes, out int sendMode)
        {
            sendMode = (int)SendMode.Default;
            if (target == null)
                return targetCount;

            for (int i = 0; i < targetCount; i++)
            {
                if ((Object)targets[i] != (Object)target)
                    continue;
                if (sendModes != null)
                    sendMode = sendModes[i];
                return targetCount;
            }

            TSMPBehaviour.SendCustomEvent(target, TSMPNetworkBehaviour.BeforeEncodeEventName);
            if (sendModes != null)
            {
                sendMode = GetSendMode(target);
                sendModes[targetCount] = sendMode;
            }
            return BindingTable.AddUdonTarget(targets, targetCount, target);
        }

        public static int GetSendMode(UdonBehaviour target)
        {
            if (target == null)
                return (int)SendMode.Default;
#if UNITY_EDITOR && !COMPILER_UDONSHARP
            if (!Application.isPlaying)
            {
                TSMPNetworkBehaviour proxy = UdonProxySyncBridge.ResolveProxy(target) as TSMPNetworkBehaviour;
                return proxy == null ? (int)SendMode.Default : (int)proxy.sendMode;
            }
#endif
#if COMPILER_UDONSHARP
            if (target.GetProgramVariableType(nameof(TSMPNetworkBehaviour.sendMode)) == null)
                return (int)SendMode.Default;
            object mode = target.GetProgramVariable(nameof(TSMPNetworkBehaviour.sendMode));
#else
            object mode;
            if (!target.TryGetProgramVariable(nameof(TSMPNetworkBehaviour.sendMode), out mode))
                return (int)SendMode.Default;
#endif
            return mode == null ? (int)SendMode.Default : (int)mode;
        }
    }
}
#endif
