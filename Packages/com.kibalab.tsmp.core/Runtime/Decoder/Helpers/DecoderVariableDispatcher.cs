using UnityEngine;
using K13A.TSMP.Udon;

#if UDONSHARP || COMPILER_UDONSHARP
using VRC.Udon;
#endif

namespace K13A.TSMP
{
    public static class DecoderVariableDispatcher
    {
#if UDONSHARP || COMPILER_UDONSHARP
        public static bool IsTargetActive(UdonBehaviour[] targets, int index)
        {
            if (targets == null)
                return false;
            if (index < 0 || index >= targets.Length)
                return false;

            UdonBehaviour target = targets[index];
            if (target == null)
                return false;

            if (!BindingTable.IsUdonTargetActive(target))
                return false;

#if UNITY_EDITOR && !COMPILER_UDONSHARP
            if (!Application.isPlaying)
            {
                TSMPNetworkBehaviour proxy = UdonProxySyncBridge.ResolveProxy(target) as TSMPNetworkBehaviour;
                return proxy == null || proxy.receiveInterpolation != ReceiveInterpolationMode.None;
            }
#endif
#if COMPILER_UDONSHARP
            if (target.GetProgramVariableType(nameof(TSMPNetworkBehaviour.receiveInterpolation)) == null)
                return true;
            object receiveMode = target.GetProgramVariable(nameof(TSMPNetworkBehaviour.receiveInterpolation));
#else
            object receiveMode;
            if (!target.TryGetProgramVariable(nameof(TSMPNetworkBehaviour.receiveInterpolation), out receiveMode))
                return true;
#endif
            return receiveMode == null || (int)receiveMode != (int)ReceiveInterpolationMode.None;
        }

        public static bool Apply(UdonBehaviour[] targets, int index, string fieldName, uint variableHash, object decodedValue)
        {
            if (decodedValue == null)
                return false;
            if (!IsTargetActive(targets, index))
                return false;

            return ApplyToActiveTarget(targets[index], fieldName, variableHash, decodedValue);
        }

        // The caller must have checked this target with IsTargetActive in the same
        // synchronous operation, without dispatching events or changing target state.
        internal static bool ApplyToActiveTarget(UdonBehaviour target, string fieldName, uint variableHash, object decodedValue)
        {
            if (decodedValue == null)
                return false;

            TSMPBehaviour.SetProgramVariable(target, fieldName, decodedValue);
            TSMPBehaviour.SetProgramVariable(target, TSMPNetworkBehaviour.LastVariableHashFieldName, variableHash);
            TSMPBehaviour.SendCustomEvent(target, TSMPNetworkBehaviour.OnVariableReceivedEventName);
            return true;
        }
#else
        public static bool IsTargetActive(Component[] targets, int index)
        {
            if (targets == null)
                return false;
            if (index < 0 || index >= targets.Length)
                return false;

            Component target = targets[index];
            if (target == null)
                return false;

            if (!BindingTable.IsComponentTargetActive(target))
                return false;

            TSMPNetworkBehaviour behaviour = target as TSMPNetworkBehaviour;
            return behaviour == null || behaviour.receiveInterpolation != ReceiveInterpolationMode.None;
        }

        public static bool Apply(Component[] targets, int index, string fieldName, uint variableHash, object decodedValue)
        {
            if (decodedValue == null)
                return false;
            if (!IsTargetActive(targets, index))
                return false;

            return ApplyToActiveTarget(targets[index], fieldName, variableHash, decodedValue);
        }

        // Same precondition as the Udon path: eligibility was checked immediately
        // before value decoding, which does not invoke recipient code.
        internal static bool ApplyToActiveTarget(Component target, string fieldName, uint variableHash, object decodedValue)
        {
            if (decodedValue == null)
                return false;

            TSMPBehaviour.SetProgramVariable(target, fieldName, decodedValue);
            TSMPBehaviour.SetProgramVariable(target, TSMPNetworkBehaviour.LastVariableHashFieldName, variableHash);
            TSMPBehaviour.SendCustomEvent(target, TSMPNetworkBehaviour.OnVariableReceivedEventName);
            return true;
        }
#endif
    }
}
