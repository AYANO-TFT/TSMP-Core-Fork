// Fork regression. Copy into an existing project's Editor folder only when testing.
using System;
using System.Collections.Generic;
using System.Reflection;
using K13A.TSMP.Udon;
using UnityEngine;

public static class BlendShapeCacheValidation
{
    public abstract class Driver
    {
        public abstract object Get(string field);
        public abstract void Set(string field,object value);
        public abstract void Call(string method);
        public abstract GameObject Host { get; }
    }
    sealed class CSharpDriver : Driver
    {
        readonly TSMPNetworkBlendShapesSync sync;
        const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        public CSharpDriver(TSMPNetworkBlendShapesSync value){sync=value;}
        public override object Get(string n)=>typeof(TSMPNetworkBlendShapesSync).GetField(n,F).GetValue(sync);
        public override void Set(string n,object value)=>typeof(TSMPNetworkBlendShapesSync).GetField(n,F).SetValue(sync,value);
        public override void Call(string n)=>typeof(TSMPNetworkBlendShapesSync).GetMethod(n,F).Invoke(sync,null);
        public override GameObject Host=>sync.gameObject;
    }
    public static Driver CSharp(TSMPNetworkBlendShapesSync sync)=>new CSharpDriver(sync);
    static Mesh MeshWithShapes(int count)
    {
        var m=new Mesh{vertices=new[]{Vector3.zero,Vector3.right,Vector3.up},triangles=new[]{0,1,2}};
        for(int i=0;i<count;i++)m.AddBlendShapeFrame("shape"+i,100,new[]{Vector3.forward,Vector3.forward,Vector3.forward},new Vector3[3],new Vector3[3]);
        return m;
    }
    public static string[] Run(Func<GameObject,Driver> make)
    {
        var results=new List<string>();
        var root=new GameObject("BlendShape cache validation"){hideFlags=HideFlags.HideAndDontSave};
        var mesh=MeshWithShapes(3);var replacement=MeshWithShapes(3);
        try
        {
            var a=new GameObject("Character A");a.transform.SetParent(root.transform);
            var b=new GameObject("Character B");b.transform.SetParent(root.transform);
            var renderer=a.AddComponent<SkinnedMeshRenderer>();renderer.sharedMesh=mesh;
            var other=b.AddComponent<SkinnedMeshRenderer>();other.sharedMesh=replacement;
            var d=make(root);
            Require((bool)d.Get("validateBlendShapeCacheOnReceive"),"Default preserves full validation",results);
            d.Set("targetRenderer",renderer);d.Set("blendShapeIndices",new[]{0});
            d.Set("receiveInterpolation",ReceiveInterpolationMode.Discrete);
            Receive(d,0,10);Require(renderer.GetBlendShapeWeight(0)==10,"Initial receipt",results);
            ((int[])d.Get("blendShapeIndices"))[0]=1;Receive(d,1,20);
            Require(renderer.GetBlendShapeWeight(1)==20,"Default detects in-place edit",results);
            d.Set("validateBlendShapeCacheOnReceive",false);var lookup=d.Get("_selectedBlendShapeLookup");
            Receive(d,1,30);Require(ReferenceEquals(lookup,d.Get("_selectedBlendShapeLookup"))&&renderer.GetBlendShapeWeight(1)==30,"Fast cache receipt",results);
            ((int[])d.Get("blendShapeIndices"))[0]=2;Receive(d,2,40);
            Require(renderer.GetBlendShapeWeight(2)==0,"In-place edit requires invalidation when opted out",results);
            d.Call("_InvalidateBlendShapeCache");Receive(d,2,40);Require(renderer.GetBlendShapeWeight(2)==40,"Explicit invalidation",results);
            d.Set("blendShapeIndices",new[]{0});Receive(d,0,50);Require(renderer.GetBlendShapeWeight(0)==50,"Array replacement",results);
            d.Set("blendShapeIndices",new[]{0,1});Receive(d,1,60);Require(renderer.GetBlendShapeWeight(1)==60,"Array length change",results);
            d.Set("blendShapeIndices",null);Receive(d,1,70);Require(renderer.GetBlendShapeWeight(1)==60,"Null selection",results);
            d.Set("blendShapeIndices",new[]{0});d.Set("targetRenderer",other);Receive(d,0,33);
            Require(other.GetBlendShapeWeight(0)==33&&renderer.GetBlendShapeWeight(0)==50,"Renderer/character replacement isolated",results);
            other.sharedMesh=mesh;Receive(d,0,34);Require(other.GetBlendShapeWeight(0)==34,"Mesh replacement with same shape count",results);
            d.Host.SetActive(false);Receive(d,0,35);Require(other.GetBlendShapeWeight(0)==34,"Inactive receiver skips input",results);
            d.Host.SetActive(true);Receive(d,0,36);Require(other.GetBlendShapeWeight(0)==36,"Reactivated receiver resumes",results);
            d.Set("receiveInterpolation",ReceiveInterpolationMode.Continuous);d.Set("continuousInterpolationRate",0f);
            Receive(d,0,80);d.Call("ApplyContinuousBlendShapes");Require(other.GetBlendShapeWeight(0)==80,"Continuous interpolation",results);
            d.Call("ResetReceiveState");Receive(d,0,90);Require(other.GetBlendShapeWeight(0)==90,"Reset snaps next receive",results);
            ((int[])d.Get("blendShapeIndices"))[0]=1;d.Set("validateBlendShapeCacheOnReceive",true);Receive(d,1,44);d.Call("ApplyContinuousBlendShapes");
            Require(other.GetBlendShapeWeight(1)==44,"Re-enabling validation detects in-place edit",results);
            d.Set("validateBlendShapeCacheOnReceive",false);((int[])d.Get("blendShapeIndices"))[0]=2;other.SetBlendShapeWeight(2,55);
            d.Call("TSMPBeforeEncode");var encoded=(byte[])d.Get("blendShapeBytes");
            Require(encoded.Length==5&&encoded[2]==2&&encoded[4]==55,"Encoder still validates selection",results);
            return results.ToArray();
        }
        finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(replacement);}
    }
    static void Receive(Driver d,int index,int value){d.Set("blendShapeBytes",new byte[]{1,1,(byte)index,(byte)(index>>8),(byte)value});d.Call("OnTSMPVariableReceived");}
    static void Require(bool ok,string name,List<string> results){if(!ok)throw new InvalidOperationException(name);results.Add(name);}
}
