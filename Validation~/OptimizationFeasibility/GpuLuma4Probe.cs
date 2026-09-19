#if !COMPILER_UDONSHARP
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using K13A.TSMP;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Profiling;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

public sealed class GpuLuma4Probe : MonoBehaviour
{
    readonly List<string> rows = new List<string>();
    readonly List<string> gpuRows = new List<string> { "width,height,path,samples,gpu_p50_us,gpu_p95_us" };
    Texture2D upload;
    byte[] uploadBytes;
    RenderTexture symbols;
    Color32[] pixels;
    int cases;

#if UNITY_EDITOR
    public static void Gamma() { Launch(ColorSpace.Gamma); }
    public static void Linear() { Launch(ColorSpace.Linear); }
    static void Launch(ColorSpace space)
    {
        PlayerSettings.colorSpace = space;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("GPU Luma4 probe").AddComponent<GpuLuma4Probe>();
        new GameObject("Camera").AddComponent<Camera>().cullingMask = 0;
        EditorApplication.EnterPlaymode();
    }
#endif

    IEnumerator Start()
    {
        rows.Add("space,width,height,payload,path,cpu_mean_ms");
        var work = Check();
        while (true)
        {
            bool next;
            try { next = work.MoveNext(); }
            catch (Exception error) { Finish(error); yield break; }
            if (!next) break;
            yield return work.Current;
        }
        Finish(null);
    }

