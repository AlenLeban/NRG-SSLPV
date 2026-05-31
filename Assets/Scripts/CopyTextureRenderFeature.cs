using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using Unity.Mathematics;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Experimental.GlobalIllumination;
using UnityEditor;
using static Unity.Burst.Intrinsics.X86.Avx;



struct FSHSphere
{
	public Matrix4x4 objectToWorld; // We must specify object-to-world transformation for each instance
	public uint renderingLayerMask; // In addition we also like to specify rendering layer mask per instence.
	public float weight; // Just some additional per-instance data unrelated to rendering
};
public class CopyTextureRenderFeature : ScriptableRendererFeature
{
    [SerializeField] CopyTextureRenderFeatureSettings settings;
    CopyTextureRenderFeaturePass m_ScriptablePass;
    CopyTextureDebugRenderFeaturePass m_ScriptablePassDebug;
    CopyTextureSHDebugRenderFeaturePass m_ScriptablePassSHDebug;

    private Material blitMaterial;
    private GraphicsBuffer[] coefficientsBuffer;
    private GraphicsBuffer[] coefficientsBufferNew;
    private GraphicsBuffer[] finalCoefficientsBuffer;
    private GraphicsBuffer voxelOcclusionBuffer;

    /// <inheritdoc/>
    public override void Create()
    {

        if (settings.blitShader == null)
        {
            Debug.LogError("Missing blit shader");
            return;
        }
        
        if (settings.mortonSortCompute == null)
        {
            Debug.LogError("Missing morton sort compute shader");
            
        }

        coefficientsBuffer = new GraphicsBuffer[settings.numCascades];
		coefficientsBufferNew = new GraphicsBuffer[settings.numCascades];
		finalCoefficientsBuffer = new GraphicsBuffer[settings.numCascades];

        for (int i = 0; i < settings.numCascades; i++)
        {
            coefficientsBuffer[i] = new GraphicsBuffer(GraphicsBuffer.Target.Structured, settings.SHGridWidth * settings.SHGridDepth * settings.SHGridHeight, sizeof(int) * 9 * 3 + sizeof(uint) * 3);
            coefficientsBufferNew[i] = new GraphicsBuffer(GraphicsBuffer.Target.Structured, settings.SHGridWidth * settings.SHGridDepth * settings.SHGridHeight, sizeof(int) * 9 * 3 + sizeof(uint) * 3);
		    finalCoefficientsBuffer[i] = new GraphicsBuffer(GraphicsBuffer.Target.Structured, settings.SHGridWidth * settings.SHGridDepth * settings.SHGridHeight, sizeof(int) * 9 * 3 + sizeof(uint) * 3);
        }

		//voxelOcclusionBuffer = new ComputeBuffer((int)Math.Floor(settings.SHGridWidth / settings.voxelGridScale * settings.SHGridDepth / settings.voxelGridScale * settings.SHGridHeight / settings.voxelGridScale), sizeof(float));

        blitMaterial = new Material(settings.blitShader);
        settings.blitMaterial = blitMaterial;

        settings.debugMaterial = new Material(settings.debugShader);

        m_ScriptablePass = new CopyTextureRenderFeaturePass(settings, coefficientsBuffer, coefficientsBufferNew, finalCoefficientsBuffer, voxelOcclusionBuffer);
        m_ScriptablePassDebug = new CopyTextureDebugRenderFeaturePass(settings, coefficientsBuffer, coefficientsBufferNew, finalCoefficientsBuffer, voxelOcclusionBuffer);
        m_ScriptablePassSHDebug = new CopyTextureSHDebugRenderFeaturePass(settings, coefficientsBuffer, coefficientsBufferNew, finalCoefficientsBuffer, voxelOcclusionBuffer);

        // Configures where the render pass should be injected.
        m_ScriptablePass.renderPassEvent = settings.renderPassPosition;
        m_ScriptablePass.ConfigureInput(ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Depth);

        m_ScriptablePassDebug.renderPassEvent = RenderPassEvent.AfterRenderingDeferredLights;
        m_ScriptablePassDebug.ConfigureInput(ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Depth);

		m_ScriptablePassSHDebug.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;

		// You can request URP color texture and depth buffer as inputs by uncommenting the line below,
		// URP will ensure copies of these resources are available for sampling before executing the render pass.
		// Only uncomment it if necessary, it will have a performance impact, especially on mobiles and other TBDR GPUs where it will break render passes.
		//m_ScriptablePass.ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth);

		// You can request URP to render to an intermediate texture by uncommenting the line below.
		// Use this option for passes that do not support rendering directly to the backbuffer.
		// Only uncomment it if necessary, it will have a performance impact, especially on mobiles and other TBDR GPUs where it will break render passes.
		m_ScriptablePass.requiresIntermediateTexture = true;
        m_ScriptablePassDebug.requiresIntermediateTexture = true;
        
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (blitMaterial == null)
        {
            return;
        }

        blitMaterial.SetInteger("_DownsampleFactor", settings.downSample);
        
        renderer.EnqueuePass(m_ScriptablePass);
        renderer.EnqueuePass(m_ScriptablePassDebug);
        renderer.EnqueuePass(m_ScriptablePassSHDebug);
    }

