using UnityEngine;
using UnityEngine.UI;

namespace StableFluids
{
    public class StableFluids : MonoBehaviour
    {
        [SerializeField] private VelocityInputMock velocityInputMock;
        [SerializeField] private ComputeShader shader;
        [SerializeField] private int w;
        [SerializeField] private int h;
        [SerializeField, Range(1, 1000)] private int pressureJacobiCount;
        [SerializeField] private Texture2D dyeInitTex;
        [SerializeField] private RawImage rawImage;

        private PingPongRenderTexture _dye;
        private PingPongRenderTexture _velocity;
        private PingPongRenderTexture _pressure;
        private RenderTexture _divergenceRt;
        
        private int _addVelocityKernelIndex;
        private Vector3Int _addVelocityGroupSize;

        private int _advectVelocityKernelIndex;
        private Vector3Int _advectVelocityGroupSize;
        
        private int _divergenceKernelIndex;
        private Vector3Int _divergenceGroupSize;
        
        private int _pressureJacobiKernelIndex;
        private Vector3Int _pressureJacobiGroupSize;

        private int _advectDyeKernelIndex;
        private Vector3Int _advectDyeGroupSize;

        private int _subtractPressureGradientKernelIndex;
        private Vector3Int _subtractPressureGradientGroupSize;
        
        private void Start()
        {
           // RenderTextureの作成（PingPong用に2枚ずつ）
           var dyeFirstReadRt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBFloat) { enableRandomWrite = true };
           var dyeFirstWriteRt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBFloat) { enableRandomWrite = true };
           var velFirstReadRt = new RenderTexture(w, h, 0, RenderTextureFormat.RGFloat) { enableRandomWrite = true };
           var velFirstWriteRt = new RenderTexture(w, h, 0, RenderTextureFormat.RGFloat) { enableRandomWrite = true };
           var presFirstReadRt = new RenderTexture(w, h, 0, RenderTextureFormat.RFloat) { enableRandomWrite = true };
           var presFirstWriteRt = new RenderTexture(w, h, 0, RenderTextureFormat.RFloat) { enableRandomWrite = true };
           _divergenceRt = new  RenderTexture(w, h, 0, RenderTextureFormat.RFloat) { enableRandomWrite = true };
           
           // RenderTexture のVRAM確保
           dyeFirstReadRt.Create();
           dyeFirstWriteRt.Create();
           velFirstReadRt.Create();
           velFirstWriteRt.Create();
           presFirstReadRt.Create();
           presFirstWriteRt.Create();
           _divergenceRt.Create();
           
           // 初期状態を書き込む
           Graphics.Blit(Texture2D.blackTexture, velFirstReadRt);
           Graphics.Blit(dyeInitTex, dyeFirstReadRt);
           
           // PingPongラッパーにする
           _dye = new PingPongRenderTexture(dyeFirstReadRt, dyeFirstWriteRt);
           _velocity = new PingPongRenderTexture(velFirstReadRt, velFirstWriteRt);
           _pressure = new PingPongRenderTexture(presFirstReadRt, presFirstWriteRt);
           
           // KernelIndexを取得
           _addVelocityKernelIndex = shader.FindKernel("AddVelocity");
           _advectVelocityKernelIndex = shader.FindKernel("AdvectVelocity");
           _divergenceKernelIndex = shader.FindKernel("Divergence");
           _pressureJacobiKernelIndex  = shader.FindKernel("PressureJacobi");
           _subtractPressureGradientKernelIndex = shader.FindKernel("SubtractPressureGradient");
           _advectDyeKernelIndex = shader.FindKernel("AdvectDye");
           
           // GroupSize(Threads数)を取得
           uint x, y, z;
           shader.GetKernelThreadGroupSizes(_addVelocityKernelIndex, out x, out y, out z);
           _addVelocityGroupSize = new Vector3Int((int)x, (int)y, (int)z);
           shader.GetKernelThreadGroupSizes(_advectVelocityKernelIndex, out x, out y, out z);
           _advectVelocityGroupSize = new Vector3Int((int)x, (int)y, (int)z);
           shader.GetKernelThreadGroupSizes(_divergenceKernelIndex, out x, out y, out z);
           _divergenceGroupSize = new Vector3Int((int)x, (int)y, (int)z);
           shader.GetKernelThreadGroupSizes(_pressureJacobiKernelIndex, out x, out y, out z);
           _pressureJacobiGroupSize = new Vector3Int((int)x, (int)y, (int)z);
           shader.GetKernelThreadGroupSizes(_subtractPressureGradientKernelIndex, out x, out y, out z);
           _subtractPressureGradientGroupSize = new Vector3Int((int)x, (int)y, (int)z);
           shader.GetKernelThreadGroupSizes(_advectDyeKernelIndex, out x, out y, out z);
           _advectDyeGroupSize = new Vector3Int((int)x, (int)y, (int)z);
           
           // 反映テスト
           rawImage.texture = _dye.read;

