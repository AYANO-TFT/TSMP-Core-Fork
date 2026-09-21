using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEngine;
#if UDONSHARP
using VRC.Udon;
using VRC.SDKBase;
#endif
using Endpoint = ReceivePolicyValidation.Endpoint;
using Object = UnityEngine.Object;

public static class SendCommitCases
{
    public static void Notifications()
    {
        using (var source = new Endpoint(typeof(SendCommitProbe)))
        using (var encoder = new Endpoint(typeof(TSMPEncoder)))
        using (var codec = new Endpoint(typeof(SendCommitCodec)))
        {
            var normal = Output(360);
            var tiny = Output(56);
            try
            {
                Configure(encoder, source, codec, normal, typeof(SendCommitProbe), new[] { "bytes", "other" });
                source.Get<byte[]>("bytes")[0] = 17;
                codec.Set("fail", true);
                encoder.Call("EncodeNow");
                Check(source.Get<int>("captures") == 1 && source.Get<int>("commits") == 0, "Failed codec consumed capture: captures=" + source.Get<int>("captures") + " commits=" + source.Get<int>("commits") + " error=" + encoder.Get<string>("lastError"));
                Check(encoder.Get<uint>("frameIndex") == 0, "Failed codec advanced frame");
                codec.Set("fail", false);
                encoder.Set("output", tiny);
                encoder.Call("EncodeNow");
                Check(encoder.Get<int>("deferredVariableCount") == 1 && source.Get<int>("commits") == 0, "Deferred field was notified");
                Check(encoder.Get<int>("autoVariableCount") == 1, "Unrelated field did not fit");
                encoder.Set("output", null);
                encoder.Call("EncodeNow");
                Check(source.Get<int>("commits") == 0, "Missing output notified");
                source.Get<byte[]>("bytes")[0] = 29;
                encoder.Set("output", normal);
                encoder.Call("EncodeNow");
                Check(source.Get<int>("commits") == 1 && source.Get<int>("committedValue") == 29, "Latest deferred value not committed");
                encoder.Call("EncodeNow");
                Check(source.Get<int>("commits") == 1, "Unchanged field notified again");
                source.Get<byte[]>("bytes")[0] = 31;
                source.Set("transRpcEncoder", encoder.Target);
                source.Set("reenter", true);
                int captures = source.Get<int>("captures");
                encoder.Call("EncodeNow");
                Check(source.Get<int>("commits") == 2 && source.Get<int>("committedValue") == 31, "Changed field not notified");
                Check(source.Get<int>("captures") == captures + 1, "Callback reentered the encoder");
#if !UDONSHARP
                var snapshot = TransSyncBindingSnapshotBuilder.Build(false);
                int slot = Array.IndexOf(snapshot.FieldNames, "bytes");
                Check(slot >= 0 && snapshot.SentEvents[slot] == nameof(SendCommitProbe.CommitBytes), "Snapshot dropped sent event");
                var builder = typeof(K13A.TSMP.Editor.TransSyncBindingBuilder).GetMethod("AssignEncoderBindings", BindingFlags.Static | BindingFlags.NonPublic);
                builder.Invoke(null, new object[] { encoder.Target, new[] { (TSMPNetworkBehaviour)source.Target }, 0 });
                slot = Array.IndexOf(encoder.Get<string[]>("bindingFieldNames"), "bytes");
                Check(slot >= 0 && encoder.Get<string[]>("bindingSentEvents")[slot] == nameof(SendCommitProbe.CommitBytes), "Editor binding generation dropped sent event");
#endif
            }
            finally { DestroyOutput(normal); DestroyOutput(tiny); }
        }
    }

