using System.Runtime.InteropServices;
using UnityEngine;

public class StableFluids : MonoBehaviour
{
    [SerializeField] private InputSource inputSource;
    [SerializeField] private ComputeShader shader;
    private int _w;
    private int _h;
    [SerializeField, Range(1, 1000)] private int pressureJacobiCount;
    [SerializeField, Range(1, 1000)] private int diffuseJacobiCount;
    [SerializeField] private float velocityBlendFactor = 0.5f;
    [SerializeField] private float viscosity = 1f;

    public RenderTexture velocityRt => _velocity.read;

    private float _elapsed;

    private PingPongRenderTexture _velocity;
    private PingPongRenderTexture _pressure;
    private RenderTexture _velocitySourceRt;
    private RenderTexture _divergenceRt;
    
    private int _addVelocityKernelIndex;
    private Vector3Int _addVelocityGroupSize;

    private int _advectVelocityKernelIndex;
    private Vector3Int _advectVelocityGroupSize;
    
    private int _diffuseVelocityKernelIndex;
    private Vector3Int _diffuseVelocityGroupSize;
    
    private int _divergenceKernelIndex;
    private Vector3Int _divergenceGroupSize;
    
    private int _pressureJacobiKernelIndex;
    private Vector3Int _pressureJacobiGroupSize;
    
    private int _subtractPressureGradientKernelIndex;
    private Vector3Int _subtractPressureGradientGroupSize;
    
    
    private ComputeBuffer _inputBuffer;
    private const int MaxInputs = 1000;
    
    public void Initialize(int w, int h)
    {
        if (inputSource == null)
        {
            Debug.LogError("[Bootstrapper] InputSource を割り当ててください");
            return;
        }

        _w = w;
        _h = h;
        
       // RenderTextureの作成（PingPong用に2枚ずつ）
       
       var velFirstReadRt = new RenderTexture(w, h, 0, RenderTextureFormat.RGFloat) { enableRandomWrite = true };
       var velFirstWriteRt = new RenderTexture(w, h, 0, RenderTextureFormat.RGFloat) { enableRandomWrite = true };
       var presFirstReadRt = new RenderTexture(w, h, 0, RenderTextureFormat.RFloat) { enableRandomWrite = true };
       var presFirstWriteRt = new RenderTexture(w, h, 0, RenderTextureFormat.RFloat) { enableRandomWrite = true };
       _velocitySourceRt = new  RenderTexture(w, h, 0, RenderTextureFormat.RGFloat) { enableRandomWrite = true };
       _divergenceRt = new  RenderTexture(w, h, 0, RenderTextureFormat.RFloat) { enableRandomWrite = true };
       
       // RenderTexture のVRAM確保
       velFirstReadRt.Create();
       velFirstWriteRt.Create();
       presFirstReadRt.Create();
       presFirstWriteRt.Create();
       _velocitySourceRt.Create();
       _divergenceRt.Create();
       
       // 初期状態を書き込む
       Graphics.Blit(Texture2D.blackTexture, velFirstReadRt);
       Graphics.Blit(Texture2D.blackTexture, presFirstReadRt);
       
       // PingPongラッパーにする
       _velocity = new PingPongRenderTexture(velFirstReadRt, velFirstWriteRt);
       _pressure = new PingPongRenderTexture(presFirstReadRt, presFirstWriteRt);
       
       // KernelIndexを取得
       _addVelocityKernelIndex = shader.FindKernel("AddVelocity");
       _advectVelocityKernelIndex = shader.FindKernel("AdvectVelocity");
       _diffuseVelocityKernelIndex = shader.FindKernel("DiffuseVelocity");
       _divergenceKernelIndex = shader.FindKernel("Divergence");
       _pressureJacobiKernelIndex  = shader.FindKernel("PressureJacobi");
       _subtractPressureGradientKernelIndex = shader.FindKernel("SubtractPressureGradient");
       
       // GroupSize(Threads数)を取得
       uint x, y, z;
       shader.GetKernelThreadGroupSizes(_addVelocityKernelIndex, out x, out y, out z);
       _addVelocityGroupSize = new Vector3Int((int)x, (int)y, (int)z);
       shader.GetKernelThreadGroupSizes(_advectVelocityKernelIndex, out x, out y, out z);
       _advectVelocityGroupSize = new Vector3Int((int)x, (int)y, (int)z);
       shader.GetKernelThreadGroupSizes(_diffuseVelocityKernelIndex, out x, out y, out z);
       _diffuseVelocityGroupSize = new Vector3Int((int)x, (int)y, (int)z);
       shader.GetKernelThreadGroupSizes(_divergenceKernelIndex, out x, out y, out z);
       _divergenceGroupSize = new Vector3Int((int)x, (int)y, (int)z);
       shader.GetKernelThreadGroupSizes(_pressureJacobiKernelIndex, out x, out y, out z);
       _pressureJacobiGroupSize = new Vector3Int((int)x, (int)y, (int)z);
       shader.GetKernelThreadGroupSizes(_subtractPressureGradientKernelIndex, out x, out y, out z);
       _subtractPressureGradientGroupSize = new Vector3Int((int)x, (int)y, (int)z);
       
       // InputBufferの初期化
       int stride = Marshal.SizeOf<InputData>();
       var input = new InputData[MaxInputs];
       _inputBuffer = new ComputeBuffer(MaxInputs, stride);
       _inputBuffer.SetData(input);
    }

