Shader "Custom/DepthToPositionShader"
{
    Properties
    {
        _DownsampleFactor ("Downsample Factor", Integer) = 0
        _GridWidth ("Grid Width", Integer) = 8
        _GridHeight ("Grid Height", Integer) = 8
        _GridDepth ("Grid Depth", Integer) = 8
        _GridScale ("Grid Scale", Float) = 1.0
    }


    


    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        ZWrite Off Cull Off
        Pass
        {
            HLSLPROGRAM

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            //#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            #pragma fragment frag
            #pragma vertex Vert;

            SAMPLER(sampler_BlitTexture);

            //TEXTURE3D(_VolumeTexture);
            //SAMPLER(sampler_VolumeTexture);

            TEXTURE2D(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);

            /*TEXTURE2D(_CameraNormalsTexture);
            SAMPLER(sampler_CameraNormalsTexture);*/


           
            int _DownsampleFactor;
            int _GridWidth;
            int _GridHeight;
            int _GridDepth;
            float _GridScale;
            float4 _CameraPositionWS;

            // function taken from https://discussions.unity.com/t/how-exactly-shadow-maps-work/922799/2 and adjusted a bit
            float3 GetPixelWorldPosition(uint2 pixelCoordinate)  // pixelCoordinate = SV_Position.xy in the pixel shader
            {
                //return float3(pixelCoordinate.xy, 0);
                // Read this pixel's depth value from the depth buffer
                float depth = LOAD_TEXTURE2D(_CameraDepthTexture, pixelCoordinate).r;

                // Reconstruct 3d position
                float3 ndcPosition;
                
                ndcPosition.xy = float2(pixelCoordinate) + 0.5; // center in the middle of the pixel.
                ndcPosition.xy = ndcPosition.xy * _BlitTexture_TexelSize.xy * 2.0 - 1.0;
                ndcPosition.z = depth;
                //return ndcPosition;
                ndcPosition.y = -ndcPosition.y;
                float4 worldPosition = mul(UNITY_MATRIX_I_VP, float4(ndcPosition, 1));
                worldPosition.xyz /= worldPosition.w;
                //worldPosition.x = frac(worldPosition.y);
                //return worldPosition.xyz;
                //return float3(depth, depth, depth);
                return worldPosition.xyz;
                //return frac(worldPosition.xyz); // for debugging
            }

            float3 WorldSpaceToScaledViewSpace(float3 positionWS)
            {
                return mul(UNITY_MATRIX_V, float4(positionWS, 1))/int4(_GridWidth, _GridHeight, _GridDepth, 1)/_GridScale * float4(1, 1, -1, 1);
                //return positionWS/_GridSize/_GridScale; // for checking in world position, but beware where is Z axis
            }


            bool IsDepthEdge(uint2 p)
            {
                float d  = LOAD_TEXTURE2D(_CameraDepthTexture, p).r;
                float dx = LOAD_TEXTURE2D(_CameraDepthTexture, p + uint2(1, 0)).r;
                float dy = LOAD_TEXTURE2D(_CameraDepthTexture, p + uint2(0, 1)).r;

                return max(abs(d - dx), abs(d - dy)) > 0.001;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 positionWS = GetPixelWorldPosition(IN.positionCS.xy).xyz;
                //float3 positionVS = mul(UNITY_MATRIX_V, float4(positionWS, 1));
                //positionVS.z *= -1;
                //positionVS /= positionVS.z;
                //float3 sampledVolumeColor = Sample3DVolume(positionWS);

                // half4 color = LOAD_TEXTURE2D(_BlitTexture, IN.positionCS.xy);
                // half mask = clamp(round(color.x + color.y + color.z + 0.47f), 0, 1);

                // float4 normalWS = LOAD_TEXTURE2D(_CameraNormalsTexture, IN.positionCS.xy);
                // float grazingAngleMask = floor(clamp(dot(-normalize(positionWS - _WorldSpaceCameraPos), normalWS.xyz), 0, 1)+0.97f);
                //mask *= grazingAngleMask;
                //return float4(positionWS.xyz/30, 1);
                //return float4(float3(positionVS.z, positionVS.z, positionVS.z).xyz * mask, 1);
                //return float4(positionWS.xyz * mask * grazingAngleMask, 1);
                //return float4(grazingAngleMask, grazingAngleMask, grazingAngleMask, 1);
                //return float4(normalize(positionWS - _WorldSpaceCameraPos).xyz, 1);
                /*if (IsDepthEdge(IN.positionCS.xy * _DownsampleFactor))
                {
                    return float4(0, 0, 0, 0);
                }*/
                return float4(positionWS.xyz, 1);
                //return float4(normalWS.xyz * mask, 1);
                //return float4(mask, mask, mask, 1);
                //return float4(positionVS.xyz, 1);
                //return float4(positionVS.xyz * mask, 1);
            }

            ENDHLSL
        }
    }
}
