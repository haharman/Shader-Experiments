using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class Nebula : MonoBehaviour
{
    private struct StarData
    {
        public Vector3 Position;
        public float Euv;
        public float Optical;
    };
    
    // Const
    private const int MaxStars = 10;
    
    // Inspector
    [SerializeField] private Material addVeilMat;
    [SerializeField] private RawImage debugRawImage;
    [SerializeField] private ComputeShader shader;
    [SerializeField] private float advectCloudWeight;
    [SerializeField] private float advectVeilWeight;
    [SerializeField] private int starCount = 1;
    [SerializeField] private int angleCount = 360;
    [SerializeField] private int radiusCount = 100;
    [SerializeField] private float minN = 0.02f;
    [SerializeField] private float minDist = 0.02f;
    [SerializeField] private float shadowFill = 0.05f;
    [SerializeField] private float cloudCellSize = 0.05f;
    [SerializeField] private float parallaxWeight = 1f;

    [SerializeField] private float cloudHeightMax;
    [SerializeField] private float starHeight;

    [Header("Nebula")] 
    [SerializeField] private int perlinSeed;
    
    [SerializeField] private float s2Thickness; // IBLの厚さ 密度で割られる
    [SerializeField] private float s2Weight;
    [SerializeField] private Color s2Color;
    
    [SerializeField] private float haThickness; // Haが光る領域の厚さ
    [SerializeField] private float haWeight;
    [SerializeField] private Color haColor;
    
    [SerializeField] private float o3Weight;
    [SerializeField] private Color o3Color;
    
    [Header("Debug")]
    [SerializeField] private float sunEuv;
    [SerializeField] private float sunOptical;
    
    // Kernel
    private Kernel2D _writeGradients;
    private Kernel2D _accumulatePerlin;
    private Kernel2D _initializeCloud;
    private Kernel2D _initializeVeil;
    private Kernel2D _advectCloud;
    private Kernel2D _advectVeil;
    private Kernel2D _sweepHorizon;
    private Kernel2D _illuminateCloud;
    private Kernel2D _illuminateVeil;
    private Kernel2D _compose;
    
    // RT
    private PingPongRenderTexture _cloudPingPong; // R=奥行き, G=密度
    private PingPongRenderTexture _veilPingPong; // R=密度

    private RenderTexture _gradient;
    private RenderTexture _shadowMap;
    private RenderTexture _flux; // EUVを受ける量
    private RenderTexture _reflection;
    private RenderTexture _nebula; // 色 完成品
    
    
    private RenderTexture _spawnFieldRt; // 星団がスポーンしやすい位置

    // Buffer
    private ComputeBuffer _lightsBuffer;
    
    private Vector2Int _res;
    
    public void Initialize(Vector2Int resolution)
    {
        _res = resolution;
        
        // Kernel
        _writeGradients = new Kernel2D(shader, "WriteGradients");
        _accumulatePerlin = new Kernel2D(shader, "AccumulatePerlin");
        _initializeCloud = new Kernel2D(shader, "InitializeCloud");
        _initializeVeil = new Kernel2D(shader, "InitializeVeil");
        _advectCloud = new Kernel2D(shader, "AdvectCloud");
        _advectVeil = new Kernel2D(shader, "AdvectVeil");
        _sweepHorizon = new Kernel2D(shader, "SweepHorizon");
        _illuminateCloud = new Kernel2D(shader, "IlluminateCloud");
        _illuminateVeil = new Kernel2D(shader, "IlluminateVeil");
        _compose = new Kernel2D(shader, "Compose");
        
        // RT
        var cloudRead = new RenderTexture(_res.x, _res.y, 0, RenderTextureFormat.RGFloat){ enableRandomWrite = true };
        var cloudWrite = new RenderTexture(_res.x, _res.y, 0, RenderTextureFormat.RGFloat){ enableRandomWrite = true };
        cloudRead.Create();
        cloudWrite.Create();
        _cloudPingPong = new PingPongRenderTexture(cloudRead, cloudWrite);
        BakePerlinNoise(cloudRead, cloudCellSize, cloudHeightMax);
        debugRawImage.texture = _cloudPingPong.read;
        
        var veilRead = new RenderTexture(_res.x, _res.y, 0, RenderTextureFormat.RFloat){ enableRandomWrite = true };
        var veilWrite = new RenderTexture(_res.x, _res.y, 0, RenderTextureFormat.RFloat){ enableRandomWrite = true };
        veilRead.Create();
        veilWrite.Create();
        _veilPingPong = new PingPongRenderTexture(veilRead, veilWrite);
        BakePerlinNoise(veilRead, 10);
        
        var desc = new RenderTextureDescriptor(angleCount, radiusCount,RenderTextureFormat.RGHalf, 0);
        desc.dimension = TextureDimension.Tex2DArray;
        desc.volumeDepth = MaxStars;
        desc.enableRandomWrite = true;
        _shadowMap = new RenderTexture(desc);
        _shadowMap.wrapModeU = TextureWrapMode.Repeat;
        _shadowMap.wrapModeV = TextureWrapMode.Clamp;
        _shadowMap.Create();
        
        _gradient = new RenderTexture(_res.x, _res.y, 0, RenderTextureFormat.RGFloat){ enableRandomWrite = true };
        _flux = new RenderTexture(_res.x, _res.y, 0, RenderTextureFormat.RHalf, mipCount:0){ enableRandomWrite = true };
        _reflection = new RenderTexture(_res.x, _res.y, 0, RenderTextureFormat.ARGBHalf){ enableRandomWrite = true };
        _nebula = new RenderTexture(_res.x, _res.y, 0, RenderTextureFormat.ARGBHalf){ enableRandomWrite = true };

        _gradient.Create();
        _flux.Create();
        _reflection.Create();
        _nebula.Create();
        
        ClearRt(_gradient);
        
        // ComputeBuffer
        int stride = Marshal.SizeOf(typeof(StarData));
        _lightsBuffer = new ComputeBuffer(MaxStars, stride); // 最適化 64とかのほうがいい？
        var initial = new StarData[MaxStars];
        var pos = new Vector3(0f, 0f, starHeight);
        for (int i = 0; i < MaxStars; i++)
        {
            initial[i] = new StarData
            {
                Position = pos,
                Euv = 0f,
                Optical = 0f
            };
        }
        // デバッグ用
        var sunPos = new Vector3(0.5f, 0.5f, starHeight);
        initial[0].Position = sunPos;
        initial[0].Euv = sunEuv;
        initial[0].Optical = sunOptical;
        _lightsBuffer.SetData(initial);
        
        // ノイズ作成
        Perlin(100f, 1f);
        //Perlin(20f, 0.6f);
        //Perlin(10f, 0.1f);
    }

    private void Perlin(float size, float weight)
    {
        Vector2Int res = Vector2Int.CeilToInt((Vector2)_res / size);

        shader.SetInts("_Resolution", _res.x, _res.y);
        shader.SetInts("_PerlinResolution", res.x, res.y);
        shader.SetInt("_PerlinSeed", perlinSeed);
        shader.SetFloat("_PerlinWeight", weight);
        
        _writeGradients.SetTexture("_GradientWrite", _gradient);
        _writeGradients.Dispatch(res);
        
        _accumulatePerlin.SetTexture("_GradientRead", _gradient);
        _accumulatePerlin.SetTexture("_CloudRead", _cloudPingPong.read);
        _accumulatePerlin.SetTexture("_CloudWrite", _cloudPingPong.write);
        _accumulatePerlin.Dispatch(_res);
        
        _cloudPingPong.Swap();
    }
    
    public RenderTexture Tick(RenderTexture velocityField)
    {
        shader.SetFloat("_AdvectCloudWeight", advectCloudWeight);
        shader.SetFloat("_AdvectVeilWeight", advectVeilWeight);
        shader.SetInts("_Resolution", _res.x, _res.y);
        shader.SetFloat("_DeltaTime", Time.deltaTime);

        if (starCount > MaxStars)
        {
            Debug.LogError("[Nebula] StarCount が MaxStars を超えています");
            starCount = MaxStars;
        }
        float aspect = (float)_res.x / _res.y;
        float maxRadius = Mathf.Sqrt((aspect / 2f) * (aspect / 2f) + 0.5f * 0.5f);
        shader.SetFloat("_Aspect", aspect);
        shader.SetInt("_AngleCount", angleCount);
        shader.SetFloat("_AngleStep", 2f * Mathf.PI / angleCount);
        shader.SetInt("_RadiusCount", radiusCount);
        shader.SetFloat("_MaxRadius", maxRadius);
        shader.SetFloat("_RadiusStep", maxRadius / radiusCount);
        shader.SetInt("_StarCount", starCount);
        shader.SetFloat("_MinN", minN);
        shader.SetFloat("_MinDist", minDist);
        shader.SetFloat("_ShadowFill", shadowFill);
        shader.SetFloat("_ParallaxWeight", parallaxWeight);
        shader.SetVector("_S2Color", s2Color.linear);

        _initializeCloud.SetTexture("_CloudWrite", _cloudPingPong.read);
        _initializeCloud.Dispatch(_res);
        
        _advectCloud.SetTexture("_VelocityRead", velocityField);
        _advectCloud.SetTexture("_CloudRead", _cloudPingPong.read);
        _advectCloud.SetTexture("_CloudWrite", _cloudPingPong.write);
        _advectCloud.Dispatch(_res);

        _advectVeil.SetTexture("_VelocityRead", velocityField);
        _advectVeil.SetTexture("_VeilRead", _veilPingPong.read);
        _advectVeil.SetTexture("_VeilWrite", _veilPingPong.write);
        _advectVeil.Dispatch(_res);
        
        
        _cloudPingPong.Swap();

        _sweepHorizon.SetTexture("_CloudRead", _cloudPingPong.read);
        _sweepHorizon.SetTexture("_ShadowMapWrite", _shadowMap);
        _sweepHorizon.SetBuffer("_StarRead", _lightsBuffer);
        _sweepHorizon.Dispatch(new Vector2Int(angleCount, starCount));

        _illuminateCloud.SetTexture("_CloudRead", _cloudPingPong.read);
        _illuminateCloud.SetTexture("_ShadowMapRead", _shadowMap);
        _illuminateCloud.SetBuffer("_StarRead", _lightsBuffer);
        _illuminateCloud.SetTexture("_FluxWrite", _flux);
        _illuminateCloud.SetTexture("_NebulaWrite", _nebula);
        _illuminateCloud.Dispatch(_res);

        debugRawImage.texture = _nebula;
        //Graphics.Blit(_veil.read, _nebula.read, addVeilMat);
        return _cloudPingPong.read;
    }
    
    private static void BakePerlinNoise(RenderTexture target, float cellsY, float maxR = float.MaxValue)
    {
        int w = target.width;
        int h = target.height;
        float[] data = new float[w * h * 2];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float px = (float)x / h * cellsY;
                float py = (float)y / h * cellsY;
                int i = (y * w + x) * 2;
                data[i]     = Mathf.Min(Mathf.PerlinNoise(px, py), maxR);
                data[i + 1] = Mathf.PerlinNoise(px + 123.4f, py + 567.8f);
                data[i + 1] = Mathf.PerlinNoise(px + 123.4f, py + 567.8f);
            }
        }

        var texture = new Texture2D(w, h, TextureFormat.RGFloat, false, true);
        texture.SetPixelData(data, 0);
        texture.Apply(false);

        Graphics.Blit(texture, target);
        Object.Destroy(texture);
    }

    /// <summary>
    /// RTを0で埋める
    /// </summary>
    /// <param name="rt"></param>
    private void ClearRt(RenderTexture rt)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(false, true, Color.clear);
        RenderTexture.active = prev;
    }

    private void OnDestroy()
    {
        _cloudPingPong.Dispose();
        _veilPingPong.Dispose();
        _lightsBuffer.Release();
        
        if(_gradient != null) _gradient.Release();
        _gradient = null;
        if(_shadowMap != null) _shadowMap.Release();
        _shadowMap = null;
        if(_flux != null)_flux.Release();
        _flux = null;
        if(_reflection != null)_reflection.Release();
        _reflection = null;
        if(_nebula != null)_nebula.Release();
        _nebula = null;
    }
}
