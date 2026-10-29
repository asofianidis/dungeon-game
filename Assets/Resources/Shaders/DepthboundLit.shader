// Lit shader for characters and props. _Tint/_Flash are driven per-renderer via MaterialPropertyBlock
// (fog-of-war dimming, hit flashes, wind-up glow) without breaking material sharing.
Shader "Depthbound/Lit"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
        _Emission ("Emission", Color) = (0,0,0,0)
        _Tint ("Tint", Color) = (1,1,1,1)
        _Flash ("Flash", Color) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Lambert fullforwardshadows addshadow
        #pragma target 3.0
        sampler2D _MainTex;
        fixed4 _Color, _Emission, _Tint, _Flash;
        struct Input { float2 uv_MainTex; };
        void surf (Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb * _Tint.rgb;
            o.Emission = _Emission.rgb * _Tint.rgb + _Flash.rgb;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
