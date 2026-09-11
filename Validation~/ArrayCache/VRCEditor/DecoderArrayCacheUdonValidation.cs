using System;
using System.Collections.Generic;
using System.IO;
using K13A.TSMP;
using UdonSharp;
using UdonSharp.Compiler;
using UnityEditor;
using UnityEngine;
using VRC.Udon.Common.Interfaces;
using VRC.Udon.Editor;

public static class DecoderArrayCacheUdonValidation
{
    public static void Run()
    {
        string output = Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT");
        var results = new List<string>();
        try
        {
            const string assetPath = "Assets/Validation/ArrayCache/DecoderArrayCacheVmProbe.asset";
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Validation/ArrayCache/Udon/DecoderArrayCacheVmProbe.cs");
                AssetDatabase.CreateAsset(asset, assetPath);
            }
            bool compileError = false;
            Application.LogCallback callback = (message, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) compileError = true;
            };
            Application.logMessageReceived += callback;
            try
            {
                UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = false });
            }
            finally
            {
                Application.logMessageReceived -= callback;
            }
            ArrayCacheCases.Require(!compileError, "Udon client compilation reported an error");
            IUdonProgram program = asset.GetRealProgram();
            ArrayCacheCases.Require(program != null && program.ByteCode.Length > 0, "No Udon bytecode was produced");
            IUdonVM vm = UdonEditorManager.Instance.ConstructUdonVM();
            vm.LoadProgram(program);
            foreach (int type in ArrayCacheCases.Types)
            {
                ArrayCacheCases.Exercise(type, (index, value) =>
                {
                    Set(program, "payload", ArrayCacheCases.EncodeValue(type, value), typeof(byte[]));
                    Set(program, "valueType", type, typeof(int));
                    Set(program, "bindingIndex", index, typeof(int));
                    vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol("DecodeValue"));
                    uint status = vm.Interpret();
                    ArrayCacheCases.Require(status == 0, "Udon VM returned " + status);
                    return (Array)program.Heap.GetHeapVariable(program.SymbolTable.GetAddressFromSymbol("decodedValue"));
                });
                results.Add("PASS Udon VM array type " + type + ": isolation, reuse, resize, empty");
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText(output, "PASS\nUnity=" + Application.unityVersion + "\nBuildType=Udon client bytecode in editor VM\n" + string.Join("\n", results));
        }
        catch (Exception exception)
        {
            File.WriteAllText(output, "FAIL\n" + string.Join("\n", results) + "\n" + exception);
            throw;
        }
    }

    private static void Set(IUdonProgram program, string name, object value, Type type)
    {
        program.Heap.SetHeapVariable(program.SymbolTable.GetAddressFromSymbol(name), value, type);
    }
}