    IEnumerator Check()
    {
        var material = new Material(Resources.Load<Material>("TSMPEncodeLuma4"));
        byte[] header = new byte[56];
        for (int i = 0; i < header.Length; i++) header[i] = (byte)(i * 71 + 15);
        foreach (Vector2Int size in new[] { new Vector2Int(640,360), new Vector2Int(648,360), new Vector2Int(1280,720), new Vector2Int(3840,2160), new Vector2Int(640,360) })
        {
            int capacity = Luma4Raster.GetPayloadCapacityBytes(size.x, size.y, 8);
            var cpuTexture = Luma4Raster.CreateTexture(size.x, size.y);
            var expected = new RenderTexture(size.x,size.y,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            var actual = new RenderTexture(size.x,size.y,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            expected.Create(); actual.Create();
            foreach (int count in new[] { 0, 1, 3, 32, 256, Math.Min(4096,capacity), capacity, 32 })
            {
                byte[] payload = new byte[count];
                for (int i = 0; i < count; i++) payload[i] = (byte)(i * 73 + cases);
                if (!Luma4Raster.TryWriteFrameBuffered(cpuTexture,8,header,payload,ref pixels,out string error)) throw new Exception(error);
                Graphics.Blit(cpuTexture,expected);
                if (!GpuLuma4Writer.TryWrite(actual,material,8,5,header,payload,count,128f/255f,ref upload,ref uploadBytes,ref symbols)) throw new Exception("GPU path declined supported layout");
                var left = AsyncGPUReadback.Request(expected,0);
                var right = AsyncGPUReadback.Request(actual,0);
                Color32[] a=null,b=null;
                while(a==null || b==null)
                {
                    if(a==null && left.done) { if(left.hasError) throw new Exception("CPU readback error"); a=left.GetData<Color32>().ToArray(); }
                    if(b==null && right.done) { if(right.hasError) throw new Exception("GPU readback error"); b=right.GetData<Color32>().ToArray(); }
                    if(a==null || b==null) yield return null;
                }
                for(int i=0;i<a.Length;i++)
                    if (!a[i].Equals(b[i])) throw new Exception("Pixels differ at " + size + " payload=" + count + " pixel=" + i + " CPU=" + a[i] + " GPU=" + b[i]);
                cases++;
                foreach (bool gpu in Environment.GetEnvironmentVariable("TSMP_GPU_SMOKE")=="1" ? new bool[0] : new[] { false,true })
                {
                    double ms=0;
                    for(int iteration=0;iteration<24;iteration++)
                    {
                        var timer=Stopwatch.StartNew();
                        if(gpu) GpuLuma4Writer.TryWrite(actual,material,8,5,header,payload,count,128f/255f,ref upload,ref uploadBytes,ref symbols);
                        else { Luma4Raster.TryWriteFrameBuffered(cpuTexture,8,header,payload,ref pixels,out error); Graphics.Blit(cpuTexture,expected); }
                        timer.Stop();
                        if(iteration>=4) ms+=timer.Elapsed.TotalMilliseconds;
                        yield return null;
                    }
                    rows.Add(string.Join(",",QualitySettings.activeColorSpace,size.x,size.y,count,gpu?"GPU":"CPU",(ms/20).ToString("F6",CultureInfo.InvariantCulture)));
                }
                if (count==256 && Environment.GetEnvironmentVariable("TSMP_GPU_SMOKE")!="1")
                {
                    var gpuWork=MeasureGpu(cpuTexture,actual,material);
                    while(gpuWork.MoveNext()) yield return gpuWork.Current;
                }
            }
            expected.Release(); actual.Release(); Destroy(expected); Destroy(actual); Destroy(cpuTexture);
        }
        GpuLuma4Writer.ReleaseTexture(upload);
        DecoderSnapshotRuntime.Release(symbols);
        Destroy(material);
    }

    void Finish(Exception error)
    {
        string path=Environment.GetEnvironmentVariable("TSMP_FEASIBILITY_RESULTS");
        File.WriteAllLines(Path.Combine(path,"gpu-times.csv"),rows);
        File.WriteAllLines(Path.Combine(path,"gpu-draw-times.csv"),gpuRows);
        File.WriteAllText(Path.Combine(path,"gpu-result.txt"),(error==null?"PASS":"FAIL")+"\nPixel cases="+cases+"; "+QualitySettings.activeColorSpace+"; "+SystemInfo.graphicsDeviceName+"\n"+error);
#if UNITY_EDITOR
        EditorApplication.Exit(error==null?0:1);
#endif
    }

    IEnumerator MeasureGpu(Texture2D cpuTexture,RenderTexture output,Material material)
    {
#if UNITY_EDITOR
        UnityEditorInternal.ProfilerDriver.SetAreaEnabled(ProfilerArea.GPU,true);
#endif
        Profiler.enabled=true;
        Profiler.SetAreaEnabled(ProfilerArea.GPU,true);
        var camera=FindObjectOfType<Camera>();
        foreach(string name in new[] { "native-upload-blit","udon-symbol-expand","gpu-convert-expand" })
        {
            var sampler=CustomSampler.Create("Luma4 "+name,true);
            var recorder=sampler.GetRecorder(); recorder.enabled=true;
            var commands=new CommandBuffer();
            commands.BeginSample(sampler);
            for(int i=0;i<32;i++)
            {
                if(name=="native-upload-blit") commands.Blit(cpuTexture,output);
                else
                {
                    if(name=="gpu-convert-expand") commands.Blit(upload,symbols,material,0);
                    commands.Blit(symbols,output,material,1);
                }
            }
            commands.EndSample(sampler);
            var values=new List<double>();
            for(int frame=0;frame<40;frame++)
            {
                Profiler.enabled=true;
                Graphics.ExecuteCommandBuffer(commands);
                var request=AsyncGPUReadback.Request(output,0); request.WaitForCompletion();
                if(request.hasError) throw new Exception("GPU timing readback failed");
                camera.Render();
                yield return null;
                if(frame>=15 && recorder.gpuSampleBlockCount==1 && recorder.gpuElapsedNanoseconds>0)
                    values.Add(recorder.gpuElapsedNanoseconds/1000.0/32);
            }
            values.Sort();
            gpuRows.Add(string.Join(",",output.width,output.height,name,values.Count,
                values.Count>0?values[values.Count/2].ToString("F4",CultureInfo.InvariantCulture):"unavailable",
                values.Count>0?values[(int)(values.Count*.95)].ToString("F4",CultureInfo.InvariantCulture):"unavailable"));
            recorder.enabled=false; commands.Dispose();
        }
        Profiler.enabled=false;
    }
}
#endif
