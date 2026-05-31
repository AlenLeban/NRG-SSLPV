using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal.Internal;
using static UnityEngine.Rendering.Universal.ShaderInput;

public class MainLightNormalPass : ScriptableRendererFeature
{
    [SerializeField] MainLightNormalPassSettings settings;
    MainLightNormalPassPass m_ScriptablePass;

    public RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingShadows;
    /// <inheritdoc/>
    public override void Create()
    {
        m_ScriptablePass = new MainLightNormalPassPass(settings);

        // Configures where the render pass should be injected.
        m_ScriptablePass.renderPassEvent = injectionPoint;

        // You can request URP color texture and depth buffer as inputs by uncommenting the line below,
        // URP will ensure copies of these resources are available for sampling before executing the render pass.
        // Only uncomment it if necessary, it will have a performance impact, especially on mobiles and other TBDR GPUs where it will break render passes.
        //m_ScriptablePass.ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth);

        // You can request URP to render to an intermediate texture by uncommenting the line below.
        // Use this option for passes that do not support rendering directly to the backbuffer.
        // Only uncomment it if necessary, it will have a performance impact, especially on mobiles and other TBDR GPUs where it will break render passes.
        //m_ScriptablePass.requiresIntermediateTexture = true;
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(m_ScriptablePass);
    }

    // Use this class to pass around settings from the feature to the pass
    [Serializable]
    public class MainLightNormalPassSettings
    {
        public Material material;
    }

    class MainLightNormalPassPass : ScriptableRenderPass
    {
        readonly MainLightNormalPassSettings settings;
        const string m_passName = "MainLightsNormalPass";
        public static readonly int _WorldToShadow = Shader.PropertyToID("_MainLightWorldToShadow");

        public MainLightNormalPassPass(MainLightNormalPassSettings settings)
        {
            this.settings = settings;
        }

        // This class stores the data needed by the RenderGraph pass.
        // It is passed as a parameter to the delegate function that executes the RenderGraph pass.
        private class PassData
        {
            
        }

        // This static method is passed as the RenderFunc delegate to the RenderGraph render pass.
        // It is used to execute draw commands.
        static void ExecutePass(PassData data, RasterGraphContext context)
        {
            Matrix4x4 viewMatrix;
            Matrix4x4 projMatrix;
            Matrix4x4 shadowTransform;

            CullingResults cullResult = new();
            UnityEngine.Rendering.Universal.ShadowData shadowData;
            /*ShadowUtils.ExtractDirectionalLightMatrix(
                ref cullResult,
                ref shadowData,
                0,
                0,
                1024,
                1024,
                1,
                0.01f,
                out viewMatrix,
                out projMatrix,
                out shadowTransform
            );*/

            //Matrix4x4 lightVP = projMatrix * viewMatrix;

            //context.cmd.SetGlobalMatrix("_LightViewProj", lightVP);
        }

        // RecordRenderGraph is where the RenderGraph handle can be accessed, through which render passes can be added to the graph.
        // FrameData is a context container through which URP resources can be accessed and managed.
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            TextureHandle normalAtlas;
            TextureHandle positionAtlas;

            // This adds a raster render pass to the graph, specifying the name and the data type that will be passed to the ExecutePass function.
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData))
            {
                // Use this scope to set the required inputs and outputs of the pass and to
                // setup the passData with the required properties needed at pass execution time.

                
                // Make use of frameData to access resources and camera data through the dedicated containers.
                // Eg:
                // UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                
                var source = resourceData.mainShadowsTexture;
                TextureDesc desc = renderGraph.GetTextureDesc(source);
                desc.name = $"NormalShadows-{m_passName}";
                desc.clearBuffer = true; // create a new texture, dont modify existing one
                desc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;
                desc.depthBufferBits = DepthBits.None;

                normalAtlas = renderGraph.CreateTexture(desc);
                positionAtlas = renderGraph.CreateTexture(desc);

                // Setup pass inputs and outputs through the builder interface.
                // Eg:
                // builder.UseTexture(sourceTexture);
                // TextureHandle destination = UniversalRenderer.CreateRenderGraphTexture(renderGraph, cameraData.cameraTargetDescriptor, "Destination Texture", false);

                builder.SetRenderAttachment(normalAtlas, 0);
                builder.SetRenderAttachment(positionAtlas, 1);

                // This sets the render target of the pass to the active color texture. Change it to your own render target as needed.
                //builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                
                //RenderGraphUtils.BlitMaterialParameters para = new()
                //renderGraph.AddBlitPass()
                // Assigns the ExecutePass function to the render pass delegate. This will be called by the render graph when executing the pass.
                builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePass(data, context));
            }
        }
    }
}
