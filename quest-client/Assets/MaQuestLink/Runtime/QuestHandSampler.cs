using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

namespace MaQuestLink.QuestClient
{
    public static class QuestHandSampler
    {
        private static readonly List<XRHandSubsystem> Subsystems = new List<XRHandSubsystem>();

        public static HandTrackingInput Sample(ulong timestampNs)
        {
            var result = new HandTrackingInput { SampleTimestampNs = timestampNs };
            SubsystemManager.GetSubsystems(Subsystems);
            XRHandSubsystem subsystem = null;
            foreach (var candidate in Subsystems)
            {
                if (candidate.running)
                {
                    subsystem = candidate;
                    break;
                }
            }
            if (subsystem == null)
            {
                return result;
            }
            result.LeftActive = CopyHand(subsystem.leftHand, result.LeftJoints);
            result.RightActive = CopyHand(subsystem.rightHand, result.RightJoints);
            return result;
        }

        public static int UnityJointIdForOpenXrIndex(int index)
        {
            if (index < 0 || index >= Protocol.HandJointCount)
            {
                return (int)XRHandJointID.Invalid;
            }
            if (index == 0) return (int)XRHandJointID.Palm;
            if (index == 1) return (int)XRHandJointID.Wrist;
            return index + 1;
        }

        private static bool CopyHand(XRHand hand, HandJointState[] output)
        {
            if (!hand.isTracked || output == null || output.Length != Protocol.HandJointCount)
            {
                return false;
            }
            for (var index = 0; index < Protocol.HandJointCount; index++)
            {
                var id = (XRHandJointID)UnityJointIdForOpenXrIndex(index);
                var joint = hand.GetJoint(id);
                var state = new HandJointState();
                if (joint.TryGetPose(out var pose))
                {
                    state.Pose = new PoseState
                    {
                        Position = new Vector3f
                        {
                            X = pose.position.x,
                            Y = pose.position.y,
                            Z = -pose.position.z,
                        },
                        Orientation = new Quaternionf
                        {
                            X = -pose.rotation.x,
                            Y = -pose.rotation.y,
                            Z = pose.rotation.z,
                            W = pose.rotation.w,
                        },
                        Flags = PoseFlags.PositionValid | PoseFlags.OrientationValid |
                                PoseFlags.PositionTracked | PoseFlags.OrientationTracked,
                    };
                }
                if (joint.TryGetRadius(out var radius)) state.Radius = radius;
                output[index] = state;
            }
            return true;
        }
    }
}
