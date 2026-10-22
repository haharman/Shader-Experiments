using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class Nebula : MonoBehaviour
{
    // DebugView で何を見るか。値は Nebula.compute の DEBUG_ と同じ並びにする
    private enum DebugMode
    {
        Final,
        Height,
        Density,
        Normal,
        Flux,
        Layers,
        FogFlux,
        IonizationParameter,
        DustPath,
        Reflection,
        Veil,
        VeilLight
    }
    
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
    [SerializeField] private Camera fishCamera; // 魚だけを描くカメラ。描いた絵を Compose が雲の手前に重ねる
    [SerializeField] private ComputeShader shader;
    [SerializeField] private float viewerHeight = 2f; // 観測者の高さ。すべての面の高さより大きくする
    [SerializeField] private float flowReferenceDistance = 0.25f; // 観測者からの距離がこの値の面で、流れの速さが速度場そのままになる
    [SerializeField] private int starCount = 1;
    [SerializeField] private int angleCount = 360;
    [SerializeField] private int radiusCount = 100;
    [SerializeField] private float minN = 0.02f;
    [SerializeField] private float minDist = 0.02f;
    [SerializeField] private float shadowFill = 0.05f;
    [SerializeField] private float bowlDepth = 0.3f; // お椀で、光源から最も遠い点が上がる量
    [SerializeField] private float hDiffMin = 0.001f; // 光源の高さ - 面の高さ の下限
    [SerializeField] private float cloudCellSize = 0.05f;

    [SerializeField] private float minHeight = 0.42f; // 雲の高さの最小値
    [SerializeField] private float maxHeight = 0.58f; // 雲の高さの最大値
    [SerializeField] private float starHeight;

    [Header("Nebula")] 
    [SerializeField] private int perlinSeed;
    
    [SerializeField] private float s2Thickness; // IBLの厚さ 密度で割られる
    [SerializeField] private float s2Weight;
    [SerializeField] private Color s2Color;
    
    [SerializeField] private float haThickness; // Haが光る領域の厚さ
    [SerializeField] private float haWeight;
    [SerializeField] private Color haColor;
    
    [SerializeField] private int layerSampleCount = 8; // 層を読む点の数
    [SerializeField] private float layerThkCutoffRatio = 3f; // 厚さの何倍まで読むか
    [SerializeField] private float nRef = 0.5f; // 層の厚さが基準そのものになる密度
    [SerializeField] private float limbGainMax = 6.7f; // 周縁増光の倍率の上限
    
    [SerializeField] private float o3Weight;
    [SerializeField] private Color o3Color;
    
    [SerializeField] private float fogScale = 0.03f; // 霧の明るさ
    [SerializeField] private float fogLodStart = 2f; // 霧に使う最も細かいミップレベル
    [SerializeField] private int fogLevels = 4; // 重ねるミップレベルの数
    [SerializeField] private float fogFalloff = 0.6f; // 粗いレベルほどの弱め方
    [SerializeField] private float logUMin = -1f; // 青緑が出始める log10(U)
    [SerializeField] private float logUMax = 3f; // 青緑が最も強くなる log10(U)
    
    [SerializeField] private float reflectionScale = 0.02f; // 反射光の明るさの倍率
    [SerializeField] private float hgG = 0.6f; // 位相関数の g（前へ散る偏り）
    [SerializeField] private float nReflect = 0.5f; // 反射が頭打ちになる密度の目安
    [SerializeField] private Vector3 opticalExtinction = new Vector3(20f, 28f, 40f); // dustPath に掛ける RGB の係数
    [SerializeField] private Color starlightColor = new Color(0.72f, 0.78f, 1f); // 星の光の色（白青）
    
    [Header("Veil")]
    [SerializeField] private int veilNoiseCellsY = 3; // 一番粗いオクターブの縦の格子数
    [SerializeField] private uint veilSeed = 1;
    [SerializeField] private int veilOctaves = 5;
    [SerializeField] private float veilWarp = 0.3f; // ノイズの位置を歪める量（uv）
    [SerializeField] private float veilCutoff = 0.5f; // これより低いノイズの所は塵なし
    [SerializeField] private float veilScale = 5f; // 密度の倍率。fBm の値は 0.5 のまわりに集まり n の最大が 0.5 程度なので、大きめにする
    [SerializeField] private float veilHeight = 1.2f; // 塵の高さ。光源より大きく、viewerHeight より小さく
    [SerializeField] private Vector3 veilExtinction = new Vector3(0.6f, 1f, 1.6f); // 吸収の RGB の係数（青ほど大きい）
    [SerializeField] private float veilFloor = 0.3f; // 画面全体に薄く広がる塵の量
    [SerializeField] private float veilGlowScale = 0.5f; // 散乱光の明るさの倍率
    [SerializeField] private float veilScatterN = 0.3f; // 散乱が頭打ちになる密度の目安
    [SerializeField] private float veilLightLength = 0.15f; // 塵が自分の影を落とす距離
    [SerializeField] private int veilLightTaps = 8; // その距離を読む点の数
    [SerializeField] private float veilLightExtinction = 8f; // 光源の方の塵の量に掛ける係数

    [Header("Starfield")]
    [SerializeField] private int starFieldCount; // 0 なら星空は描かない
    
    [Header("Debug")]
    [SerializeField] private float sunEuv;
    [SerializeField] private float sunOptical;
    [SerializeField] private DebugMode debugMode = DebugMode.Final;
    [SerializeField] private Vector2 debugRange = new Vector2(0f, 1f); // 0〜1 に伸ばす範囲（Flux は log10 の値）
    
    // Kernel
    private Kernel2D _writeGradients;
    private Kernel2D _accumulatePerlin;
    private Kernel2D _remapHeight;
    private Kernel2D _initializeCloud;
    private Kernel2D _initializeVeil;
    private Kernel2D _advectCloud;
    private Kernel2D _advectVeil;
    private Kernel2D _sweepHorizon;
    private Kernel2D _illuminateCloud;
    private Kernel2D _illuminateVeil;
    private Kernel2D _bakeStarField;
    private Kernel2D _compose;
    private Kernel2D _debugView;
    
    // RT
    private PingPongRenderTexture _cloudPingPong; // R=奥行き, G=密度
    private PingPongRenderTexture _veilPingPong; // R=密度

    private RenderTexture _gradient;
    private RenderTexture _shadowMap;
    private RenderTexture _flux; // EUVを受ける量
    private RenderTexture _reflection;
    private RenderTexture _veilLight; // 手前の塵の散乱光
    private RenderTexture _fish; // 魚だけを描いた絵（fishCamera の出力）
    private RenderTexture _nebula; // 色 完成品
    private RenderTexture _starField; // StarField（更新しない）
    
    
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
        _remapHeight = new Kernel2D(shader, "RemapHeight");
        _initializeCloud = new Kernel2D(shader, "InitializeCloud");
        _initializeVeil = new Kernel2D(shader, "InitializeVeil");
        _advectCloud = new Kernel2D(shader, "AdvectCloud");
        _advectVeil = new Kernel2D(shader, "AdvectVeil");
        _sweepHorizon = new Kernel2D(shader, "SweepHorizon");
        _illuminateCloud = new Kernel2D(shader, "IlluminateCloud");
        _illuminateVeil = new Kernel2D(shader, "IlluminateVeil");
        _bakeStarField = new Kernel2D(shader, "BakeStarField");
        _compose = new Kernel2D(shader, "Compose");
        _debugView = new Kernel2D(shader, "DebugView");
        
        // RT
        var cloudRead = new RenderTexture(_res.x, _res.y, 0, RenderTextureFormat.RGFloat){ enableRandomWrite = true };
        var cloudWrite = new RenderTexture(_res.x, _res.y, 0, RenderTextureFormat.RGFloat){ enableRandomWrite = true };
        cloudRead.Create();
        cloudWrite.Create();
        _cloudPingPong = new PingPongRenderTexture(cloudRead, cloudWrite);
        BakePerlinNoise(cloudRead, cloudCellSize);
        debugRawImage.texture = _cloudPingPong.read;
        
        var veilRead = new RenderTexture(_res.x, _res.y, 0, RenderTextureFormat.RFloat){ enableRandomWrite = true };
        var veilWrite = new RenderTexture(_res.x, _res.y, 0, RenderTextureFormat.RFloat){ enableRandomWrite = true };
        veilRead.Create();
        veilWrite.Create();
        _veilPingPong = new PingPongRenderTexture(veilRead, veilWrite);
        
        var desc = new RenderTextureDescriptor(angleCount, radiusCount,RenderTextureFormat.RGHalf, 0);
        desc.dimension = TextureDimension.Tex2DArray;
        desc.volumeDepth = MaxStars;
        desc.enableRandomWrite = true;
        _shadowMap = new RenderTexture(desc);
        _shadowMap.wrapModeU = TextureWrapMode.Repeat;
        _shadowMap.wrapModeV = TextureWrapMode.Clamp;
        _shadowMap.Create();
        
        _gradient = new RenderTexture(_res.x, _res.y, 0, RenderTextureFormat.RGFloat){ enableRandomWrite = true };
        _flux = new RenderTexture(_res.x, _res.y, 0, RenderTextureFormat.RHalf){ enableRandomWrite = true, useMipMap = true, autoGenerateMips = false };
        _reflection = new RenderTexture(_res.x, _res.y, 0, RenderTextureFormat.ARGBHalf){ enableRandomWrite = true };
        _veilLight = new RenderTexture(_res.x, _res.y, 0, RenderTextureFormat.ARGBHalf){ enableRandomWrite = true };
        _nebula = new RenderTexture(_res.x, _res.y, 0, RenderTextureFormat.ARGBHalf){ enableRandomWrite = true };
        _starField = new RenderTexture(_res.x, _res.y, 0, RenderTextureFormat.ARGBHalf){ enableRandomWrite = true };

        _gradient.Create();
        _flux.Create();
        _reflection.Create();
        _veilLight.Create();

        // 魚: fishCamera の出力先。背景を透明にして、魚がある所だけ A を 1 にする
        _fish = new RenderTexture(_res.x, _res.y, 24, RenderTextureFormat.ARGBHalf);
        _fish.Create();
        ClearRt(_fish);
        if (fishCamera != null)
        {
            fishCamera.clearFlags = CameraClearFlags.SolidColor;
            fishCamera.backgroundColor = Color.clear;
            fishCamera.targetTexture = _fish;
        }
        _nebula.Create();
        _starField.Create();
        
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
        RemapHeight();
        InitializeVeil();
        //Perlin(20f, 0.6f);
        //Perlin(10f, 0.1f);

        // StarField
        BakeStarField();
    }

    /// <summary>
    /// StarField に starFieldCount 個の星を1ピクセルずつ焼き込む。初期化のときだけ呼ぶ
    /// </summary>
    private void BakeStarField()
    {
        ClearRt(_starField);
        if (starFieldCount <= 0) return;

        shader.SetInts("_Resolution", _res.x, _res.y);
        shader.SetInt("_StarFieldCount", starFieldCount);
        _bakeStarField.SetTexture("_StarFieldWrite", _starField);
        _bakeStarField.Dispatch(new Vector2Int(starFieldCount, 1));
    }

    /// <summary>
    /// 雲の高さ（cloud の R）を、実際の最小値・最大値を使って minHeight〜maxHeight に引き伸ばす。初期化のときだけ呼ぶ
    /// </summary>
    private void RemapHeight()
    {
        // GPU の値を CPU に読み戻して、高さの最小値と最大値を求める
        var request = AsyncGPUReadback.Request(_cloudPingPong.read);
        request.WaitForCompletion();
        float srcMin = float.MaxValue;
        float srcMax = float.MinValue;
        foreach (var cloud in request.GetData<Vector2>())
        {
            srcMin = Mathf.Min(srcMin, cloud.x);
            srcMax = Mathf.Max(srcMax, cloud.x);
        }

        shader.SetInts("_Resolution", _res.x, _res.y);
        shader.SetFloat("_HeightSrcMin", srcMin);
        shader.SetFloat("_HeightSrcMax", srcMax);
        shader.SetFloat("_HeightMin", minHeight);
        shader.SetFloat("_HeightMax", maxHeight);

        _remapHeight.SetTexture("_CloudRead", _cloudPingPong.read);
        _remapHeight.SetTexture("_CloudWrite", _cloudPingPong.write);
        _remapHeight.Dispatch(_res);
        _cloudPingPong.Swap();
    }

    /// <summary>
    /// 手前の塵の密度（veil）をノイズで作る。初期化のときだけ呼ぶ
    /// </summary>
    private void InitializeVeil()
    {
        shader.SetInts("_Resolution", _res.x, _res.y);
        shader.SetFloat("_Aspect", (float)_res.x / _res.y);
        SetVeilUniforms();

        _initializeVeil.SetTexture("_VeilWrite", _veilPingPong.read);
        _initializeVeil.Dispatch(_res);
    }

    private void SetVeilUniforms()
    {
        shader.SetInt("_VeilNoiseCellsY", veilNoiseCellsY);
        shader.SetInt("_VeilSeed", (int)veilSeed);
        shader.SetInt("_VeilOctaves", veilOctaves);
        shader.SetFloat("_VeilWarp", veilWarp);
        shader.SetFloat("_VeilCutoff", veilCutoff);
        shader.SetFloat("_VeilScale", veilScale);
        shader.SetFloat("_VeilHeight", veilHeight);
        shader.SetVector("_VeilExtinction", veilExtinction);
        shader.SetFloat("_VeilFloor", veilFloor);
        shader.SetFloat("_VeilGlowScale", veilGlowScale);
        shader.SetFloat("_VeilScatterN", veilScatterN);
        shader.SetFloat("_VeilLightLength", veilLightLength);
        shader.SetInt("_VeilLightTaps", veilLightTaps);
        shader.SetFloat("_VeilLightExtinction", veilLightExtinction);
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
        shader.SetFloat("_ViewerHeight", viewerHeight);
        shader.SetFloat("_FlowReferenceDistance", flowReferenceDistance);
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
        shader.SetFloat("_BowlDepth", bowlDepth);
        shader.SetFloat("_HDiffMin", hDiffMin);
        shader.SetVector("_S2Color", s2Color.linear);
        shader.SetVector("_HaColor", haColor.linear);
        shader.SetFloat("_S2Thickness", s2Thickness);
        shader.SetFloat("_HaThickness", haThickness);
        shader.SetFloat("_S2Weight", s2Weight);
        shader.SetFloat("_HaWeight", haWeight);
        shader.SetInt("_LayerSampleCount", layerSampleCount);
        shader.SetFloat("_LayerThkCutoffRatio", layerThkCutoffRatio);
        shader.SetFloat("_NRef", nRef);
        shader.SetFloat("_LimbGainMax", limbGainMax);
        shader.SetFloat("_FogScale", fogScale);
        shader.SetFloat("_FogLodStart", fogLodStart);
        shader.SetInt("_FogLevels", fogLevels);
        shader.SetFloat("_FogFalloff", fogFalloff);
        shader.SetVector("_O3Color", o3Color.linear);
        shader.SetFloat("_O3Weight", o3Weight);
        shader.SetFloat("_LogUMin", logUMin);
        shader.SetFloat("_LogUMax", logUMax);
        shader.SetFloat("_ReflectionScale", reflectionScale);
        shader.SetFloat("_HgG", hgG);
        shader.SetFloat("_NReflect", nReflect);
        shader.SetVector("_OpticalExtinction", opticalExtinction);
        shader.SetVector("_StarlightColor", starlightColor.linear);
        SetVeilUniforms();
        shader.SetInt("_DebugMode", (int)debugMode);
        shader.SetVector("_DebugRange", debugRange);

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
        _veilPingPong.Swap();
        
        
        _cloudPingPong.Swap();

        _sweepHorizon.SetTexture("_CloudRead", _cloudPingPong.read);
        _sweepHorizon.SetTexture("_ShadowMapWrite", _shadowMap);
        _sweepHorizon.SetBuffer("_StarRead", _lightsBuffer);
        _sweepHorizon.Dispatch(new Vector2Int(angleCount, starCount));

        _illuminateCloud.SetTexture("_CloudRead", _cloudPingPong.read);
        _illuminateCloud.SetTexture("_ShadowMapRead", _shadowMap);
        _illuminateCloud.SetBuffer("_StarRead", _lightsBuffer);
        _illuminateCloud.SetTexture("_FluxWrite", _flux);
        _illuminateCloud.SetTexture("_ReflectionWrite", _reflection);
        _illuminateCloud.Dispatch(_res);
        _flux.GenerateMips(); // 霧のぼかしに使う粗いミップレベルを作る

        _compose.SetTexture("_CloudRead", _cloudPingPong.read);
        _compose.SetTexture("_FluxRead", _flux);
        _illuminateVeil.SetTexture("_VeilRead", _veilPingPong.read);
        _illuminateVeil.SetBuffer("_StarRead", _lightsBuffer);
        _illuminateVeil.SetTexture("_VeilLightWrite", _veilLight);
        _illuminateVeil.Dispatch(_res);

        _compose.SetTexture("_ReflectionRead", _reflection);
        _compose.SetTexture("_VeilRead", _veilPingPong.read);
        _compose.SetTexture("_FishRead", _fish);
        _compose.SetTexture("_StarFieldRead", _starField);
        _compose.SetTexture("_VeilLightRead", _veilLight);
        _compose.SetTexture("_NebulaWrite", _nebula);
        _compose.Dispatch(_res);

        // Final 以外のときは、Compose の結果を DebugView の表示で上書きする
        if (debugMode != DebugMode.Final)
        {
            _debugView.SetTexture("_CloudRead", _cloudPingPong.read);
            _debugView.SetTexture("_FluxRead", _flux);
            _debugView.SetTexture("_ReflectionRead", _reflection);
            _debugView.SetTexture("_VeilRead", _veilPingPong.read);
            _debugView.SetTexture("_VeilLightRead", _veilLight);
            _debugView.SetTexture("_ShadowMapRead", _shadowMap);
            _debugView.SetBuffer("_StarRead", _lightsBuffer);
            _debugView.SetTexture("_NebulaWrite", _nebula);
            _debugView.Dispatch(_res);
        }

        debugRawImage.texture = _nebula;
        //Graphics.Blit(_veil.read, _nebula.read, addVeilMat);
        return _cloudPingPong.read;
    }
    
    private static void BakePerlinNoise(RenderTexture target, float cellsY)
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
                data[i]     = Mathf.PerlinNoise(px, py);
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
        if (fishCamera != null) fishCamera.targetTexture = null;
        if(_fish != null)_fish.Release();
        _fish = null;
        if(_veilLight != null)_veilLight.Release();
        _veilLight = null;
        if(_nebula != null)_nebula.Release();
        _nebula = null;
        if(_starField != null)_starField.Release();
        _starField = null;
    }
}
