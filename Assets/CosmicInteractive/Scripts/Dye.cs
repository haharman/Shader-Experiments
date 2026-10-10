using UnityEngine;
using UnityEngine.UI;

public class Dye : MonoBehaviour
{
    [SerializeField] private Texture2D dyeInitTex;
    [SerializeField] private ComputeShader shader;
    [SerializeField, Range(0f, 1f)] private float correctionIntensity;
    [SerializeField] private Material addMat;
    private PingPongRenderTexture _dye;
    private int _advectDyeKernelIndex;
    private Vector3Int _advectDyeGroupSize;
    private int _w, _h;

    
    public void Initialize(int w, int h)
    {
        _w = w;
        _h = h;
        var dyeFirstReadRt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf) { enableRandomWrite = true };
        var dyeFirstWriteRt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf) { enableRandomWrite = true };
        dyeFirstReadRt.Create();
        dyeFirstWriteRt.Create();
        Graphics.Blit(dyeInitTex, dyeFirstReadRt);
        _dye = new PingPongRenderTexture(dyeFirstReadRt, dyeFirstWriteRt);
        _advectDyeKernelIndex = shader.FindKernel("AdvectDye");
        uint x, y, z;
        shader.GetKernelThreadGroupSizes(_advectDyeKernelIndex, out x, out y, out z);
        _advectDyeGroupSize = new Vector3Int((int)x, (int)y, (int)z);
        
    }
    
    public RenderTexture Tick(RenderTexture velocityField, float deltaTime)
    {
        shader.SetFloat("_AdvectWeight", 1f);
        shader.SetFloat("_DeltaTime", deltaTime);
        shader.SetInts("_Resolution", _w, _h);
        shader.SetFloat("_CorrectionIntensity", correctionIntensity);
        shader.SetTexture(_advectDyeKernelIndex, "_VelocityFieldRead", velocityField);
        shader.SetTexture(_advectDyeKernelIndex, "_DyeTextureRead", _dye.read);
        shader.SetTexture(_advectDyeKernelIndex, "_DyeTextureWrite", _dye.write);
        
        shader.Dispatch(_advectDyeKernelIndex,
            (_w + _advectDyeGroupSize.x - 1) / _advectDyeGroupSize.x,
            (_h + _advectDyeGroupSize.y - 1) / _advectDyeGroupSize.y,
            1);
        
        _dye.Swap();

        return _dye.read;
    }

    private void OnDestroy()
    {
        _dye?.Dispose();
    }
}