           //shader.SetTexture(_kernelId, "_Buffer", _rt);
        }

        private void Update()
        {
            // 共通の値をセット
            shader.SetFloat("_DeltaTime", Time.deltaTime);
            shader.SetInts("_Resolution", w, h);
            
            #region Add Velocity
            if(velocityInputMock.TryGetVelocityInput(out VelocityInputData velocityInput))
            {
                // 速度入力データを格子座標に変換
                var position = new Vector2(velocityInput.PositionUv.x * w, velocityInput.PositionUv.y * h);
                var velocity = new Vector2(velocityInput.VelocityUv.x * w, velocityInput.VelocityUv.y * h);
                var radius = velocityInput.Radius * h;
                
                shader.SetVector("_InputPosition", position);
                shader.SetVector("_InputVelocity", velocity);
                shader.SetFloat("_InputRadius", radius);
                
                shader.SetTexture(_addVelocityKernelIndex, "_VelocityBufferRead", _velocity.read);
                shader.SetTexture(_addVelocityKernelIndex, "_VelocityBufferWrite", _velocity.write);
                
                shader.Dispatch(_addVelocityKernelIndex,
                    (w + _addVelocityGroupSize.x - 1) / _addVelocityGroupSize.x,
                    (h + _addVelocityGroupSize.y - 1) / _addVelocityGroupSize.y,
                    1);
                
                // Read用RTとWrite用RTを内部で入れ替える
                _velocity.Swap();
            }
            
            #endregion
            #region Advect Velocity
            
            shader.SetTexture(_advectVelocityKernelIndex, "_VelocityBufferRead", _velocity.read);
            shader.SetTexture(_advectVelocityKernelIndex, "_VelocityBufferWrite", _velocity.write);
            
            shader.Dispatch(_advectVelocityKernelIndex,
                (w + _advectVelocityGroupSize.x - 1) / _advectVelocityGroupSize.x,
                (h + _advectVelocityGroupSize.y - 1) / _advectVelocityGroupSize.y,
                1);
            
            _velocity.Swap();
            #endregion
            #region Divergence
            
            shader.SetTexture(_divergenceKernelIndex, "_VelocityBufferRead", _velocity.read);
            shader.SetTexture(_divergenceKernelIndex, "_DivergenceBufferWrite", _divergenceRt);
            
            shader.Dispatch(_divergenceKernelIndex,
                (w + _divergenceGroupSize.x - 1) / _divergenceGroupSize.x,
                (h + _divergenceGroupSize.y - 1) / _divergenceGroupSize.y,
                1);
            
            #endregion
            #region PressureJacobi
            
            _pressure.ClearRead();
            
            shader.SetTexture(_pressureJacobiKernelIndex, "_DivergenceBufferRead", _divergenceRt);
            
            for (int i = 0; i < pressureJacobiCount; i++)
            {
                shader.SetTexture(_pressureJacobiKernelIndex, "_PressureBufferRead", _pressure.read);
                shader.SetTexture(_pressureJacobiKernelIndex, "_PressureBufferWrite", _pressure.write);
                shader.Dispatch(_pressureJacobiKernelIndex,
                    (w + _pressureJacobiGroupSize.x - 1) / _pressureJacobiGroupSize.x,
                    (h + _pressureJacobiGroupSize.y - 1) / _pressureJacobiGroupSize.y,
                    1);
                _pressure.Swap();
            }
            
            #endregion
            #region PressureJacobi
            
            shader.SetTexture(_subtractPressureGradientKernelIndex, "_PressureBufferRead", _pressure.read);
            shader.SetTexture(_subtractPressureGradientKernelIndex,  "_VelocityBufferRead", _velocity.read);
            shader.SetTexture(_subtractPressureGradientKernelIndex, "_VelocityBufferWrite", _velocity.write);
            shader.Dispatch(_subtractPressureGradientKernelIndex,
                (w + _subtractPressureGradientGroupSize.x - 1) / _subtractPressureGradientGroupSize.x,
                (h + _subtractPressureGradientGroupSize.y - 1) / _subtractPressureGradientGroupSize.y,
                1);
            _velocity.Swap();
            
            #endregion
            #region Advect Dye
            
            shader.SetTexture(_advectDyeKernelIndex, "_VelocityBufferRead", _velocity.read);
            shader.SetTexture(_advectDyeKernelIndex, "_DyeBufferRead", _dye.read);
            shader.SetTexture(_advectDyeKernelIndex, "_DyeBufferWrite", _dye.write);
            
            shader.Dispatch(_advectDyeKernelIndex,
                (w + _advectDyeGroupSize.x - 1) / _advectDyeGroupSize.x,
                (h + _advectDyeGroupSize.y - 1) / _advectDyeGroupSize.y,
                1);
            
            _dye.Swap();
            
            #endregion
            
            //rawImage.texture = _dye.read;
            rawImage.texture = _dye.read;
        }

        private void OnDestroy()
        {
            _velocity?.Dispose();
            if(_divergenceRt != null) { UnityEngine.GameObject.Destroy(_divergenceRt); }
            _divergenceRt = null;
            _pressure?.Dispose();
            _dye?.Dispose();
        }
    }
}