    // Use this class to pass around settings from the feature to the pass
    [Serializable]
    public class CopyTextureRenderFeatureSettings
    {
        public RenderPassEvent renderPassPosition = RenderPassEvent.AfterRenderingShadows;
        public Material blitMaterial;
        public Material debugMaterial;
        public Material shDebugMaterial;
        public Mesh shDebugMeshSphere;
        public Shader debugShader;
        public Shader blitShader;
        public int downSample = 4;
        public ComputeShader mortonSortCompute;
        //public ComputeShader randomVolumeTextureCompute;
        public ComputeShader irradianceAndAggregateCompute;
        public ComputeShader clearGridCompute;
        public int SHGridWidth = 16;
        public int SHGridHeight = 16;
        public int SHGridDepth = 16;
        public float SHGridScale = 1.0f;
        //private float voxelGridScale = 0.5f;
        public int propagationIterations = 1;
        public float initialEnergy = 1;
        public int numCascades = 2;
        public float cascadeScalingFactor = 2;
        public bool useFrameSkipping = true;
    }

	class CopyTextureSHDebugRenderFeaturePass : ScriptableRenderPass
	{
		readonly CopyTextureRenderFeatureSettings settings;
		private GraphicsBuffer[] coefficientsBuffer;
		private GraphicsBuffer[] coefficientsBufferNew;
		private GraphicsBuffer[] finalCoefficientsBuffer;
		GraphicsBuffer voxelOcclusionBuffer;
		public CopyTextureSHDebugRenderFeaturePass(CopyTextureRenderFeatureSettings settings, GraphicsBuffer[] coefficientsBuffer, GraphicsBuffer[] coefficientsBufferNew, GraphicsBuffer[] finalCoefficientsBuffer, GraphicsBuffer voxelOcclusionBuffer)
		{
			this.settings = settings;
			this.coefficientsBuffer = coefficientsBuffer;
			this.coefficientsBufferNew = coefficientsBufferNew;
			this.finalCoefficientsBuffer = finalCoefficientsBuffer;
			this.voxelOcclusionBuffer = voxelOcclusionBuffer;
		}

		private class PassData
		{
			public int gridWidth;
			public int gridHeight;
			public int gridDepth;
			public float gridScale;
		}


