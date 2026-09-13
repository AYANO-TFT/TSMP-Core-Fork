Shader "Hidden/TSMP/Encoder Block Expand"
{
    Properties
    {
        _MainTex ("Symbol Texture", 2D) = "black" {}
        _SourceBlockWidth ("Source Block Width", Float) = 1
        _SourceBlockHeight ("Source Block Height", Float) = 1
        _BlockSize ("Block Size", Float) = 8
        _OutputWidth ("Output Width", Float) = 640
        _OutputHeight ("Output Height", Float) = 360
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }

        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _SourceBlockWidth;
            float _SourceBlockHeight;
            float _BlockSize;
            float _OutputWidth;
            float _OutputHeight;

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 blocks = max(float2(_SourceBlockWidth, _SourceBlockHeight), float2(1.0, 1.0));
                float2 size = max(float2(_OutputWidth, _OutputHeight), 1.0);
                float2 pixel = min(floor(saturate(float2(i.uv.x, 1.0 - i.uv.y)) * size), size - 1.0);
                float2 block = floor(pixel / max(_BlockSize, 1.0));
                if (any(block >= blocks))
                    return fixed4(0, 0, 0, 1);
                block.y = blocks.y - 1.0 - block.y;
                float2 uv = (block + 0.5) / blocks;
                return tex2D(_MainTex, uv);
            }
            ENDCG
        }
    }

    Fallback Off
}
