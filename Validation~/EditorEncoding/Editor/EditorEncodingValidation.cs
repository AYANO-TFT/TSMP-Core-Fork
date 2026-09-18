using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
#if UDONSHARP
using UdonSharpEditor;
#endif
using Object = UnityEngine.Object;

public static class EditorEncodingValidation
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<string> Results = new List<string>();

    public static void Run()
    {
        string output = Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT");
        GameObject root = null;
        GameObject source = null;
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            source = new GameObject("Editor Encoding Source");
#if UDONSHARP
            var sync = source.AddUdonSharpComponent<TSMPNetworkTransformSync>();
#else
            var sync = source.AddComponent<TSMPNetworkTransformSync>();
#endif
            sync.networkId = 401;
            sync.target = source.transform;
            source.transform.position = new Vector3(1, 2, 3);
            root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Packages/com.kibalab.tsmp.core/Samples/TSMPController.prefab"));
            var setup = root.GetComponent<TSMPSetup>();
            setup.width = 640;
            setup.height = 360;
            setup.ApplyNow();
            var encoder = (TSMPEncoder)setup.encoder;
            Require(encoder != null && encoder.selectedCodec != null, "Shared Controller prepared with real Luma4");
            encoder.autoEncode = true;
            encoder.frameRate = 1;
            encoder.debugLog = false;
            encoder.transSyncRefreshInterval = 0;
            setup.driveEncoderInEditor = true;

            SetDeadline(setup, encoder, 0);
            uint before = encoder.frameIndex;
            InvokeDrivers(setup, encoder);
            Results.Add("Initial tick frame delta=" + (encoder.frameIndex - before));
            Require(encoder.frameIndex == before + 1, "Exactly one frame from all registered editor drivers: " + encoder.lastError);
#if UDONSHARP
            const string headerField = "_headerBytes";
#else
            const string headerField = "_header";
#endif
            var headerBytes = (byte[])typeof(TSMPEncoder).GetField(headerField, PrivateInstance).GetValue(encoder);
            Require(FrameHeader.TryRead(headerBytes, 0, out FrameHeader header), "Encoded header passes CRC validation");
            Require(header.FrameIndex == before && header.PayloadSize > NetworkFrameProtocol.NetworkHeaderBytes,
                "Encoded frame contains a nonempty payload at the expected frame index");
            Require(Drivers(setup, encoder).Length == 1, "Exactly one automatic editor callback owns encoding");

            SetDeadline(setup, encoder, EditorApplication.timeSinceStartup + 60);
            before = encoder.frameIndex;
            InvokeDrivers(setup, encoder);
            Require(encoder.frameIndex == before, "Frame-rate deadline prevents early encoding");

            encoder.autoEncode = false;
            SetDeadline(setup, encoder, 0);
            InvokeDrivers(setup, encoder);
            Require(encoder.frameIndex == before, "Auto Encode disabled prevents automatic encoding");
            encoder.EncodeNow();
            Require(encoder.frameIndex == before, "Unchanged state does not produce an empty frame");
            source.transform.position += Vector3.right;
            encoder.EncodeNow();
            Require(encoder.frameIndex == before + 1, "Manual Encode Now works with automatic encoding disabled");
            source.transform.position += Vector3.right;
            setup.EncodeEncoderNow();
            Require(encoder.frameIndex == before + 2, "Setup manual encoding produces exactly one frame");
            before = encoder.frameIndex;
            sync.sendMode = SendMode.Always;
            encoder.EncodeNow();
            setup.EncodeEncoderNow();
            Require(encoder.frameIndex == before + 2, "Always repeats a stationary Transform without rebuilding bindings");
            sync.sendMode = SendMode.OnChange;
            encoder.EncodeNow();
            Require(encoder.frameIndex == before + 2, "On Change suppresses unchanged Transform output immediately");
            sync.sendMode = SendMode.Default;
            encoder.EncodeNow();
            Require(encoder.frameIndex == before + 2, "Default restores the Transform field's original policy");
            encoder.autoEncode = true;

#if UDONSHARP
            Behaviour owner = setup;
            setup.driveEncoderInEditor = false;
            before = encoder.frameIndex;
            SetDeadline(setup, encoder, 0);
            InvokeDrivers(setup, encoder);
            Require(encoder.frameIndex == before, "Udon editor delegate respects Drive Encoder In Editor");
            setup.driveEncoderInEditor = true;
#else
            Behaviour owner = encoder;
            setup.driveEncoderInEditor = false;
            setup.enabled = false;
            before = encoder.frameIndex;
            source.transform.position += Vector3.right;
            SetDeadline(setup, encoder, 0);
            InvokeDrivers(setup, encoder);
            Require(encoder.frameIndex == before + 1, "Native encoder remains independent of Setup driving");
            setup.enabled = true;
            setup.driveEncoderInEditor = true;
#endif
            owner.enabled = false;
            Require(Drivers(setup, encoder).Length == 0, "Disabling the owner unregisters its callback");
            owner.enabled = true;
            owner.enabled = false;
            owner.enabled = true;
            Require(Drivers(setup, encoder).Length == 1, "Repeated enable cycles do not duplicate callbacks");
            source.transform.position += Vector3.right;
            SetDeadline(setup, encoder, 0);
            before = encoder.frameIndex;
            InvokeDrivers(setup, encoder);
            Require(encoder.frameIndex == before + 1, "Encoding resumes exactly once after re-enable");

#if !UDONSHARP
            Object.DestroyImmediate(setup);
            source.transform.position += Vector3.right;
            SetDeadline(null, encoder, 0);
            before = encoder.frameIndex;
            InvokeDrivers(null, encoder);
            Require(encoder.frameIndex == before + 1, "Native encoder works without a Setup component");
#endif
            Object.DestroyImmediate(root);
            root = null;
            Require(Drivers(setup, encoder).Length == 0, "Destroying the controller unregisters encoding callbacks");
#if UDONSHARP
            const string mode = "UdonSharp editor proxy driven by Setup";
#else
            const string mode = "SDK-free native Encoder";
#endif
            File.WriteAllText(output, "PASS\nUnity=" + Application.unityVersion + "\nMode=" + mode + "\n" + string.Join("\n", Results));
        }
        catch (Exception exception)
        {
            File.WriteAllText(output, "FAIL\n" + string.Join("\n", Results) + "\n" + exception);
            throw;
        }
        finally
        {
            if (root != null) Object.DestroyImmediate(root);
            if (source != null) Object.DestroyImmediate(source);
        }
    }

    private static Delegate[] Drivers(TSMPSetup setup, TSMPEncoder encoder)
    {
        var update = (Delegate)typeof(EditorApplication).GetField("update", BindingFlags.Public | BindingFlags.Static).GetValue(null);
        return update == null ? Array.Empty<Delegate>() : update.GetInvocationList()
            .Where(callback => callback.Method.Name == "EditorUpdate" &&
                (ReferenceEquals(callback.Target, setup) || ReferenceEquals(callback.Target, encoder))).ToArray();
    }

    private static void InvokeDrivers(TSMPSetup setup, TSMPEncoder encoder)
    {
        foreach (Delegate callback in Drivers(setup, encoder)) callback.DynamicInvoke();
    }

    private static void SetDeadline(TSMPSetup setup, TSMPEncoder encoder, double time)
    {
        if (setup != null) typeof(TSMPSetup).GetField("_nextEditorEncodeTime", PrivateInstance)?.SetValue(setup, time);
#if !UDONSHARP
        typeof(TSMPEncoder).GetField("_nextEncodeTime", PrivateInstance).SetValue(encoder, time);
#endif
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Results.Add("PASS " + message);
    }
}
