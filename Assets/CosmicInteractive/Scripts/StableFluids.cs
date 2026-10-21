using System.Runtime.InteropServices;
using UnityEngine;

public class StableFluids : MonoBehaviour
{
    // Const
    private const int MaxInputs = 1000;

    // Inspector
    [SerializeField] private InputSource inputSource;
    [SerializeField] private ComputeShader shader;
    [SerializeField, Range(1, 1000)] private int pressureJacobiCount;
    [SerializeField, Range(1, 1000)] private int diffuseJacobiCount;
    [SerializeField] private float velocityBlendFactor = 0.5f;
    [SerializeField] private float viscosity = 1f;

    // Kernel
    private Kernel2D _addVelocity;
    private Kernel2D _advectVelocity;
    private Kernel2D _diffuseVelocity;
    private Kernel2D _divergence;
    private Kernel2D _pressureJacobi;
    private Kernel2D _subtractPressureGradient;
    
    // RT
    private PingPongRenderTexture _velocityPingPong;
    private PingPongRenderTexture _pressurePingPong;
    private RenderTexture _velocitySourceRt;
    private RenderTexture _divergenceRt;

    // Buffer
    private ComputeBuffer _inputBuffer;
    
    // Value
    private Vector2Int _res;

    public void Initialize(Vector2Int resolution)
    {
        if (inputSource == null)
        {
            Debug.LogError("[StableFluids] InputSource を割り当ててください");
            return;
        }

        _res = resolution;
        
        //Kernel
        _addVelocity = new Kernel2D(shader, "AddVelocity");
        _advectVelocity = new Kernel2D(shader, "AdvectVelocity");
        _diffuseVelocity = new Kernel2D(shader, "DiffuseVelocity");
        _divergence = new Kernel2D(shader, "Divergence");
        _pressureJacobi = new Kernel2D(shader, "PressureJacobi");
        _subtractPressureGradient = new Kernel2D(shader, "SubtractPressureGradient");

        // RT
        var velocityRead = CreateRt(RenderTextureFormat.RGFloat);
        var velocityWrite = CreateRt(RenderTextureFormat.RGFloat);
        var pressureRead = CreateRt(RenderTextureFormat.RFloat);
        var pressureWrite = CreateRt(RenderTextureFormat.RFloat);
        _velocitySourceRt = CreateRt(RenderTextureFormat.RGFloat);
        _divergenceRt = CreateRt(RenderTextureFormat.RFloat);

        Graphics.Blit(Texture2D.blackTexture, velocityRead);
        Graphics.Blit(Texture2D.blackTexture, pressureRead);

        _velocityPingPong = new PingPongRenderTexture(velocityRead, velocityWrite);
        _pressurePingPong = new PingPongRenderTexture(pressureRead, pressureWrite);

        // Buffer
        _inputBuffer = new ComputeBuffer(MaxInputs, Marshal.SizeOf<VelocityInputData>());
        _inputBuffer.SetData(new VelocityInputData[MaxInputs]);
    }

    public RenderTexture Tick()
    {
        shader.SetFloat("_DeltaTime", Time.deltaTime);
        shader.SetInts("_Resolution", _res.x, _res.y);
        shader.SetFloat("_Aspect", (float)_res.x / _res.y);
        shader.SetFloat("_VelocityBlendFactor", velocityBlendFactor);
        shader.SetFloat("_Viscosity", viscosity);

        if (inputSource.TryGetInput(out VelocityInputData[] input))
        {
            shader.SetInt("_MaxInputs", MaxInputs);
            shader.SetInt("_InputCount", input.Length);
            _inputBuffer.SetData(input);

            _addVelocity.SetBuffer("_InputBufferRead", _inputBuffer);
            _addVelocity.SetTexture("_VelocityFieldRead", _velocityPingPong.read);
            _addVelocity.SetTexture("_VelocityFieldWrite", _velocityPingPong.write);
            _addVelocity.Dispatch(_res);
            _velocityPingPong.Swap();
        }

        _advectVelocity.SetTexture("_VelocityFieldRead", _velocityPingPong.read);
        _advectVelocity.SetTexture("_VelocityFieldWrite", _velocityPingPong.write);
        _advectVelocity.Dispatch(_res);
        _velocityPingPong.Swap();

        Graphics.CopyTexture(_velocityPingPong.read, _velocitySourceRt);
        _diffuseVelocity.SetTexture("_VelocitySourceRead", _velocitySourceRt);
        for (int i = 0; i < diffuseJacobiCount; i++)
        {
            _diffuseVelocity.SetTexture("_VelocityFieldRead", _velocityPingPong.read);
            _diffuseVelocity.SetTexture("_VelocityFieldWrite", _velocityPingPong.write);
            _diffuseVelocity.Dispatch(_res);
            _velocityPingPong.Swap();
        }

        _divergence.SetTexture("_VelocityFieldRead", _velocityPingPong.read);
        _divergence.SetTexture("_DivergenceBufferWrite", _divergenceRt);
        _divergence.Dispatch(_res);

        _pressureJacobi.SetTexture("_DivergenceBufferRead", _divergenceRt);
        for (int i = 0; i < pressureJacobiCount; i++)
        {
            _pressureJacobi.SetTexture("_PressureBufferRead", _pressurePingPong.read);
            _pressureJacobi.SetTexture("_PressureBufferWrite", _pressurePingPong.write);
            _pressureJacobi.Dispatch(_res);
            _pressurePingPong.Swap();
        }

        _subtractPressureGradient.SetTexture("_PressureBufferRead", _pressurePingPong.read);
        _subtractPressureGradient.SetTexture("_VelocityFieldRead", _velocityPingPong.read);
        _subtractPressureGradient.SetTexture("_VelocityFieldWrite", _velocityPingPong.write);
        _subtractPressureGradient.Dispatch(_res);
        _velocityPingPong.Swap();

        return _velocityPingPong.read;
    }

    private RenderTexture CreateRt(RenderTextureFormat format)
    {
        var rt = new RenderTexture(_res.x, _res.y, 0, format) { enableRandomWrite = true };
        rt.Create();
        return rt;
    }

    private void OnDestroy()
    {
        _velocityPingPong?.Dispose();
        _pressurePingPong?.Dispose();
        if (_velocitySourceRt != null) Destroy(_velocitySourceRt);
        if (_divergenceRt != null) Destroy(_divergenceRt);
        _inputBuffer?.Release();
    }
}
