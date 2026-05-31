using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

public class CustomUniversalData : ContextItem
{
    public TextureHandle SHVolumeTexture;
    public TextureHandle downsampledTexture;
    public override void Reset()
    {
        SHVolumeTexture = TextureHandle.nullHandle;
        downsampledTexture = TextureHandle.nullHandle;
    }

    
}