   public RenderTexture Tick()
    {
        // 共通の値をセット
        shader.SetFloat("_DeltaTime", Time.deltaTime);
        shader.SetInts("_Resolution", _w, _h);
        shader.SetFloat("_Aspect", (float)_w / _h);
        shader.SetFloat("_VelocityBlendFactor", velocityBlendFactor);
        shader.SetFloat("_Viscosity", viscosity);
        
        #region Add Velocity
        if(inputSource.TryGetInput(out InputData[] input))
        {
            shader.SetInt( "_MaxInputs", MaxInputs);
            shader.SetInt( "_InputCount", input.Length);
            _inputBuffer.SetData(input);
            shader.SetBuffer(_addVelocityKernelIndex, "_InputBufferRead", _inputBuffer);
            shader.SetTexture(_addVelocityKernelIndex, "_VelocityFieldRead", _velocity.read);
            shader.SetTexture(_addVelocityKernelIndex, "_VelocityFieldWrite", _velocity.write);
            
            shader.Dispatch(_addVelocityKernelIndex,
                (_w + _addVelocityGroupSize.x - 1) / _addVelocityGroupSize.x,
                (_h + _addVelocityGroupSize.y - 1) / _addVelocityGroupSize.y,
                1);
            
            // Read用RTとWrite用RTを内部で入れ替える
            _velocity.Swap();
        }
        
        #endregion
        #region Advect Velocity
        
        shader.SetTexture(_advectVelocityKernelIndex, "_VelocityFieldRead", _velocity.read);
        shader.SetTexture(_advectVelocityKernelIndex, "_VelocityFieldWrite", _velocity.write);
        
        shader.Dispatch(_advectVelocityKernelIndex,
            (_w + _advectVelocityGroupSize.x - 1) / _advectVelocityGroupSize.x,
            (_h + _advectVelocityGroupSize.y - 1) / _advectVelocityGroupSize.y,
            1);
        
        _velocity.Swap();
        
        #endregion
        #region Diffuse Velocity

        Graphics.CopyTexture(_velocity.read, _velocitySourceRt);
        for (int i = 0; i < diffuseJacobiCount; i++)
        {
            shader.SetTexture(_diffuseVelocityKernelIndex, "_VelocityFieldRead", _velocity.read);
            shader.SetTexture(_diffuseVelocityKernelIndex, "_VelocityFieldWrite", _velocity.write);
            shader.SetTexture(_diffuseVelocityKernelIndex, "_VelocitySourceRead", _velocitySourceRt);

            shader.Dispatch(_diffuseVelocityKernelIndex,
                (_w + _diffuseVelocityGroupSize.x - 1) / _diffuseVelocityGroupSize.x,
                (_h + _diffuseVelocityGroupSize.y - 1) / _diffuseVelocityGroupSize.y,
                1);
            _velocity.Swap();
        }

        #endregion
        #region Divergence
        
        shader.SetTexture(_divergenceKernelIndex, "_VelocityFieldRead", _velocity.read);
        shader.SetTexture(_divergenceKernelIndex, "_DivergenceBufferWrite", _divergenceRt);
        
        shader.Dispatch(_divergenceKernelIndex,
            (_w + _divergenceGroupSize.x - 1) / _divergenceGroupSize.x,
            (_h + _divergenceGroupSize.y - 1) / _divergenceGroupSize.y,
            1);
        
        #endregion
        #region PressureJacobi
        
        shader.SetTexture(_pressureJacobiKernelIndex, "_DivergenceBufferRead", _divergenceRt);
        
        for (int i = 0; i < pressureJacobiCount; i++)
        {
            shader.SetTexture(_pressureJacobiKernelIndex, "_PressureBufferRead", _pressure.read);
            shader.SetTexture(_pressureJacobiKernelIndex, "_PressureBufferWrite", _pressure.write);
            
            shader.Dispatch(_pressureJacobiKernelIndex,
                (_w + _pressureJacobiGroupSize.x - 1)  / _pressureJacobiGroupSize.x,
                (_h + _pressureJacobiGroupSize.y - 1) / _pressureJacobiGroupSize.y,
                1);
            _pressure.Swap();
        }
        
        #endregion
        #region subtractPressureGradient
        
        shader.SetTexture(_subtractPressureGradientKernelIndex, "_PressureBufferRead", _pressure.read);
        shader.SetTexture(_subtractPressureGradientKernelIndex,  "_VelocityFieldRead", _velocity.read);
        shader.SetTexture(_subtractPressureGradientKernelIndex, "_VelocityFieldWrite", _velocity.write);
        shader.Dispatch(_subtractPressureGradientKernelIndex,
            (_w + _subtractPressureGradientGroupSize.x - 1) / _subtractPressureGradientGroupSize.x,
            (_h + _subtractPressureGradientGroupSize.y - 1) / _subtractPressureGradientGroupSize.y,
            1);
        _velocity.Swap();
        
        #endregion
        

        return _velocity.read;
    }

    

    private void OnDestroy()
    {
        _velocity?.Dispose();
        if(_velocitySourceRt != null) { UnityEngine.GameObject.Destroy(_velocitySourceRt); }
        _velocitySourceRt = null;
        if(_divergenceRt != null) { UnityEngine.GameObject.Destroy(_divergenceRt); }
        _divergenceRt = null;
        _pressure?.Dispose();
        _inputBuffer?.Release();
    }
}