    private static void Configure(Endpoint encoder, Endpoint source, Endpoint codec, RenderTexture output, Type type, string[] names)
    {
        var fields = TransSyncMetadata.GetOrCreate(null, type).Fields;
        source.Set("networkId", (ushort)123);
        encoder.Set("output", output);
        encoder.Set("autoEncode", false);
        encoder.Set("clearAfterEncode", true);
        encoder.Set("transSyncRefreshInterval", 0f);
        encoder.Set("blockSize", 8);
        encoder.Set("bindingFieldNames", names);
        encoder.Set("bindingNetworkIds", names.Select(n => (ushort)123).ToArray());
        encoder.Set("bindingVariableHashes", names.Select(n => fields.Single(f => f.FieldInfo.Name == n).VariableHash).ToArray());
        encoder.Set("bindingValueTypes", names.Select(n => (byte)fields.Single(f => f.FieldInfo.Name == n).ValueType).ToArray());
        encoder.Set("bindingSentEvents", names.Select(n => fields.Single(f => f.FieldInfo.Name == n).Sync.SentEvent).ToArray());
        encoder.Set("bindingSendOnChange", names.Select(n => fields.Single(f => f.FieldInfo.Name == n).Sync.SendOnChange).ToArray());
        encoder.Set("bindingDirections", new int[names.Length]);
#if UDONSHARP
        encoder.Set("bindingTargets", new Component[0]);
        encoder.Set("bindingUdonTargets", names.Select(n => (UdonBehaviour)source.Target).ToArray());
        encoder.Set("selectedCodecUdonTarget", codec.Target);
        encoder.Call("_onEnable");
#else
        encoder.Set("bindingTargets", names.Select(n => source.Target).ToArray());
        encoder.Set("selectedCodec", codec.Target);
#endif
    }

#if UDONSHARP
    public static void Avatar()
    {
        var oldId = VRCPlayerApi._GetPlayerId;
        var oldTracking = VRCPlayerApi._GetTrackingData;
        var oldBonePosition = VRCPlayerApi._GetBonePosition;
        var oldBoneRotation = VRCPlayerApi._GetBoneRotation;
        var owner = new GameObject("Pose source player");
        var fixturePlayer = new VRCPlayerApi { gameObject = owner, displayName = "7", isLocal = true };
        var fixturePlayers = new List<VRCPlayerApi> { fixturePlayer };
        fixturePlayer.AddToList();
        VRCPlayerApi._GetPlayerId = player => int.Parse(player.displayName);
        VRCPlayerApi._GetTrackingData = (player, point) => new VRCPlayerApi.TrackingData(owner.transform.position, owner.transform.rotation);
        VRCPlayerApi._GetBonePosition = (player, bone) => owner.transform.position + Vector3.up;
        VRCPlayerApi._GetBoneRotation = (player, bone) => owner.transform.rotation;
        try
        {
            using (var source = new Endpoint(typeof(TSMPNetworkVrchatAvatarPoseSync)))
            using (var encoder = new Endpoint(typeof(TSMPEncoder)))
            using (var codec = new Endpoint(typeof(SendCommitCodec)))
            {
                var normal = Output(360);
                var tiny = Output(56);
                try
                {
                    Configure(encoder, source, codec, normal, typeof(TSMPNetworkVrchatAvatarPoseSync), new[] { "avatarPoseBytes" });
                    var players = new VRCPlayerApi[80];
                    players[0] = fixturePlayer;
                    Check(Utilities.IsValid(players[0]), "SDK player fixture invalid");
                    source.Set("_players", players);
                    source.Set("_cachedPlayerCount", 1);
                    source.Set("_playerCacheInitialized", true);
                    owner.transform.position = Vector3.right;
                    source.Call("TSMPBeforeEncode");
                    source.Call("TSMPBeforeEncode");
                    Check(source.Get<int>("encodedRootPoseCount") == 1, "Unsent stationary root was omitted");
                    Check(source.Get<int[]>("_lastRootPosePlayerIds")[0] == -1 && source.Get<int[]>("_lastPlayerEntrySequences")[0] == -1, "Capture committed root or keepalive");
                    encoder.Set("output", tiny);
                    encoder.Call("EncodeNow");
                    Check(encoder.Get<int>("deferredVariableCount") == 1 && source.Get<int[]>("_lastRootPosePlayerIds")[0] == -1, "Capacity deferral committed root");
                    encoder.Set("output", normal);
                    codec.Set("fail", true);
                    encoder.Call("EncodeNow");
                    Check(source.Get<int[]>("_lastPlayerEntrySequences")[0] == -1, "Codec failure committed keepalive");
                    owner.transform.position = Vector3.right * 2;
                    codec.Set("fail", false);
                    encoder.Call("EncodeNow");
                    Check(source.Get<Vector3[]>("_lastRootPositions")[0] == owner.transform.position, "Latest moving root not committed");
                    Check(source.Get<int>("encodedPoseRecordCount") > 6, "Default full humanoid capture missing");
                    int committedSequence = source.Get<int[]>("_lastPlayerEntrySequences")[0];
                    source.Set("poseMode", VrchatAvatarPoseMode.None);
                    encoder.Call("EncodeNow");
                    Check(source.Get<int>("encodedRootPoseCount") == 0 && source.Get<int[]>("_lastPlayerEntrySequences")[0] == committedSequence, "Stationary root or early keepalive sent");
                    source.Set("_sequence", (ushort)(committedSequence + 30));
                    encoder.Call("EncodeNow");
                    Check(source.Get<int>("encodedRootPoseCount") == 1, "Periodic root keyframe lost");
                    for (int i = 1; i < 17; i++)
                    {
                        var child = new GameObject("Player " + (7 + i));
                        child.transform.SetParent(owner.transform);
                        players[i] = new VRCPlayerApi { gameObject = child, displayName = (7 + i).ToString() };
                        players[i].AddToList();
                        fixturePlayers.Add(players[i]);
                    }
                    source.Set("_cachedPlayerCount", 17);
                    source.Set("maxPlayers", 17);
                    source.Set("_sequence", (ushort)30);
                    for (int i = 0; i < 17; i++)
                    {
                        source.Get<int[]>("_lastRootPosePlayerIds")[i] = 7 + i;
                        source.Get<int[]>("_lastPlayerEntryPlayerIds")[i] = 7 + i;
                        source.Get<int[]>("_lastRootPoseSequences")[i] = 0;
                        source.Get<int[]>("_lastPlayerEntrySequences")[i] = 0;
                        source.Get<Vector3[]>("_lastRootPositions")[i] = owner.transform.position;
                        source.Get<Quaternion[]>("_lastRootRotations")[i] = Quaternion.identity;
                    }
                    codec.Set("fail", true);
                    encoder.Call("EncodeNow");
                    Check(source.Get<int>("keepAlivePlayerEntryCount") == 1, "Unsampled player keepalive missing");
                    Check(source.Get<int[]>("_lastPlayerEntrySequences").Take(17).All(s => s == 0), "Failed keepalive consumed state");
                    codec.Set("fail", false);
                    encoder.Call("EncodeNow");
                    Check(source.Get<int>("keepAlivePlayerEntryCount") == 1 && source.Get<int[]>("_lastPlayerEntrySequences").Take(17).All(s => s == 31), "Pending keepalive not retried");
                    Check(source.Get<int[]>("_lastRootPoseSequences").Take(17).Count(s => s == 31) == 16, "Unsampled root was committed");
                }
                finally { DestroyOutput(normal); DestroyOutput(tiny); }
            }
        }
        finally
        {
            VRCPlayerApi._GetPlayerId = oldId;
            VRCPlayerApi._GetTrackingData = oldTracking;
            VRCPlayerApi._GetBonePosition = oldBonePosition;
            VRCPlayerApi._GetBoneRotation = oldBoneRotation;
            foreach (var player in fixturePlayers) player.RemoveFromList();
            Object.DestroyImmediate(owner);
        }
    }
#endif

    private static RenderTexture Output(int height)
    {
        var texture = new RenderTexture(640, height, 0, RenderTextureFormat.ARGB32);
        texture.Create();
        return texture;
    }

    private static void DestroyOutput(RenderTexture texture)
    {
        if (RenderTexture.active == texture) RenderTexture.active = null;
        texture.Release();
        Object.DestroyImmediate(texture);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
