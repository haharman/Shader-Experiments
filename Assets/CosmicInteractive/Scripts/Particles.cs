using System.Runtime.InteropServices;
using UnityEngine;

public class Particles : MonoBehaviour
{
    [SerializeField] private StableFluids stableFluids;
    [SerializeField] private ComputeShader shader;
    
    [SerializeField, Range(1,100000)] private int particleCount;
    // separateWeight, separateRadius はあとに回す
    [SerializeField, Range(0f, 1f)] private float fade;
    [SerializeField, ColorUsage(false, false)] private Color slowColor;
    [SerializeField, ColorUsage(false, false)] private Color fastColor;
    [SerializeField] float slowVelocity;
    [SerializeField] float fastVelocity;

    private int _w;
    private int _h;
    private PingPongRenderTexture _particleTexture;
    private ComputeBuffer _particleBuffer;
    
    private int _fadeKernelIndex;
    private int _updateKernelIndex;
    private Vector2Int _fadeKernelSize;
    private Vector2Int _updateKernelSize;
    
    public struct ParticleData
    {
        public Vector2 Position;
    }
    public void Initialize(int w, int h)
    {
        if (stableFluids == null) {
            Debug.LogError("[Particles] stableFluids を割り当ててください");
            return;
        }

        _w = w;
        _h = h;
        
        // KernelIndex, KernelSize を取得
        _fadeKernelIndex = shader.FindKernel("Fade");
        _updateKernelIndex = shader.FindKernel("Update");
        uint x, y, z;
        shader.GetKernelThreadGroupSizes(_fadeKernelIndex, out x, out y, out z);
        _fadeKernelSize = new Vector2Int((int)x, (int)y);
        shader.GetKernelThreadGroupSizes(_updateKernelIndex, out x, out y, out z);
        _updateKernelSize = new Vector2Int((int)x, (int)y);

        // Textureを初期化
        var particleTexFirstRead = new RenderTexture(_w, _h, 0, RenderTextureFormat.ARGBHalf) { enableRandomWrite = true };
        var particleTexFirstWrite = new RenderTexture(_w, _h, 0, RenderTextureFormat.ARGBHalf) { enableRandomWrite = true };
        particleTexFirstRead.Create();
        particleTexFirstWrite.Create();
        Graphics.Blit(Texture2D.blackTexture, particleTexFirstRead);
        _particleTexture = new PingPongRenderTexture(particleTexFirstRead, particleTexFirstWrite);

        // Bufferを初期化
        int stride = Marshal.SizeOf(typeof(ParticleData));
        _particleBuffer = new ComputeBuffer(particleCount, stride);
        var initial = new ParticleData[particleCount];
        for (int i = 0; i < particleCount; i++)
        {
            initial[i].Position = new Vector2(
                UnityEngine.Random.Range(0f, _w),
                UnityEngine.Random.Range(0f, _h)
            );
        }
        _particleBuffer.SetData(initial);
    }

    public RenderTexture Tick(RenderTexture vel, float deltaTime)
    {
        shader.SetInt("_ParticleCount", particleCount);
        shader.SetFloat("_DeltaTime", deltaTime);
        shader.SetFloat("_Fade", fade);
        shader.SetVector("_SlowColor", slowColor);
        shader.SetVector("_FastColor", fastColor);
        shader.SetFloat("_SlowVelocitySq",slowVelocity * slowVelocity);
        shader.SetFloat("_FastVelocitySq", fastVelocity * fastVelocity);
        shader.SetInts("_Resolution", _w, _h);
        
        shader.SetTexture(_fadeKernelIndex, "_ParticleTextureRead", _particleTexture.read);
        shader.SetTexture(_fadeKernelIndex, "_ParticleTextureWrite", _particleTexture.write);
        shader.Dispatch(_fadeKernelIndex,
            (_w + _fadeKernelSize.x - 1) / _fadeKernelSize.x,
            (_h + _fadeKernelSize.y - 1) / _fadeKernelSize.y,
            1);

        shader.SetTexture(_updateKernelIndex, "_VelocityField", vel);
        shader.SetBuffer(_updateKernelIndex, "_ParticleBuffer", _particleBuffer);
        shader.SetTexture(_updateKernelIndex, "_ParticleTextureWrite", _particleTexture.write);
        shader.Dispatch(_updateKernelIndex, (particleCount + _updateKernelSize.x - 1) / _updateKernelSize.x, 1, 1);
        
        _particleTexture.Swap();
        return _particleTexture.read;
    }

    private void OnDestroy()
    {
        if(_particleBuffer != null) _particleBuffer.Release();
        _particleBuffer = null;
        _particleTexture?.Dispose();
    }
}