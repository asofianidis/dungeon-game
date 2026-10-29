// Dungeon floor/wall mesh. Vertex color carries fog of war (1 = visible, dim = explored, 0 = unknown).
Shader "Depthbound/Map"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _EmissionStrength ("Emission Strength", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Lambert fullforwardshadows addshadow
        #pragma target 3.0
        sampler2D _MainTex;
        float _EmissionStrength;
        struct Input { float2 uv_MainTex; float4 color : COLOR; };
        void surf (Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex);
            o.Albedo = c.rgb * IN.color.rgb;
            o.Emission = c.rgb * _EmissionStrength * IN.color.rgb;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
