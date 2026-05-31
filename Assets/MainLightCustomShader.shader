Shader "Custom/MainLightCustomShader"
{   
    SubShader
    {
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
        ENDHLSL

        Tags
        {
            "LightMode" = "Deferred"
        }
        LOD 100
        ZWrite On
        Pass
        {
            Name "MainLightCustomShader"

            HLSLPROGRAM
            
            #pragma vertex Vert
            #pragma fragment Frag

            float4x4 _LightViewProj;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            struct FragOutput
            {
                float4 normal : SV_Target0;
                float4 position : SV_Target1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                o.positionWS = positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);

                o.positionCS = mul(_LightViewProj, float4(positionWS,1));

                return o;
            }

            FragOutput Frag(Varyings input)
            {
                FragOutput o;

                float3 normal = normalize(input.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                o.normal = float4(normal * 0.5 + 0.5, 1.0);
                o.position = float4(input.positionWS, 1.0);

                return o;
            }
            
            ENDHLSL
        }
    }
}
