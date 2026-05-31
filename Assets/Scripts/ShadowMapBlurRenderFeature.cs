using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using Unity.VisualScripting;

public class ShadowMapBlurRenderFeature : ScriptableRendererFeature
{
    [SerializeField] ShadowMapBlurRenderFeatureSettings settings;
    ShadowMapBlurRenderFeaturePass m_ScriptablePass;
    

    /// <inheritdoc/>
    public override void Create()
    {
        m_ScriptablePass = new ShadowMapBlurRenderFeaturePass(settings);

        // Configures where the render pass should be injected.
        m_ScriptablePass.renderPassEvent = RenderPassEvent.AfterRenderingShadows;

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
    public class ShadowMapBlurRenderFeatureSettings
    {
        public Material blitMaterial;
        public string passName = "ShadowBlurRenderPass";
    }

    class ShadowMapBlurRenderFeaturePass : ScriptableRenderPass
    {
        RTHandle tempRT;
        readonly ShadowMapBlurRenderFeatureSettings settings;

        public ShadowMapBlurRenderFeaturePass(ShadowMapBlurRenderFeatureSettings settings)
        {
            this.settings = settings;
            requiresIntermediateTexture = true;
        }

        // This class stores the data needed by the RenderGraph pass.
        // It is passed as a parameter to the delegate function that executes the RenderGraph pass.
        private class PassData
        {
            public TextureHandle sourceTexture;
            public Material blurMaterial;
        }

        // This static method is passed as the RenderFunc delegate to the RenderGraph render pass.
        // It is used to execute draw commands.
        static void ExecutePass(PassData data, RasterGraphContext context)
        {
            //var desc = data.sourceTexture.
            
            Blitter.BlitTexture(context.cmd, data.sourceTexture, new Vector4(1, 1, 0, 0), data.blurMaterial, 0);
        }

        // RecordRenderGraph is where the RenderGraph handle can be accessed, through which render passes can be added to the graph.
        // FrameData is a context container through which URP resources can be accessed and managed.
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData))
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();


                passData.sourceTexture = resourceData.mainShadowsTexture;
                passData.blurMaterial = settings.blitMaterial;

                builder.UseTexture(passData.sourceTexture);

                TextureDesc destinationDesc = passData.sourceTexture.GetDescriptor(renderGraph);
                destinationDesc.name = $"Shadow-{settings.passName}";
                destinationDesc.colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SFloat;
                destinationDesc.depthBufferBits = DepthBits.None;
                TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

                builder.SetRenderAttachment(destination, 0);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePass(data, context));

            }
        }
    }
}
