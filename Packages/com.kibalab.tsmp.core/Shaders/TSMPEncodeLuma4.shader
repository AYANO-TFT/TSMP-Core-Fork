Shader "Hidden/TSMP/Encode Luma4"
{
    Properties
    {
        _MainTex ("Bytes", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            Texture2D<float4> _MainTex;
            float4 _MainTex_TexelSize;
            int _SourceBlockWidth;
            int _SourceBlockHeight;
            int _PayloadStartRow;
            int _PayloadBytes;
            float _Background;

            uint ReadByte(uint index)
            {
                uint pixel = index / 4;
                uint width = (uint)_MainTex_TexelSize.z;
                float4 bytes = _MainTex.Load(int3(pixel % width, pixel / width, 0));
                return (uint)round(bytes[index % 4] * 255.0);
            }

            float4 frag(v2f_img i) : SV_Target
            {
                int2 size = int2(_SourceBlockWidth, _SourceBlockHeight);
                int2 block = min((int2)floor(float2(i.uv.x, 1.0 - i.uv.y) * size), size - 1);
                int symbol = -1;
                if (block.y == 0)
                {
                    if (block.x < 8) symbol = (block.x & 1) == 0 ? 15 : 0;
                    if (block.x >= size.x - 8) symbol = ((block.x - size.x + 8) & 1) == 0 ? 0 : 15;
                }
                if (block.y == 1 && block.x < 32) symbol = block.x >> 1;
                int cursor = block.y * size.x + block.x;
                int headerSymbol = cursor - 2 * size.x;
                if (headerSymbol >= 0 && headerSymbol < 112)
                    symbol = (ReadByte((uint)headerSymbol >> 1) >> ((headerSymbol & 1) * 4)) & 15;
                int payloadSymbol = cursor - _PayloadStartRow * size.x;
                if (payloadSymbol >= 0 && payloadSymbol < _PayloadBytes * 2 && block.y < size.y - 1)
                    symbol = (ReadByte(56u + ((uint)payloadSymbol >> 1)) >> ((payloadSymbol & 1) * 4)) & 15;
                if (block.y == size.y - 1)
                {
                    if (block.x >= size.x - 8) symbol = (((block.x - size.x + 8) >> 1) & 1) == 0 ? 0 : 15;
                    if (block.x < 16) symbol = (block.x & 1) == 0 ? 15 : 0;
                }
                float value = symbol < 0 ? _Background : (symbol == 15 ? 232.0 : 24.0 + symbol * 14.0) / 255.0;
                #ifndef UNITY_COLORSPACE_GAMMA
                value = value <= 0.04045 ? value / 12.92 : pow((value + 0.055) / 1.055, 2.4);
                #endif
                return float4(value, value, value, 1);
            }
            ENDCG
        }
        UsePass "Hidden/TSMP/Encoder Block Expand/EXPAND"
    }
    Fallback Off
}
