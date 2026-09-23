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
        [SerializeField] private Texture2D dyeInitTex;
        [SerializeField] private RawImage rawImage;

        private PingPongRenderTexture _dyePingPong;
        private PingPongRenderTexture _velPingPong;
        
        private int _kernelId;
        private Vector3Int _groupSize;
        
        private int _addVelocityKernelIndex;
        private Vector3Int _addVelocityGroupSize;

        private int _advectDyeKernelIndex;
        private Vector3Int _advectDyeGroupSize;
        
        private void Start()
        {
           // RenderTextureの作成（PingPong用に2枚ずつ）
           var dyeFirstReadRt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBFloat) { enableRandomWrite = true };
           var dyeFirstWriteRt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBFloat) { enableRandomWrite = true };
           var velFirstReadRt = new RenderTexture(w, h, 0, RenderTextureFormat.RGFloat) { enableRandomWrite = true };
           var velFirstWriteRt = new RenderTexture(w, h, 0, RenderTextureFormat.RGFloat) { enableRandomWrite = true };
           
           // RenderTexture のVRAM確保
           dyeFirstReadRt.Create();
           dyeFirstWriteRt.Create();
           velFirstReadRt.Create();
           velFirstWriteRt.Create();
           
           // 初期状態を書き込む
           Graphics.Blit(Texture2D.blackTexture, velFirstReadRt);
           Graphics.Blit(dyeInitTex, dyeFirstReadRt);
           
           // PingPongラッパーにする
           _dyePingPong = new PingPongRenderTexture(dyeFirstReadRt, dyeFirstWriteRt);
           _velPingPong = new PingPongRenderTexture(velFirstReadRt, velFirstWriteRt);
           
           // KernelIndex, GroupSize(Threadsの数)を取得
           _addVelocityKernelIndex = shader.FindKernel("AddVelocity");
           _advectDyeKernelIndex = shader.FindKernel("AdvectDye");
           _kernelId = shader.FindKernel("KernelA");
           uint x, y, z;
           shader.GetKernelThreadGroupSizes(_kernelId, out x, out y, out z);
           _groupSize = new Vector3Int((int)x, (int)y, (int)z);
           shader.GetKernelThreadGroupSizes(_addVelocityKernelIndex, out x, out y, out z);
           _addVelocityGroupSize = new Vector3Int((int)x, (int)y, (int)z);
           shader.GetKernelThreadGroupSizes(_advectDyeKernelIndex, out x, out y, out z);
           _advectDyeGroupSize = new Vector3Int((int)x, (int)y, (int)z);
           
           // 反映テスト
           rawImage.texture = _dyePingPong.read;

           //shader.SetTexture(_kernelId, "_Buffer", _rt);
        }

        private void Update()
        {
            if(velocityInputMock.TryGetVelocityInput(out VelocityInputData velocityInput))
            {
                // 速度入力データをスクリーン座標に変換
                var position = new Vector2(velocityInput.PositionUv.x * w, velocityInput.PositionUv.y * h);
                var velocity = new Vector2(velocityInput.VelocityUv.x * w, velocityInput.VelocityUv.y * h);
                var radius = velocityInput.Radius * h;
                
                shader.SetVector("_InputPosition", position);
                shader.SetVector("_InputVelocity", velocity);
                shader.SetFloat("_InputRadius", radius);
                
                shader.SetTexture(_addVelocityKernelIndex, "_VelocityBufferRead", _velPingPong.read);
                shader.SetTexture(_addVelocityKernelIndex, "_VelocityBufferWrite", _velPingPong.write);
                shader.Dispatch(_addVelocityKernelIndex,
                    w / _addVelocityGroupSize.x,
                    h / _addVelocityGroupSize.y,
                    _addVelocityGroupSize.z);
                
                // Read用RTとWrite用RTを内部で入れ替える
                _velPingPong.Swap();
            }
            
            shader.SetFloat("_DeltaTime", Time.deltaTime);
            shader.SetInts("_Resolution", w, h);
            
            shader.SetTexture(_advectDyeKernelIndex, "_VelocityBufferRead", _velPingPong.read);
            shader.SetTexture(_advectDyeKernelIndex, "_DyeBufferRead", _dyePingPong.read);
            shader.SetTexture(_advectDyeKernelIndex, "_DyeBufferWrite", _dyePingPong.write);
            shader.Dispatch(_advectDyeKernelIndex,
                w / _advectDyeGroupSize.x,
                h / _advectDyeGroupSize.y,
                _advectDyeGroupSize.z);
            
            _dyePingPong.Swap();
            
        }
    }
}