		public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
		{

			UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
			//CustomUniversalData customData = frameData.Get<CustomUniversalData>();


			using (var builder = renderGraph.AddUnsafePass<PassData>($"{passName}-SHdebug", out var passData))
			{
				//builder.AllowPassCulling(false);

				passData.gridWidth = settings.SHGridWidth;
				passData.gridHeight = settings.SHGridHeight;
				passData.gridDepth = settings.SHGridDepth;
				passData.gridScale = settings.SHGridScale;

				UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
				Vector3 cameraPositionWS = cameraData.camera.transform.position;


                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
					CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

					Matrix4x4[] sphereLocations = new Matrix4x4[1];
                    sphereLocations[0] = Matrix4x4.Translate(new Vector3(0, 2, 0));
                    cmd.DrawMeshInstanced(settings.shDebugMeshSphere, 0, settings.shDebugMaterial, 0, sphereLocations, 0);
                    //RenderParams renderParams = new RenderParams(settings.shDebugMaterial);
                    //Graphics.RenderMeshInstanced<FSHSphere>(renderParams, settings.shDebugMeshSphere, 0, shSpheresData);
                });

			}
		}
		public void Dispose()
		{
			for (int i = 0; i < settings.numCascades; i++)
			{
				coefficientsBufferNew[i]?.Dispose();
				coefficientsBuffer[i]?.Dispose();
			}

		}
	}

	class CopyTextureDebugRenderFeaturePass : ScriptableRenderPass
    {
		static readonly int[] SHGridIDs =
		{
			Shader.PropertyToID("_SHCoefficientsGrid0"),
			Shader.PropertyToID("_SHCoefficientsGrid1"),
			Shader.PropertyToID("_SHCoefficientsGrid2"),
			Shader.PropertyToID("_SHCoefficientsGrid3"),
		};

		readonly CopyTextureRenderFeatureSettings settings;
		private GraphicsBuffer[] coefficientsBuffer;
		private GraphicsBuffer[] coefficientsBufferNew;
        private GraphicsBuffer[] finalCoefficientsBuffer;
		GraphicsBuffer voxelOcclusionBuffer;
		public CopyTextureDebugRenderFeaturePass(CopyTextureRenderFeatureSettings settings, GraphicsBuffer[] coefficientsBuffer, GraphicsBuffer[] coefficientsBufferNew, GraphicsBuffer[] finalCoefficientsBuffer, GraphicsBuffer voxelOcclusionBuffer)
		{
			this.settings = settings;
			this.coefficientsBuffer = coefficientsBuffer;
			this.coefficientsBufferNew = coefficientsBufferNew;
            this.finalCoefficientsBuffer = finalCoefficientsBuffer;
            this.voxelOcclusionBuffer = voxelOcclusionBuffer;
		}

		private class PassData
        {
            public Material debugMaterial;
            //public TextureHandle volumeTexture;
            public TextureHandle sourceTexture;
            public TextureHandle normalTexture;
            public TextureHandle albedoTexture;
            public TextureHandle downsampledTexture;
            public TextureHandle cameraDepthTexture;
            public int gridWidth;
            public int gridHeight;
            public int gridDepth;
            public float gridScale;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            CustomUniversalData customData = frameData.Get<CustomUniversalData>();
            TextureDesc outputTextureDesc = resourceData.cameraColor.GetDescriptor(renderGraph);
            outputTextureDesc.clearBuffer = false;
            outputTextureDesc.name = $"{passName}-gbuffer[3]";
			outputTextureDesc.enableRandomWrite = true;
			TextureHandle outputTexture = renderGraph.CreateTexture(outputTextureDesc);

			using (var builder = renderGraph.AddRasterRenderPass<PassData>($"{passName}-debug", out var passData))
            {
                //builder.AllowPassCulling(false);
                //builder.UseTexture(customData.SHVolumeTexture);
                builder.UseTexture(resourceData.activeColorTexture);
                builder.UseTexture(customData.downsampledTexture);
                builder.UseTexture(resourceData.gBuffer[2]);
                builder.UseTexture(resourceData.gBuffer[0]);
                builder.UseTexture(resourceData.cameraDepthTexture);
                builder.UseTexture(resourceData.cameraColor);

                passData.debugMaterial = settings.debugMaterial;
                //passData.volumeTexture = customData.SHVolumeTexture;
                passData.downsampledTexture = customData.downsampledTexture;
                passData.sourceTexture = resourceData.activeColorTexture;
                passData.normalTexture = resourceData.gBuffer[2];
                passData.albedoTexture = resourceData.gBuffer[0];
                passData.gridWidth = settings.SHGridWidth;
                passData.gridHeight = settings.SHGridHeight;
                passData.gridDepth = settings.SHGridDepth;
                passData.gridScale = settings.SHGridScale;
                passData.cameraDepthTexture = resourceData.cameraDepthTexture;


                builder.SetRenderAttachment(outputTexture, 0);

                resourceData.cameraColor = outputTexture;

                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
				Vector3 cameraPositionWS = cameraData.camera.transform.position;

                builder.SetRenderFunc((PassData data, RasterGraphContext context) => 
                {

                    using (new ProfilingScope(context.cmd, new ProfilingSampler("Visualizing LPV")))
                    {
                        int precision = 1024 * 8;
                        //data.debugMaterial.SetTexture("_VolumeTexture", data.volumeTexture);
                        data.debugMaterial.SetTexture("_NormalTexture", data.normalTexture);
                        data.debugMaterial.SetTexture("_CustomCameraDepthTexture", data.cameraDepthTexture); // debug
                        data.debugMaterial.SetTexture("_AlbedoTexture", data.albedoTexture);
                        data.debugMaterial.SetTexture("_DownsampledTexture", data.downsampledTexture);

                        for (int i = 0; i < settings.numCascades; i++)
                        {
                            data.debugMaterial.SetBuffer(SHGridIDs[i], finalCoefficientsBuffer[i]);
                        }
                        //data.debugMaterial.SetBuffer("_VoxelOcclusionGrid", voxelOcclusionBuffer);
                        data.debugMaterial.SetInteger("_GridWidth", data.gridWidth);
                        data.debugMaterial.SetInteger("_GridHeight", data.gridHeight);
                        data.debugMaterial.SetInteger("_Precision", precision);
                        data.debugMaterial.SetInteger("_GridDepth", data.gridDepth);
                        data.debugMaterial.SetFloat("_GridScale", data.gridScale);
                        data.debugMaterial.SetVector("_CameraPositionWS", cameraPositionWS);
                        data.debugMaterial.SetFloat("_CascadeScalingFactor", settings.cascadeScalingFactor);
                        data.debugMaterial.SetInteger("_NumCascades", settings.numCascades);
                        //data.debugMaterial.SetFloat("_VoxelGridScale", settings.voxelGridScale);
                        data.debugMaterial.SetInteger("_DownsampleFactor", settings.downSample);
                        Blitter.BlitTexture(
                            context.cmd,
                            data.sourceTexture,
                            new Vector4(1.0f, 1.0f, 0.0f, 0.0f),
                            data.debugMaterial,
                            0);
                    }
                });

            }
        }
		public void Dispose()
		{
            for (int i = 0; i < settings.numCascades; i++)
            {
                coefficientsBufferNew[i]?.Dispose();
                coefficientsBuffer[i]?.Dispose();
            }

		}
	}
    class CopyTextureRenderFeaturePass : ScriptableRenderPass
    {
        readonly CopyTextureRenderFeatureSettings settings;
        private GraphicsBuffer[] coefficientsBuffer;
        private GraphicsBuffer[] coefficientsBufferNew;
        private GraphicsBuffer[] finalCoefficientsBuffer;
		GraphicsBuffer voxelOcclusionBuffer;
        private Vector3[] previousSnappedCameraLocations;
        private GraphicsBuffer gridMoveBuffer;
        private int kernelId;
        private int propagationKernelId;
        private int clearComputeKernelId;
        private int gridShiftKernelId;
        private int addNewLightingKernelId;

		static readonly int[] SHGridIDs =
        {
	        Shader.PropertyToID("_SHCoefficientsGrid0"),
	        Shader.PropertyToID("_SHCoefficientsGrid1"),
	        Shader.PropertyToID("_SHCoefficientsGrid2"),
	        Shader.PropertyToID("_SHCoefficientsGrid3"),
        };

		static readonly int[] SHGridNewIDs =
		{
			Shader.PropertyToID("_SHCoefficientsGridNew0"),
			Shader.PropertyToID("_SHCoefficientsGridNew1"),
			Shader.PropertyToID("_SHCoefficientsGridNew2"),
			Shader.PropertyToID("_SHCoefficientsGridNew3"),
		};


		public CopyTextureRenderFeaturePass(CopyTextureRenderFeatureSettings settings, GraphicsBuffer[] coefficientsBuffer, GraphicsBuffer[] coefficientsBufferNew, GraphicsBuffer[] finalCoefficientsBuffer, GraphicsBuffer voxelOcclusionBuffer)
        {
            this.settings = settings;
            this.coefficientsBuffer = coefficientsBuffer;
            this.coefficientsBufferNew = coefficientsBufferNew;
            this.finalCoefficientsBuffer = finalCoefficientsBuffer;
            this.voxelOcclusionBuffer = voxelOcclusionBuffer;
            gridMoveBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, coefficientsBuffer[0].count, coefficientsBuffer[0].stride);
            previousSnappedCameraLocations = new Vector3[settings.numCascades];


			kernelId = settings.irradianceAndAggregateCompute.FindKernel("CSMain");
		    propagationKernelId = settings.clearGridCompute.FindKernel("PropagateIrradiance");
		    clearComputeKernelId = settings.clearGridCompute.FindKernel("CSMain");
		    gridShiftKernelId = settings.clearGridCompute.FindKernel("ShiftGrid");
		    addNewLightingKernelId = settings.clearGridCompute.FindKernel("AddNewLighting");
	}

        // This class stores the data needed by the RenderGraph pass.
        // It is passed as a parameter to the delegate function that executes the RenderGraph pass.
        private class PassData
        {
            public TextureHandle sourceTexture;
            //public TextureHandle volumeTexture;
            public TextureHandle normalTexture;
            public TextureHandle albedoTexture;
            public Material blitMaterial;
            public ComputeShader mortonSortCompute;
            public int gridWidth;
            public int gridHeight;
            public int gridDepth;
            public float gridScale;
            public RenderTargetIdentifier destinationRTIdentifier;
        }

        private class ComputePassData
        {
            //public ComputeShader randomVolumeTextureCompute;
            public ComputeShader irradianceAndAggregateCompute;
            public int kernelId;
            //public TextureHandle volumeTextureHandle;
            public TextureHandle positionWSTexture;
            public TextureHandle normalWSTexture;
            public TextureHandle litSurfaceColorTexture;
            public TextureHandle cameraDepthTexture;

			public BufferHandle[] coefficientsBufferHandle;
			public BufferHandle[] coefficientsBufferNewHandle;
			public BufferHandle[] coefficientsBufferFinalHandle;
            public BufferHandle gridMoveBufferHandle;

            public int threadGroupsScreenX;
            public int threadGroupsScreenY;

            public int threadGroupsGridX;
            public int threadGroupsGridY;
            public int threadGroupsGridZ;

			public int gridWidth;
            public int gridHeight;
            public int gridDepth;
            public float gridScale;
        }
        // This static method is passed as the RenderFunc delegate to the RenderGraph render pass.
        // It is used to execute draw commands.
        static void ExecutePass(PassData data, RasterGraphContext context)
        {

            data.blitMaterial.SetInteger("_GridWidth", data.gridWidth);
            data.blitMaterial.SetInteger("_GridHeight", data.gridHeight);
            data.blitMaterial.SetInteger("_GridDepth", data.gridDepth);
            data.blitMaterial.SetFloat("_GridScale", data.gridScale);
            Blitter.BlitTexture(
                context.cmd, 
                data.sourceTexture,
                new Vector4(1.0f, 1.0f, 0.0f, 0.0f),
                data.blitMaterial,
                0);

        }

        // RecordRenderGraph is where the RenderGraph handle can be accessed, through which render passes can be added to the graph.
        // FrameData is a context container through which URP resources can be accessed and managed.
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {

            const string passName = "CopyTexturePass";
            string computePassName = $"{passName}-compute";

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            
            TextureDesc destinationDesc = resourceData.activeColorTexture.GetDescriptor(renderGraph);
            destinationDesc.name = $"{passName}-copiedLight";
            destinationDesc.clearBuffer = false;
            //destinationDesc.format = UnityEngine.Experimental.Rendering.GraphicsFormat.R32G32B32A32_SFloat;
            destinationDesc.enableRandomWrite = true;
            destinationDesc.filterMode = FilterMode.Point;

			TextureHandle copiedTextureDesc = renderGraph.CreateTexture(destinationDesc);
            BufferHandle[] coefficientsBufferHandle = new BufferHandle[settings.numCascades];
            BufferHandle[] coefficientsBufferNewHandle = new BufferHandle[settings.numCascades];
            BufferHandle[] coefficientsBufferFinalHandle = new BufferHandle[settings.numCascades];
			for (int cb = 0; cb < settings.numCascades; cb++)
			{
				coefficientsBufferHandle[cb] = renderGraph.ImportBuffer(coefficientsBuffer[cb]);
				coefficientsBufferNewHandle[cb] = renderGraph.ImportBuffer(coefficientsBufferNew[cb]);
				coefficientsBufferFinalHandle[cb] = renderGraph.ImportBuffer(finalCoefficientsBuffer[cb]);
			}
			BufferHandle gridMoveBufferHandle = renderGraph.ImportBuffer(gridMoveBuffer);


            // This adds a raster render pass to the graph, specifying the name and the data type that will be passed to the ExecutePass function.
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData))
            {
                passData.blitMaterial = settings.blitMaterial;
                passData.sourceTexture = resourceData.activeColorTexture;
                passData.normalTexture = resourceData.gBuffer[2];
                passData.gridWidth = settings.SHGridWidth;
                passData.gridHeight = settings.SHGridHeight;
                passData.gridDepth = settings.SHGridDepth;
                passData.gridScale = settings.SHGridScale;

                builder.UseTexture(resourceData.activeColorTexture);
                builder.UseTexture(resourceData.gBuffer[2]);
                builder.SetRenderAttachment(copiedTextureDesc, 0);
                //builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePass(data, context));
            }

            using (var builder = renderGraph.AddComputePass<ComputePassData>(computePassName, out var passData))
            {
				//builder.AllowPassCulling(false);
                CustomUniversalData customData = frameData.Create<CustomUniversalData>();
                customData.downsampledTexture = copiedTextureDesc;

				for (int cb = 0; cb < settings.numCascades; cb++)
				{
					builder.UseBuffer(coefficientsBufferHandle[cb], flags: AccessFlags.ReadWrite);
					builder.UseBuffer(coefficientsBufferNewHandle[cb], flags: AccessFlags.ReadWrite);
					builder.UseBuffer(coefficientsBufferFinalHandle[cb], flags: AccessFlags.ReadWrite);
				}
				builder.UseBuffer(gridMoveBufferHandle, flags: AccessFlags.ReadWrite);

				builder.UseTexture(copiedTextureDesc);
                builder.UseTexture(resourceData.gBuffer[2]);
                builder.UseTexture(resourceData.gBuffer[3]);
                builder.UseTexture(resourceData.cameraDepthTexture);

				passData.gridWidth = settings.SHGridWidth;
				passData.gridHeight = settings.SHGridHeight;
				passData.gridDepth = settings.SHGridDepth;
				passData.gridScale = settings.SHGridScale;

				passData.coefficientsBufferHandle = new BufferHandle[settings.numCascades];
				passData.coefficientsBufferNewHandle = new BufferHandle[settings.numCascades];
				passData.coefficientsBufferFinalHandle = new BufferHandle[settings.numCascades];
				for (int cb = 0; cb < settings.numCascades; cb++)
				{
                    passData.coefficientsBufferHandle[cb] = coefficientsBufferHandle[cb];
                    passData.coefficientsBufferNewHandle[cb] = coefficientsBufferNewHandle[cb];
                    passData.coefficientsBufferFinalHandle[cb] = coefficientsBufferFinalHandle[cb];
				}
                passData.gridMoveBufferHandle = gridMoveBufferHandle;

				passData.irradianceAndAggregateCompute = settings.irradianceAndAggregateCompute;
                passData.positionWSTexture = copiedTextureDesc;
                passData.normalWSTexture = resourceData.gBuffer[2];
                passData.litSurfaceColorTexture = resourceData.gBuffer[3];
                passData.cameraDepthTexture = resourceData.cameraDepthTexture;

                passData.threadGroupsGridX = (int)math.ceil(settings.SHGridWidth / 2.0f);
                passData.threadGroupsGridY = (int)math.ceil(settings.SHGridWidth / 2.0f);
                passData.threadGroupsGridZ = (int)math.ceil(settings.SHGridWidth / 2.0f);

                passData.threadGroupsScreenX = (int)math.ceil(destinationDesc.width / 16.0f);
				passData.threadGroupsScreenY = (int)math.ceil(destinationDesc.width / 8.0f);

				if (settings.irradianceAndAggregateCompute == null)
                {
                    Debug.LogError("Irradiance and aggregate compute shader not set, skipping");
                    return;
                }

                
                passData.kernelId = kernelId;
                //passData.volumeTextureHandle = SHGridTextureHandle;

				builder.SetRenderFunc((ComputePassData data, ComputeGraphContext context) =>
                {

                    int precision = 1024*8;
                    Vector3 cameraPositionWS = cameraData.camera.transform.position;

                    context.cmd.SetComputeIntParam(settings.clearGridCompute, "_GridWidth", settings.SHGridWidth);
                    context.cmd.SetComputeIntParam(settings.clearGridCompute, "_GridHeight", settings.SHGridHeight);
                    context.cmd.SetComputeIntParam(settings.clearGridCompute, "_GridDepth", settings.SHGridDepth);
                    context.cmd.SetComputeIntParam(settings.clearGridCompute, "_Precision", precision);
                    context.cmd.SetComputeIntParam(settings.clearGridCompute, "_NumCascades", settings.numCascades);

                    context.cmd.SetComputeIntParam(data.irradianceAndAggregateCompute, "_GridWidth", data.gridWidth);
                    context.cmd.SetComputeIntParam(data.irradianceAndAggregateCompute, "_GridHeight", data.gridHeight);
                    context.cmd.SetComputeIntParam(data.irradianceAndAggregateCompute, "_GridDepth", data.gridDepth);
                    context.cmd.SetComputeFloatParam(data.irradianceAndAggregateCompute, "_InitialEnergy", settings.initialEnergy);
                    context.cmd.SetComputeIntParam(data.irradianceAndAggregateCompute, "_DownsampleFactor", settings.downSample);
                    context.cmd.SetComputeFloatParam(data.irradianceAndAggregateCompute, "_GridScale", data.gridScale);
                    context.cmd.SetComputeFloatParam(data.irradianceAndAggregateCompute, "_CascadeScalingFactor", settings.cascadeScalingFactor);
                    context.cmd.SetComputeIntParam(data.irradianceAndAggregateCompute, "_NumCascades", settings.numCascades);
                    context.cmd.SetComputeIntParam(data.irradianceAndAggregateCompute, "_Precision", precision);
                    context.cmd.SetComputeVectorParam(data.irradianceAndAggregateCompute, "_CameraPositionWS", new Vector4(cameraPositionWS.x, cameraPositionWS.y, cameraPositionWS.z, 0.0f));

                    //clear the grid
                    // clear work
                    {
                        int cascadeIndexPow = 1;
                        float cascadeScalePow = 1;
                        for (int c0 = 0; c0 < settings.numCascades; c0++)
                        {
					        using (new ProfilingScope(context.cmd, new ProfilingSampler("Clearing Grid")))
					        {
                                if (Time.frameCount % cascadeIndexPow == 0)
                                {
                                    int cascadeIndex = Math.Min(c0, settings.numCascades - 1);
                                    context.cmd.SetComputeBufferParam(settings.clearGridCompute, clearComputeKernelId, SHGridIDs[0], passData.coefficientsBufferHandle[cascadeIndex]);
                                    context.cmd.SetComputeBufferParam(settings.clearGridCompute, clearComputeKernelId, SHGridNewIDs[0], passData.coefficientsBufferNewHandle[cascadeIndex]);
                                    context.cmd.DispatchCompute(settings.clearGridCompute, clearComputeKernelId, data.threadGroupsGridX, data.threadGroupsGridY, data.threadGroupsGridZ);
                                }
                            }
                            using (new ProfilingScope(context.cmd, new ProfilingSampler("Gather pixels into grid")))
                            {
                                // gather pixels into the grid
                                context.cmd.SetComputeTextureParam(data.irradianceAndAggregateCompute, data.kernelId, "_PositionWS", data.positionWSTexture);
                                context.cmd.SetComputeTextureParam(data.irradianceAndAggregateCompute, data.kernelId, "_NormalWS", data.normalWSTexture);
                                context.cmd.SetComputeTextureParam(data.irradianceAndAggregateCompute, data.kernelId, "_CameraDepth", data.cameraDepthTexture);
                                context.cmd.SetComputeTextureParam(data.irradianceAndAggregateCompute, data.kernelId, "_LitSurfaceColor", data.litSurfaceColorTexture);
                                if (Time.frameCount % cascadeIndexPow == 0)
                                {
                                    int cascadeIndex = Math.Min(c0, settings.numCascades - 1);

                                    context.cmd.SetComputeFloatParam(data.irradianceAndAggregateCompute, "_GridScale", data.gridScale * cascadeScalePow);
                                    context.cmd.SetComputeBufferParam(data.irradianceAndAggregateCompute, data.kernelId, SHGridIDs[0], passData.coefficientsBufferHandle[cascadeIndex]);
                                    context.cmd.DispatchCompute(data.irradianceAndAggregateCompute, data.kernelId, data.threadGroupsScreenX / settings.downSample, data.threadGroupsScreenY / settings.downSample, 1);
                                }

                            }
                            if (settings.useFrameSkipping)
						        cascadeIndexPow *= 2;
                            cascadeScalePow *= settings.cascadeScalingFactor;
					    }
                    }


                   
                    using (new ProfilingScope(context.cmd, new ProfilingSampler("Propagation and updating")))
                    {
						int cascadeIndexPow = 1;
						float cascadeScalePow = 1;
						for (int c = 0; c < settings.numCascades; c++)
                        {
                            if (Time.frameCount % cascadeIndexPow == 0)
                            {
                                
                                // propagate irradiance among cells
                                for (int i = 0; i < settings.propagationIterations; i++)
                                {
                                    context.cmd.SetComputeBufferParam(settings.clearGridCompute, propagationKernelId, "_SHCoefficientsGridOld", i % 2 == 0 ? passData.coefficientsBufferHandle[c] : passData.coefficientsBufferNewHandle[c]);
                                    context.cmd.SetComputeBufferParam(settings.clearGridCompute, propagationKernelId, "_SHCoefficientsGridNew0", i % 2 == 1 ? passData.coefficientsBufferHandle[c] : passData.coefficientsBufferNewHandle[c]);
                                    context.cmd.DispatchCompute(settings.clearGridCompute, propagationKernelId, data.threadGroupsGridX, data.threadGroupsGridY, data.threadGroupsGridZ);
                                }

                            }
                            Vector3 snappedCameraPosition = cameraPositionWS;
                            float cascadeGridFinalScale = (data.gridScale * (float)cascadeScalePow);
                            snappedCameraPosition.x = (float)Math.Round(snappedCameraPosition.x / cascadeGridFinalScale) * cascadeGridFinalScale;
                            snappedCameraPosition.y = (float)Math.Round(snappedCameraPosition.y / cascadeGridFinalScale) * cascadeGridFinalScale;
                            snappedCameraPosition.z = (float)Math.Round(snappedCameraPosition.z / cascadeGridFinalScale) * cascadeGridFinalScale;
                            // if camera moved to new snapped position, shift buffers
                            if (snappedCameraPosition != previousSnappedCameraLocations[c])
                            {
                                
                                context.cmd.SetComputeBufferParam(settings.clearGridCompute, gridShiftKernelId, "_SHCoefficientsGridFinal", passData.coefficientsBufferFinalHandle[c]);
                                context.cmd.SetComputeBufferParam(settings.clearGridCompute, gridShiftKernelId, "_GridAfterShift", passData.gridMoveBufferHandle);
                                context.cmd.SetComputeVectorParam(settings.clearGridCompute, "_CameraPositionOffset", new Vector4(
                                        snappedCameraPosition.x - previousSnappedCameraLocations[c].x,
                                        snappedCameraPosition.y - previousSnappedCameraLocations[c].y,
                                        snappedCameraPosition.z - previousSnappedCameraLocations[c].z,
                                        0) / cascadeGridFinalScale);
                                context.cmd.DispatchCompute(settings.clearGridCompute, gridShiftKernelId, data.threadGroupsGridX, data.threadGroupsGridY, data.threadGroupsGridZ);

								BufferHandle temp = passData.coefficientsBufferFinalHandle[c];
								passData.coefficientsBufferFinalHandle[c] = passData.gridMoveBufferHandle;
								passData.gridMoveBufferHandle = temp;

								GraphicsBuffer tempgb = finalCoefficientsBuffer[c];
								finalCoefficientsBuffer[c] = gridMoveBuffer;
								gridMoveBuffer = tempgb;
							}

                            // update lighting with new data
                            if (Time.frameCount % cascadeIndexPow == 0)
                            {
                                BufferHandle shCoefficientsGridNew = (settings.propagationIterations % 2 == 0 ? passData.coefficientsBufferHandle[c] : passData.coefficientsBufferNewHandle[c]);
                                context.cmd.SetComputeBufferParam(settings.clearGridCompute, addNewLightingKernelId, "_SHCoefficientsGridNew0", shCoefficientsGridNew);
                                context.cmd.SetComputeBufferParam(settings.clearGridCompute, addNewLightingKernelId, "_SHCoefficientsGridFinal", passData.coefficientsBufferFinalHandle[c]);
                                context.cmd.DispatchCompute(settings.clearGridCompute, addNewLightingKernelId, data.threadGroupsGridX, data.threadGroupsGridY, data.threadGroupsGridZ);
                            }

                            previousSnappedCameraLocations[c] = snappedCameraPosition;

							if (settings.useFrameSkipping)
								cascadeIndexPow *= 2;
							cascadeScalePow *= settings.cascadeScalingFactor;
                        }

                    }

				});
                    
            }


            
        }
    }
}
