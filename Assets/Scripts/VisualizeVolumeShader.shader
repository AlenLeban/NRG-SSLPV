Shader "Custom/VisualizeVolumeShader"
{
    Properties
    {
        _DownsampleFactor ("Downsample Factor", Integer) = 1
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
            //#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            //#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            #define MAX_CASCADES 4

            #pragma fragment frag
            #pragma vertex Vert;

            SAMPLER(sampler_BlitTexture);

            TEXTURE3D(_VolumeTexture);
            SAMPLER(sampler_VolumeTexture);
            
            TEXTURE2D(_NormalTexture);
            TEXTURE2D(_AlbedoTexture);

            TEXTURE2D(_DownsampledTexture);
            SAMPLER(sampler_DownsampledTexture);

            TEXTURE2D(_CustomCameraDepthTexture);

            TEXTURE2D(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);

            TEXTURE2D(_CameraNormalsTexture);
            SAMPLER(sampler_CameraNormalsTexture);

            struct FCell
            {
	            int sh_r[9];
	            int sh_g[9];
	            int sh_b[9];
	            uint count;
	            uint avgCount;
	            uint isVisible;
            };

            struct FCellFloat
            {
	            float sh_r[9];
	            float sh_g[9];
	            float sh_b[9];
	            uint count;
	            uint avgCount;
	            uint isVisible;
            };

            int _DownsampleFactor;
            int _GridWidth;
            int _GridHeight;
            int _GridDepth;
            float _GridScale;
            float _VoxelGridScale;
            int _Precision;

            int _NumCascades;
            float _CascadeScalingFactor;
            float4 _CameraPositionWS;

            FCellFloat FCellToFloat(in FCell cell)
            {
	            FCellFloat fcellFloat = (FCellFloat) 0;
	            for (int i = 0; i < 9; i++)
	            {
		            fcellFloat.sh_r[i] = ((float) cell.sh_r[i]) / _Precision;
		            fcellFloat.sh_g[i] = ((float) cell.sh_g[i]) / _Precision;
		            fcellFloat.sh_b[i] = ((float) cell.sh_b[i]) / _Precision;
	            }
	            fcellFloat.count = cell.count;
	            fcellFloat.avgCount = cell.avgCount;
	            fcellFloat.isVisible = cell.isVisible;
	            return fcellFloat;
            }

            FCell FCellFloatToInt(in FCellFloat cell)
            {
	            FCell fcell = (FCell) 0;
	            for (int i = 0; i < 9; i++)
	            {
		            fcell.sh_r[i] = floor(cell.sh_r[i] * _Precision);
		            fcell.sh_g[i] = floor(cell.sh_g[i] * _Precision);
		            fcell.sh_b[i] = floor(cell.sh_b[i] * _Precision);
	            }
	            fcell.count = cell.count;
	            fcell.avgCount = cell.avgCount;
	            fcell.isVisible = cell.isVisible;
	            return fcell;
            }

            StructuredBuffer<FCell> _SHCoefficientsGrid0;
            StructuredBuffer<FCell> _SHCoefficientsGrid1;
            StructuredBuffer<FCell> _SHCoefficientsGrid2;
            StructuredBuffer<FCell> _SHCoefficientsGrid3;

            StructuredBuffer<float> _VoxelOcclusionGrid;


            float4 SHCosineLobeL1(in float3 dir)
            {
	            float CosineA0 = PI;
	            float CosineA1 = 2 * PI / 3;
	            return float4(
                    CosineA0 * 0.282095f,
                    CosineA1 * 0.4886025f * dir.y,
                    CosineA1 * 0.4886025f * dir.z,
                    CosineA1 * 0.4886025f * dir.x
                );

            }

            FCellFloat SHCosineLobeL2(in float3 dir)
            {
	            float CosineA0 = PI;
	            float CosineA1 = PI * 0.66666f;
	            float CosineA2 = PI * 0.25f;
	            FCellFloat coeffs = (FCellFloat) 0;
	            coeffs.sh_r[0] = CosineA0 * 0.282095f;
	
	            coeffs.sh_r[1] = CosineA1 * 0.4886025f * dir.y;
	            coeffs.sh_r[2] = CosineA1 * 0.4886025f * dir.z;
	            coeffs.sh_r[3] = CosineA1 * 0.4886025f * dir.x;
	
	            coeffs.sh_r[4] = CosineA2 * 1.092548f * dir.x * dir.y;
	            coeffs.sh_r[5] = CosineA2 * 1.092548f * dir.y * dir.z;
	            coeffs.sh_r[6] = CosineA2 * 0.315392f * (3.0f * dir.z * dir.z - 1.0f);
	            coeffs.sh_r[7] = CosineA2 * 1.092548f * dir.x * dir.z;
	            coeffs.sh_r[8] = CosineA2 * 0.546274f * (dir.x * dir.x - dir.y * dir.y);
	
	            return coeffs;


            }

            // function taken from https://discussions.unity.com/t/how-exactly-shadow-maps-work/922799/2 and adjusted a bit
            float3 GetPixelWorldPosition(uint2 pixelCoordinate)  // pixelCoordinate = SV_Position.xy in the pixel shader
            {
                //return float3(pixelCoordinate.xy, 0);
                // Read this pixel's depth value from the depth buffer
                float depth = LOAD_TEXTURE2D(_CameraDepthTexture, pixelCoordinate).r;

                float3 ndcPosition;
                
                ndcPosition.xy = float2(pixelCoordinate) + 0.5; // center in the middle of the pixel.
                ndcPosition.xy = ndcPosition.xy * _BlitTexture_TexelSize.xy * 2.0 - 1.0;
                ndcPosition.z = depth;
                //return ndcPosition;
                ndcPosition.y = -ndcPosition.y;
                float4 worldPosition = mul(UNITY_MATRIX_I_VP, float4(ndcPosition, 1));
                worldPosition.xyz /= worldPosition.w;

                return worldPosition.xyz;
                //return frac(worldPosition.xyz); // for debugging
            }

            /*float3 WorldSpaceToScaledViewSpace(float3 positionWS)
            {
                //float4 viewSpacePos = float4(positionWS, 1)/_GridScale;
                float4 viewSpacePos = float4(positionWS, 1)/float4(_GridWidth, _GridHeight, _GridDepth, _GridScale)/_GridScale;
                //float4 viewSpacePos = mul(UNITY_MATRIX_V, float4(positionWS, 1)) * float4(1, 1, -1, 1);
                //viewSpacePos.xy /= viewSpacePos.z * 2;
                return viewSpacePos;
                //return positionWS/_GridSize/_GridScale; // for checking in world position, but beware where is Z axis
            }*/

            FCellFloat GetCell(int3 gridPos, int3 offset, int cascadeIndex)
            {
                int3 newGridPos = gridPos + offset;
                int gridPositionToIndex = newGridPos.x + newGridPos.y * (_GridWidth) + newGridPos.z * (_GridWidth * _GridHeight);
                if (gridPos.x >= _GridWidth - 1 || gridPos.y >= _GridHeight - 1 || gridPos.z >= _GridDepth - 1 || gridPos.x < 0 || gridPos.y < 0 || gridPos.z < 0)
                {
                    return (FCellFloat)0;
                }
                if (cascadeIndex == 0)
                    return FCellToFloat(_SHCoefficientsGrid0[gridPositionToIndex]);
                else if (cascadeIndex == 1)
                    return FCellToFloat(_SHCoefficientsGrid1[gridPositionToIndex]);
                else if (cascadeIndex == 2)
                    return FCellToFloat(_SHCoefficientsGrid2[gridPositionToIndex]);
                else
                    return FCellToFloat(_SHCoefficientsGrid3[gridPositionToIndex]);
                
            }

            FCellFloat InterpCells(in FCellFloat c1, in FCellFloat c2, float alpha)
            {
                FCellFloat newCell = (FCellFloat)0;
                for (int i = 0; i < 9; i++)
                {
                    newCell.sh_r[i] = (1 - alpha) * c1.sh_r[i] + alpha * c2.sh_r[i];
                    newCell.sh_g[i] = (1 - alpha) * c1.sh_g[i] + alpha * c2.sh_g[i];
                    newCell.sh_b[i] = (1 - alpha) * c1.sh_b[i] + alpha * c2.sh_b[i];

                }
                return newCell;
            }

            float L2Dot(float a[9], float b[9])
            {
	            float sum = 0;
	            for (int i = 0; i < 9; i++)
	            {
		            sum += a[i] * b[i];
	            }
	            return sum;
            }

            float3 EvalCellLighting(FCellFloat cell, FCellFloat normalSH)
            {
                return float3(
                    L2Dot(cell.sh_r, normalSH.sh_r),
                    L2Dot(cell.sh_g, normalSH.sh_r),
                    L2Dot(cell.sh_b, normalSH.sh_r)
                );
            }

            float4 Sample3DVolume(float3 positionWS, float3 normalWS, int cascadeIndex)
            {
                if (cascadeIndex+1 > _NumCascades)
                {
                    return float4(0, 0, 0, 0);
                }
                float cascadeGridScale = _GridScale;
                if (cascadeIndex >= 1)
                {
                    cascadeGridScale *= _CascadeScalingFactor;
                }
                if (cascadeIndex >= 2)
                {
                    cascadeGridScale *= _CascadeScalingFactor;
                }
                if (cascadeIndex >= 3)
                {
                    cascadeGridScale *= _CascadeScalingFactor;
                }
                float cascadeGridScaleInv = 1 / cascadeGridScale;
                //float cascadeGridScale = _GridScale * pow(_CascadeScalingFactor, cascadeIndex);
                float3 cameraRelativePos = positionWS-round(_WorldSpaceCameraPos * cascadeGridScaleInv) * cascadeGridScale;
                float3 pos = cameraRelativePos * cascadeGridScaleInv + float3(_GridWidth, _GridHeight, _GridDepth) * 0.5f - float3(1, 1, 1) * 0.5;
                //float3 posCameraRelative = cameraRelativePos / cascadeGridScale + float3(_GridWidth, _GridHeight, _GridDepth) / 2 - float3(1, 1, 1) * 0.5;
                //float3 voxelPos = floor((positionWS / _GridScale + float3(_GridWidth, _GridHeight, _GridDepth) / 2) / _VoxelGridScale - float3(1, 1, 1) * 0.5);
                float3 gridLinesDebug = max(abs((pos) - floor(pos))-0.9, 0)*0;
                //float3 voxelGridLinesDebug = max(abs((voxelPos) - floor(voxelPos))-0.90, 0)*0;
                //int voxelGridPosIndex = voxelPos.x + voxelPos.y * floor(_GridWidth / _VoxelGridScale) + voxelPos.z * floor(_GridWidth / _VoxelGridScale * _GridHeight / _VoxelGridScale);
                //float voxelVisDebug = _VoxelOcclusionGrid[voxelGridPosIndex]*0;
                //float voxelVisDebug = (voxelGridPosIndex%200) / 100.0;
                int3 base = (int3)floor(pos);
                //int3 baseCameraRelative = (int3)floor(posCameraRelative);
                float3 frac = pos - base;
                float3 debugLines = frac;

                // bounds
                if (base.x < 1 || base.y < 1 || base.z < 1 ||
                    base.x >= _GridWidth - 2 ||
                    base.y >= _GridHeight - 2 ||
                    base.z >= _GridDepth - 2)
                    {
                        return float4(0,0,0,0);
                    }

                FCellFloat normalSH = SHCosineLobeL2(normalWS);
                float3 l000 = EvalCellLighting(GetCell(base, int3(0,0,0), cascadeIndex), normalSH);
                float3 l100 = EvalCellLighting(GetCell(base, int3(1,0,0), cascadeIndex), normalSH);
                float3 l010 = EvalCellLighting(GetCell(base, int3(0,1,0), cascadeIndex), normalSH);
                float3 l110 = EvalCellLighting(GetCell(base, int3(1,1,0), cascadeIndex), normalSH);
                float3 l001 = EvalCellLighting(GetCell(base, int3(0,0,1), cascadeIndex), normalSH);
                float3 l101 = EvalCellLighting(GetCell(base, int3(1,0,1), cascadeIndex), normalSH);
                float3 l011 = EvalCellLighting(GetCell(base, int3(0,1,1), cascadeIndex), normalSH);
                float3 l111 = EvalCellLighting(GetCell(base, int3(1,1,1), cascadeIndex), normalSH);

                float3 l00 = lerp(l000, l100, frac.x);
                float3 l10 = lerp(l010, l110, frac.x);
                float3 l01 = lerp(l001, l101, frac.x);
                float3 l11 = lerp(l011, l111, frac.x);

                float3 l0 = lerp(l00, l10, frac.y);
                float3 l1 = lerp(l01, l11, frac.y);

                return float4(lerp(l0, l1, frac.z), 0);

            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 positionWS = GetPixelWorldPosition(IN.positionCS.xy).xyz;
                //float3 cameraRelativePosition = round(positionWS/_GridScale)*_GridScale - round(_WorldSpaceCameraPos / _GridScale) * _GridScale;
                

                float4 normalWS = LOAD_TEXTURE2D(_NormalTexture, IN.positionCS.xy);
                float4 sampledVolumeColor = float4(0, 0, 0, 0);

                for (int i = 0; i < MAX_CASCADES; i++)
                {
                    sampledVolumeColor += Sample3DVolume(positionWS, normalWS, i);
                }
                half4 color = LOAD_TEXTURE2D(_BlitTexture, IN.positionCS.xy);
                //half mask = max(round(color.x + color.y + color.z + 0.47f), 0);
                float3 albedoColor = LOAD_TEXTURE2D(_AlbedoTexture, IN.positionCS.xy).xyz;
                //float4 normalWS = LOAD_TEXTURE2D(_CameraNormalsTexture, IN.positionCS.xy);
                float depth = LOAD_TEXTURE2D(_CustomCameraDepthTexture, IN.positionCS.xy);
                //float grazingAngleMask = floor(clamp(dot(-normalize(positionWS - _WorldSpaceCameraPos), normalWS.xyz), 0, 1)+0.97f);

                //float3 combinedColor = color.xyz + sampledVolumeColor.xyz * normalWS.y;
                //float3 downsampledTexture = LOAD_TEXTURE2D(_DownsampledTexture, IN.positionCS.xy / _DownsampleFactor);
                //return (positionWS-
                //return float4(downsampledTexture.xyy, 1);
                //return float4(1/depth, 1/depth, 1/depth, 1)*0.001;
                //return float4(normalWS.xyz, 1);
                //return float4(positionWS, 1);
                //return float4(albedoColor.xyz, 0);
                return float4(sampledVolumeColor.xyz * albedoColor + color, 0);
                //return float4(depth, depth, depth, 1);

            }

            ENDHLSL
        }
    }
}
