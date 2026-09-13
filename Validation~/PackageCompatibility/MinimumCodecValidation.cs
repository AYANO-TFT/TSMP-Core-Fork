using System;
using System.IO;
using System.Linq;
using K13A.TSMP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class MinimumCodecValidation
{
    public static void Run()
    {
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string name = Environment.GetEnvironmentVariable("TSMP_MINIMUM_CODEC");
            string id = "com.kibalab.tsmp.codec." + name.ToLowerInvariant();
            var installed = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
            Require(installed.Single(p => p.name == "com.kibalab.tsmp.core").version == "0.3.0-beta.2", "Core version");
            Require(!installed.Any(p => p.name.StartsWith("com.vrchat.")), "SDK-free project");
            Require(installed.Where(p => p.name.StartsWith("com.kibalab.tsmp.codec.")).All(p => p.name == id || p.name == "com.kibalab.tsmp.codec.luma4"), "Unexpected optional codec");
            ValidateCodec("Luma4");
            if (name != "Luma4") ValidateCodec(name);
            string result = "PASS\nCore 0.3.0-beta.2 + Luma4" + (name == "Luma4" ? "" : " + " + name) +
                "; no SDK; prefab/material/shader references; header and payload bytes; sampling and preparation fallback; Unity=" + Application.unityVersion;
            File.WriteAllText(Environment.GetEnvironmentVariable("TSMP_MINIMUM_RESULT"), result);
            Debug.Log(result);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            File.WriteAllText(Environment.GetEnvironmentVariable("TSMP_MINIMUM_RESULT"), "FAIL\n" + exception);
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    static void ValidateCodec(string name)
    {
        string path = "Packages/com.kibalab.tsmp.codec." + name.ToLowerInvariant() + "/Runtime/Codec_" + name + ".prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Require(prefab != null, "Missing prefab: " + path);
        var instance = Object.Instantiate(prefab);
        var codec = instance.GetComponent<TSMPCodec>();
        var source = new Texture2D(1280, 720, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
        var output = new RenderTexture(257, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Material preparation = null;
        try
        {
            Require(codec != null && codec.calibrationMaterial != null && codec.calibrationMaterial.shader != null, "Preparation references: " + name);
            preparation = new Material(codec.calibrationMaterial);
            codec.calibrationMaterial = preparation;
            Require(output.Create(), "Byte output allocation");
            foreach (int length in new[] { 4, 56, 1027 })
            {
                byte[] bytes = Enumerable.Range(0, length).Select(i => (byte)(i * 73 + 19)).ToArray();
                var header = FrameHeader.CreateDefault();
                header.ActiveWidthBlocks = 160;
                header.ActiveHeightBlocks = 90;
                header.BlockSize = 8;
                header.PayloadSize = (ushort)length;
                var headerBytes = new byte[56];
                header.WriteTo(headerBytes, 0);
                Require(codec.TryWriteFrame(source, 8, headerBytes, bytes, out string error), error);
                CodecBridge.PrepareDecodeHandler(new[] { codec }, codec.codecId, codec.GetCodecOptionBytes(), 160, 2, false, length);
                Require(codec.selectedDecodeMaterial != null && codec.selectedDecodeMaterial.shader != null, "Byte material: " + name);
                var material = new Material(codec.selectedDecodeMaterial);
                try
                {
                    foreach (int sample in new[] { 1, 4 })
                    {
                        Configure(material, length, codec.GetPayloadStartRow(1280, 8) * 160, sample);
                        foreach (bool prepared in new[] { false, true })
                        {
                            codec.calibrationMaterial = prepared ? preparation : null;
                            codec.PrepareDecode(source, material);
                            Require(prepared || !material.IsKeywordEnabled("TSMP_CALIBRATION_LUT"), "Missing-material fallback");
                            Graphics.Blit(source, output, material);
                            Require(bytes.SequenceEqual(Read(output).Take(length)), "Payload mismatch: " + name + " length=" + length + " sample=" + sample + " prepared=" + prepared);
                        }
                    }
                    if (name == "Luma4")
                    {
                        Configure(material, 56, 320, 4);
                        codec.PrepareDecode(source, material);
                        Graphics.Blit(source, output, material);
                        Require(headerBytes.SequenceEqual(Read(output).Take(56)), "Header mismatch");
                    }
                    Require(!ShaderUtil.ShaderHasError(material.shader), "Byte shader errors: " + name);
                }
                finally { Object.DestroyImmediate(material); }
            }
            Require(!ShaderUtil.ShaderHasError(preparation.shader), "Preparation shader errors: " + name);
        }
        finally
        {
            RenderTexture.active = null;
            Object.DestroyImmediate(instance);
            Object.DestroyImmediate(preparation);
            Object.DestroyImmediate(source);
            output.Release();
            Object.DestroyImmediate(output);
        }
    }

    static void Configure(Material material, int count, int start, int sample)
    {
        material.SetFloat("_BlockSize", 8);
        material.SetFloat("_SampleSize", sample);
        material.SetFloat("_StartBlock", start);
        material.SetFloat("_ByteCount", count);
        material.SetFloat("_ActiveWidthBlocks", 160);
        material.SetFloat("_SourceWidth", 1280);
        material.SetFloat("_SourceHeight", 720);
        material.SetFloat("_OutputWidth", 257);
        material.SetFloat("_OutputHeight", 1);
        material.SetFloat("_FlipY", 1);
    }

    static byte[] Read(RenderTexture output)
    {
        var request = AsyncGPUReadback.Request(output, 0, TextureFormat.RGBA32);
        request.WaitForCompletion();
        Require(!request.hasError, "GPU readback");
        return request.GetData<byte>().ToArray();
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
