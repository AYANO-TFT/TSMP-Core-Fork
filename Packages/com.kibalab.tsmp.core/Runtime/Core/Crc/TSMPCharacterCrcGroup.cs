using UnityEngine;
using K13A.TSMP.Udon;

namespace K13A.TSMP
{
    public enum FingerProtection { Excluded = 0, SeparateCrc = 1 }

    [AddComponentMenu("TSMP/Character CRC Group")]
    public class TSMPCharacterCrcGroup : TSMPBehaviour
    {
        public TSMPNetworkHumanoidPoseSync pose;
        public FingerProtection fingerProtection = FingerProtection.SeparateCrc;
        [Tooltip("Sent together with this Pose and accepted/rejected together with Body. Expressions are not included.")]
        public TSMPNetworkTransformSync[] additionalTransforms = new TSMPNetworkTransformSync[0];
    }
}
