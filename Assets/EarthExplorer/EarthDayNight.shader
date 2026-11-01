Shader "EarthExplorer/DayNight"
{
 Properties
 {
  _DayMap("Day Albedo", 2D) = "white" {}
  _NightMap("Night Lights", 2D) = "black" {}
  _SunDirection("Sun Direction", Vector) = (-0.4,0.5,-1,0)
 }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
  Pass
  {
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   TEXTURE2D(_DayMap); SAMPLER(sampler_DayMap);
   TEXTURE2D(_NightMap); SAMPLER(sampler_NightMap);
   CBUFFER_START(UnityPerMaterial)
   float4 _SunDirection;
   CBUFFER_END
   struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
   struct Varyings { float4 positionCS:SV_POSITION; float3 normalWS:TEXCOORD0; float3 positionWS:TEXCOORD1; float2 uv:TEXCOORD2; };
   Varyings vert(Attributes v) { Varyings o; o.positionWS=TransformObjectToWorld(v.positionOS.xyz); o.positionCS=TransformWorldToHClip(o.positionWS); o.normalWS=TransformObjectToWorldNormal(v.normalOS); o.uv=v.uv; return o; }
   half4 frag(Varyings i):SV_Target
   {
    float3 n=normalize(i.normalWS);
    float sunlight=dot(n,normalize(_SunDirection.xyz));
    float day=smoothstep(-0.15,0.22,sunlight);
    half3 albedo=SAMPLE_TEXTURE2D(_DayMap,sampler_DayMap,i.uv).rgb;
    half3 night=SAMPLE_TEXTURE2D(_NightMap,sampler_NightMap,i.uv).rgb;
    float rim=pow(1-saturate(dot(n,normalize(GetCameraPositionWS()-i.positionWS))),4);
    return half4(albedo*(0.055+day*0.95)+night*(1-day)*1.5+half3(0.03,0.3,0.55)*rim,1);
   }
   ENDHLSL
  }
 }
}
