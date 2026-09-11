using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class DecoderArrayCacheValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string[] FieldPrefixes = { "bytes", "bools", "ints", "floats", "vectors2", "vectors3", "rotations", "strings" };

    public static void Run()
    {
        var results = new List<string>();
        string output = Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT");
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            for (int i = 0; i < ArrayCacheCases.Types.Length; i++)
            {
                VerifyType(ArrayCacheCases.Types[i], FieldPrefixes[i]);
                results.Add("PASS " + FieldPrefixes[i] + ": isolation, fanout, reuse, resize, empty, multi-entry frame");
            }
            File.WriteAllText(output, "PASS\nUnity=" + Application.unityVersion + "\n" + string.Join("\n", results));
        }
        catch (Exception exception)
        {
            File.WriteAllText(output, "FAIL\n" + string.Join("\n", results) + "\n" + exception);
            throw;
        }
    }

    private static void VerifyType(int type, string prefix)
    {
        var owner = new GameObject("Array Cache Validation") { hideFlags = HideFlags.HideAndDontSave };
        var otherOwner = new GameObject("Array Cache Fanout") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var target = owner.AddComponent<ArrayCacheProbe>();
            var other = otherOwner.AddComponent<ArrayCacheProbe>();
            var decoder = owner.AddComponent<TSMPDecoder>();
            decoder.applyEveryFrame = false;
            decoder.debugLog = false;
            decoder.bindingTargets = new Component[] { target, target, other };
            decoder.bindingNetworkIds = new ushort[] { 1, 1, 1 };
            decoder.bindingVariableHashes = new uint[] { 100, 200, 100 };
            decoder.bindingValueTypes = new byte[] { (byte)type, (byte)type, (byte)type };
            decoder.bindingFieldNames = new[] { prefix + "A", prefix + "B", prefix + "A" };
            FieldInfo firstField = typeof(ArrayCacheProbe).GetField(prefix + "A");
            FieldInfo secondField = typeof(ArrayCacheProbe).GetField(prefix + "B");
            Array lastFanout = null;
            Array lastFirst = null;
            ArrayCacheCases.Exercise(type, (index, value) =>
            {
                Apply(decoder, BuildFrame(type, index == 0 ? 100u : 200u, value));
                var decoded = (Array)(index == 0 ? firstField : secondField).GetValue(target);
                if (index == 0)
                {
                    var fanout = (Array)firstField.GetValue(other);
                    ArrayCacheCases.AssertEqual(value, fanout);
                    ArrayCacheCases.Require(!ReferenceEquals(decoded, fanout), "Fanout recipients share an array");
                    if (lastFanout != null && lastFanout.Length == value.Length)
                        ArrayCacheCases.Require(ReferenceEquals(lastFanout, fanout), "Fanout did not reuse its array");
                    lastFanout = fanout;
                    lastFirst = (Array)value.Clone();
                }
                else if (lastFirst != null)
                {
                    ArrayCacheCases.AssertEqual(lastFirst, (Array)firstField.GetValue(other));
                }
                return decoded;
            });

            Array first = ArrayCacheCases.CreateValue(type, 2, 7);
            Array second = ArrayCacheCases.CreateValue(type, 2, 8);
            Apply(decoder, BuildFrame(type, 100, first, second));
            ArrayCacheCases.AssertEqual(first, (Array)firstField.GetValue(target));
            ArrayCacheCases.AssertEqual(first, (Array)firstField.GetValue(other));
            ArrayCacheCases.AssertEqual(second, (Array)secondField.GetValue(target));
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(otherOwner);
        }
    }

    private static byte[] BuildFrame(int type, uint hash, Array value, Array second = null)
    {
        var bytes = new byte[4096];
        int cursor = NetworkFrameWriter.BeginNetworkFrame(bytes, 0, 1);
        int start = cursor;
        cursor = NetworkFrameWriter.BeginVariableState(bytes, cursor, 1, 1);
        cursor = NetworkValueEntryWriter.WriteVariableValue(bytes, cursor, hash, type, value);
        ArrayCacheCases.Require(cursor >= 0, "Failed to build variable entry");
        if (second != null)
            cursor = NetworkValueEntryWriter.WriteVariableValue(bytes, cursor, 200, type, second);
        ArrayCacheCases.Require(cursor >= 0, "Failed to build second variable entry");
        ArrayCacheCases.Require(NetworkFrameWriter.EndVariableState(bytes, start, cursor, second == null ? 1 : 2), "Failed to finish variable state");
        ArrayCacheCases.Require(NetworkFrameWriter.EndNetworkFrame(bytes, 0, 1), "Failed to finish frame");
        Array.Resize(ref bytes, cursor);
        return bytes;
    }

    private static void Apply(TSMPDecoder decoder, byte[] bytes)
    {
        typeof(TSMPDecoder).GetField("_payloadBytes", Private).SetValue(decoder, bytes);
        bool accepted = (bool)typeof(TSMPDecoder).GetMethod("ApplyNetworkFrame", Private).Invoke(decoder, null);
        ArrayCacheCases.Require(accepted, "Decoder rejected test frame: " + decoder.lastError);
    }
}
