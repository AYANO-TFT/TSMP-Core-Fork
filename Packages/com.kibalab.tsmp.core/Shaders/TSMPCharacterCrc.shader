Shader "Hidden/TSMP/Character CRC"
{
    Properties { _MainTex ("Decoded bytes", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma target 4.0
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            Texture2D<float4> _MainTex;
            int _InputWidth, _OutputWidth, _PayloadOffset, _PayloadLength, _ResultPixel;
            static const uint nibble[16] = {
                0,0x1db71064u,0x3b6e20c8u,0x26d930acu,0x76dc4190u,0x6b6b51f4u,0x4db26158u,0x5005713cu,
                0xedb88320u,0xf00f9344u,0xd6d6a3e8u,0xcb61b38cu,0x9b64c2b0u,0x86d3d2d4u,0xa00ae278u,0xbdbdf21cu };
            struct Reader { int pixel; uint4 rgba; };
            uint B(int offset, inout Reader r)
            {
                int p = offset + _PayloadOffset, pixel = p >> 2;
                // Reuse all four channels, also across headers/ranges within this invocation.
                if (r.pixel != pixel) { r.pixel = pixel; r.rgba = (uint4)round(_MainTex.Load(int3(pixel % _InputWidth, pixel / _InputWidth, 0)) * 255.0); }
                return r.rgba[p & 3];
            }
            uint U16(int p,inout Reader r) { uint a=B(p,r); return a | (B(p+1,r)<<8); }
            uint U32(int p,inout Reader r) { uint a=U16(p,r); return a | (U16(p+2,r)<<16); }
            uint Step(uint c,uint b) { c^=b; c=(c>>4)^nibble[c&15u]; return (c>>4)^nibble[c&15u]; }
            uint Range(uint c,int start,int length,inout Reader r)
            { [loop] for(int p=start;p<start+length;p++) c=Step(c,B(p,r)); return c; }
            float4 Encode(uint v) { return float4(v&255u,(v>>8)&255u,(v>>16)&255u,(v>>24)&255u)/255.0; }
            bool Layout(inout Reader r,out int m,out int chars,out int ranges)
            {
                m=0;chars=0;ranges=0;
                if(_PayloadLength<28 || _PayloadLength>65535) return false;
                m=U16(_PayloadLength-4,r);
                if((m^U16(_PayloadLength-2,r))!=65535 || m<8 || m>_PayloadLength-20) return false;
                if(U32(m,r)!=0x43524354u || B(m+4,r)!=1 || U16(m+8,r)!=m || U16(m+10,r)!=0) return false;
                chars=B(m+5,r);ranges=U16(m+6,r);
                return chars<=16 && ranges<=64 && m+20+chars*16+ranges*4==_PayloadLength;
            }
            uint Metadata(int m,int chars,int ranges,inout Reader r)
            {
                uint c=Range(0xffffffffu,m,12,r);
                [loop] for(int i=0;i<chars;i++) c=Range(c,m+16+i*16,8,r);
                c=Range(c,m+16+chars*16,ranges*4+4,r);
                if(B(0,r)!=1) return 0;
                int messages=U16(2,r),cursor=8;
                if(messages>(m-8)/8) return 0;
                c=Range(c,0,8,r);
                [loop] for(int i=0;i<messages;i++)
                {
                    if(cursor>m-8) return 0;
                    int type=B(cursor+2,r),body=cursor+8,end=body+U16(cursor+6,r),pos,count;
                    if(end>m || (type!=1 && type!=2)) return 0;
                    c=Range(c,cursor,8,r);
                    if(type==1) { if(body>end-2) return 0; count=U16(body,r);pos=body+2;c=Range(c,body,2,r); }
                    else { if(body>end-5) return 0;count=B(body+4,r);pos=body+5;c=Range(c,body,5,r); }
                    int hs=type==1?7:3;
                    if(count>(end-pos)/hs) return 0;
                    [loop] for(int j=0;j<count;j++)
                    {
                        if(pos>end-hs) return 0;
                        int length=U16(pos+hs-2,r);
                        if(length>end-pos-hs) return 0;
                        c=Range(c,pos,hs,r);pos+=hs+length;
                    }
                    if(pos!=end) return 0;
                    cursor=end;
                }
                if(cursor!=m || (c^0xffffffffu)!=U32(m+12,r)) return 0;
                return 0x43524300u | (uint)chars;
            }
            uint Group(int g,bool fingers,int m,int chars,int ranges,inout Reader r)
            {
                int d=m+16+g*16, start=U16(d,r),length=U16(d+2,r),first=U16(d+4,r),count=B(d+6,r),mode=B(d+7,r);
                if(mode>1 || first>ranges || count>ranges-first || start<8 || length<4 || start>m-length) return 0;
                if(fingers && mode==0) return 1;
                int version=B(start,r),flags=B(start+1,r),bones=U16(start+2,r),tail=start+4+bones*6;
                if(version<1 || version>4 || bones>55 || (flags&~15)!=0 || tail>start+length) return 0;
                uint c=0xffffffffu;
                if(!fingers) c=Range(c,start,4,r);
                [loop] for(int i=0;i<bones;i++)
                {
                    int p=start+4+i*6,id=B(p,r); if(id>54) return 0;
                    if(!fingers) c=Step(c,(uint)id);
                    if(fingers==(id>=24 && id<=53)) c=Range(c,p+1,5,r);
                }
                if(!fingers)
                {
                    c=Range(c,tail,start+length-tail,r);
                    [loop] for(int i=first;i<first+count;i++)
                    {
                        int rp=m+16+chars*16+i*4,p=U16(rp,r),n=U16(rp+2,r);
                        if(p<8 || n<1 || p>m-n) return 0;
                        c=Range(c,p,n,r);
                    }
                }
                return (c^0xffffffffu)==U32(d+(fingers?12:8),r)?1u:0u;
            }
            float4 frag(v2f_img i):SV_Target
            {
                int pixel=(int)i.pos.y*_OutputWidth+(int)i.pos.x;
                if(pixel<_ResultPixel) return _MainTex.Load(int3(pixel%_InputWidth,pixel/_InputWidth,0));
                int index=pixel-_ResultPixel;
                if(index>32) return 0;
                Reader r; r.pixel=-1;r.rgba=0;
                int m,chars,ranges; if(!Layout(r,m,chars,ranges)) return 0;
                if(index==0) return Encode(Metadata(m,chars,ranges,r));
                int g=(index-1)/2; if(g>=chars) return 0;
                return Encode(Group(g,(index&1)==0,m,chars,ranges,r));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
