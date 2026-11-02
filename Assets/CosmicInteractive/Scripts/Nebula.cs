using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Serialization;
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

    // 雲（高さ R・密度 G）の、GPU から読み戻した最小・最大・平均
    private struct CloudStats
    {
        public Vector2 Min;
        public Vector2 Max;
        public Vector2 Mean;
    }

    // 雲のシードの切り替えの段階
    private enum SeedPhase
    {
        Waiting,    // 今の雲のまま、次の切り替えまで待つ
        Generating, // 新しい雲を作って、範囲を読み戻している
        Blending    // 今の雲から新しい雲へ、寄せている
    }

    // Const
    private const int MaxStars = 10;
    private const int DensitySeedOffset = 7919; // 密度のノイズのシードを、高さからずらす量
    private const int SeedStride = 1000; // 連番のシードを実際のシードにするときの刻み。Fbm がオクターブごとに seed + o、ワープが seed + 100, 200 を使うので、それより大きくして重ならないようにする
    private const float HeightBaseOffsetStep = 37.3f; // 高さの土台（Mathf.PerlinNoise）の読む位置を、シード 1 つごとにずらす量（格子単位）
    private const int HeightBaseTexelsPerCell = 16; // 高さの土台を焼く解像度。格子 1 つあたりのテクセル数（バイリニアで拡大する）
    
    // Inspector
    [SerializeField] private Material addVeilMat;
    [SerializeField] private RawImage debugRawImage;
    [SerializeField] private Camera fishCamera; // 魚だけを描くカメラ。描いた絵を Compose が雲の手前に重ねる
    [SerializeField] private ComputeShader shader;
    [SerializeField] private float viewerHeight = 2f; // 観測者の高さ。すべての面の高さより大きくする
    [SerializeField] private float flowReferenceDistance = 0.25f; // 観測者からの距離がこの値の面で、流れの速さが速度場そのままになる
    [SerializeField] private SolarBodies solarBodies; // 星の位置と明るさを光源として受け取る。未割り当てなら、デバッグ用の太陽だけ
    [SerializeField] private int angleCount = 360;
    [SerializeField] private int radiusCount = 100;
    [SerializeField] private float minN = 0.02f;
    [SerializeField] private float minDist = 0.02f;
    [SerializeField] private float shadowFill = 0.05f;
    [SerializeField] private float bowlDepth = 0.3f; // お椀で、光源から最も遠い点が上がる量
    [SerializeField] private float hDiffMin = 0.001f; // 光源の高さ - 面の高さ の下限
    [FormerlySerializedAs("cloudCellSize")]
    [SerializeField] private float heightCellSize = 0.05f; // 高さのノイズの、画面の高さあたりのセル数
    [SerializeField] private float densityCellSize = 3f; // 密度のノイズの、一番粗いオクターブの画面の高さあたりのセル数（整数に丸める）
    [SerializeField, Range(1, 8)] private int densityOctaves = 4; // 密度のノイズを重ねる層の数
    [SerializeField] private float densityWarp = 0.3f; // 密度のノイズの位置を歪める量（uv）
    [SerializeField, Min(1f)] private float densityRidge = 1.5f; // 尾根の鋭さ。大きいほど細い筋になる

    [SerializeField] private float minHeight = 0.42f; // 雲の高さの最小値
    [SerializeField] private float maxHeight = 0.58f; // 雲の高さの最大値
    [SerializeField] private float minDensity = 0.1f; // 雲の密度の最小値
    [SerializeField] private float maxDensity = 0.9f; // 雲の密度の最大値
    [SerializeField, Min(1f)] private float densityContrast = 2f; // 密度を読むときのコントラスト。1 でそのまま、大きいほど筋が際立つ
    [SerializeField] private float starHeight;

    [Header("Nebula")]
    [SerializeField] private int perlinSeed; // 最初の雲のシード。高さは perlinSeed、密度は perlinSeed + DensitySeedOffset から、切り替えのたびに SeedStride ずつ進む

    [Header("Cloud Seed Transition")]
    [SerializeField] private bool seedTransitionEnabled = true; // 一定時間ごとに雲（高さと密度）を別のシードのものへ寄せる
    [SerializeField, Min(0f)] private float seedInterval = 180f; // 寄せ終わってから、次に寄せ始めるまでの秒数
    [SerializeField, Min(0.1f)] private float seedBlendDuration = 60f; // 新しい雲へ寄せきるまでの秒数

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
    
    [Header("移流のぼやけの抑制（アンシャープマスク）")]
    [SerializeField, Min(0f)] private float cloudUnsharp = 0.3f; // 雲の密度。流れが unsharpFlowReference のときの強さ。0 で何もしない
    [SerializeField, Min(0f)] private float veilUnsharp = 0.3f;  // 手前の塵の密度。同上
    [SerializeField, Min(0.001f)] private float unsharpFlowReference = 0.5f; // 1フレームの移動量（ピクセル）がこの値のとき、強さが上の値になる。流れがなければ強さは 0
    [SerializeField, Min(0f)] private float unsharpMaxScale = 4f; // 流れが速い所の、強さの倍率の上限

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
    [SerializeField, Range(0f, 1f)] private float fishVeilCompensation = 0.8f; // 魚の色に掛ける Veil 吸収の逆補正の強さ。1 で厳密な逆、0 で補正なし（暖色になる）
    [SerializeField, Min(1f)] private float fishVeilCompensationMax = 8f; // 逆補正の倍率の上限。濃い塵で値が爆発するのを防ぐ

    [Header("Veil Seed Transition")]
    [SerializeField] private bool veilSeedTransitionEnabled = true; // 一定時間ごとに塵の密度を別のシードのものへ寄せる（雲とは別のタイマー）
    [SerializeField, Min(0f)] private float veilSeedInterval = 240f; // 寄せ終わってから、次に寄せ始めるまでの秒数
    [SerializeField, Min(0.1f)] private float veilSeedBlendDuration = 90f; // 新しい塵へ寄せきるまでの秒数

    [Header("Particle")]
    [SerializeField, Min(0f)] private float particleScale = 1f; // パーティクルの明るさの倍率（魚の後ろ、雲に加算）

    [Header("Starfield")]
    [SerializeField] private int starFieldCount; // 0 なら星空は描かない
    
    [Header("Debug")]
    [SerializeField] private float sunEuv;
    [SerializeField] private float sunOptical;
    [SerializeField] private DebugMode debugMode = DebugMode.Final;
    [SerializeField] private Vector2 debugRange = new Vector2(0f, 1f); // 0〜1 に伸ばす範囲（Flux は log10 の値）
    
    // Stars
    private readonly System.Collections.Generic.List<SolarLight> _solarLights = new System.Collections.Generic.List<SolarLight>(MaxStars);
    private StarData[] _starData;
    private int _starCount = 1;

    // Kernel
    private Kernel2D _writeGradients;
    private Kernel2D _accumulatePerlin;
    private Kernel2D _remapCloud;
    private Kernel2D _initializeCloud;
    private Kernel2D _initializeVeil;
    private Kernel2D _blendCloud;
    private Kernel2D _blendVeil;
    private Kernel2D _advectCloud;
    private Kernel2D _advectVeil;
    private Kernel2D _sharpenCloud;
    private Kernel2D _sharpenVeil;
    private Kernel2D _sweepHorizon;
    private Kernel2D _illuminateCloud;
    private Kernel2D _illuminateVeil;
    private Kernel2D _bakeStarField;
    private Kernel2D _compose;
    private Kernel2D _debugView;
    
    // RT
    private PingPongRenderTexture _cloudPingPong; // R=奥行き, G=密度
    private PingPongRenderTexture _nextCloudPingPong; // 寄せ先の雲。_cloudPingPong と同じ形式で、寄せている間だけ移流する
    private RenderTexture _cloudBlend; // 寄せている間の、_cloudPingPong と _nextCloudPingPong を混ぜた雲。照明や合成はこれを読む
    private PingPongRenderTexture _veilPingPong; // R=密度
    private PingPongRenderTexture _nextVeilPingPong; // 寄せ先の塵。寄せている間だけ移流する
    private RenderTexture _veilBlend; // 寄せている間の、_veilPingPong と _nextVeilPingPong を混ぜた塵。照明や合成はこれを読む

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

    // シードの切り替え
    private SeedPhase _seedPhase = SeedPhase.Waiting;
    private float _seedTimer; // Waiting: 待った秒数 / Blending: 寄せた秒数
    private int _seedIndex; // 今の雲のシード番号（連番）
    private int _nextSeedIndex; // 寄せ先の雲のシード番号
    private Vector2 _cloudMean; // 今の雲の、高さと密度の平均（範囲を引き伸ばしたあと）
    private Vector2 _nextCloudMean;
    private CloudStats _nextStats; // 寄せ先の雲の、引き伸ばす前の統計
    private bool _nextStatsReady;

    // 塵（Veil）のシードの切り替え。雲とは別のタイマーで動く
    private SeedPhase _veilSeedPhase = SeedPhase.Waiting;
    private float _veilSeedTimer; // Waiting: 待った秒数 / Blending: 寄せた秒数
    private int _veilSeedIndex; // 今の塵のシード番号（連番）
    private int _nextVeilSeedIndex;
    private float _veilMean; // 今の塵の密度の平均
    private float _nextVeilMean;
    private bool _nextVeilMeanReady;

    public void Initialize(Vector2Int resolution)
    {
        _res = resolution;
        
        // Kernel
        _writeGradients = new Kernel2D(shader, "WriteGradients");
        _accumulatePerlin = new Kernel2D(shader, "AccumulatePerlin");
        _remapCloud = new Kernel2D(shader, "RemapCloud");
        _initializeCloud = new Kernel2D(shader, "InitializeCloud");
        _initializeVeil = new Kernel2D(shader, "InitializeVeil");
        _blendCloud = new Kernel2D(shader, "BlendCloud");
        _blendVeil = new Kernel2D(shader, "BlendVeil");
        _advectCloud = new Kernel2D(shader, "AdvectCloud");
        _advectVeil = new Kernel2D(shader, "AdvectVeil");
        _sharpenCloud = new Kernel2D(shader, "SharpenCloud");
        _sharpenVeil = new Kernel2D(shader, "SharpenVeil");
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
        debugRawImage.texture = _cloudPingPong.read;

        var nextCloudRead = new RenderTexture(_res.x, _res.y, 0, RenderTextureFormat.RGFloat){ enableRandomWrite = true };
        var nextCloudWrite = new RenderTexture(_res.x, _res.y, 0, RenderTextureFormat.RGFloat){ enableRandomWrite = true };
        nextCloudRead.Create();
        nextCloudWrite.Create();
        _nextCloudPingPong = new PingPongRenderTexture(nextCloudRead, nextCloudWrite);
        _cloudBlend = new RenderTexture(_res.x, _res.y, 0, RenderTextureFormat.RGFloat){ enableRandomWrite = true };
        _cloudBlend.Create();
        
        var veilRead = new RenderTexture(_res.x, _res.y, 0, RenderTextureFormat.RFloat){ enableRandomWrite = true };
        var veilWrite = new RenderTexture(_res.x, _res.y, 0, RenderTextureFormat.RFloat){ enableRandomWrite = true };
        veilRead.Create();
        veilWrite.Create();
        _veilPingPong = new PingPongRenderTexture(veilRead, veilWrite);

        var nextVeilRead = new RenderTexture(_res.x, _res.y, 0, RenderTextureFormat.RFloat){ enableRandomWrite = true };
        var nextVeilWrite = new RenderTexture(_res.x, _res.y, 0, RenderTextureFormat.RFloat){ enableRandomWrite = true };
        nextVeilRead.Create();
        nextVeilWrite.Create();
        _nextVeilPingPong = new PingPongRenderTexture(nextVeilRead, nextVeilWrite);
        _veilBlend = new RenderTexture(_res.x, _res.y, 0, RenderTextureFormat.RFloat){ enableRandomWrite = true };
        _veilBlend.Create();

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
            fishCamera.allowHDR = true; // 魚の発光は 1 を超える。HDR でないと 1 に潰れる
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
        _starData = initial;
        _starCount = 1;
        
        // ノイズ作成
        _seedIndex = 0;
        _cloudMean = GenerateCloudBlocking(_cloudPingPong, _seedIndex);
        _veilSeedIndex = 0;
        InitializeVeil(_veilPingPong.read, VeilSeed(_veilSeedIndex));
        var veilRequest = AsyncGPUReadback.Request(_veilPingPong.read);
        veilRequest.WaitForCompletion();
        _veilMean = ComputeMean(veilRequest.GetData<float>());
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

    // 連番のシード番号から、実際のシードを作る。番号 0 は perlinSeed そのまま
    private int HeightSeed(int index) { return perlinSeed + index * SeedStride; }
    private int DensitySeed(int index) { return perlinSeed + DensitySeedOffset + index * SeedStride; } // 高さと同じ勾配にならないよう、シードをずらす

    /// <summary>
    /// 雲を target.read に作る。高さ（R）は、土台（Mathf.PerlinNoise）+ 勾配ノイズ、
    /// 密度（G）は、高さとは独立した、ワープした fBm の尾根のノイズ。どちらも範囲は引き伸ばす前（RemapCloud が引き伸ばす）
    /// </summary>
    private void BuildRawCloud(PingPongRenderTexture target, int seedIndex)
    {
        BakeBaseHeight(target.read, heightCellSize, seedIndex * HeightBaseOffsetStep);
        Perlin(target, HeightSeed(seedIndex), 100f, 1f);
        InitializeCloud(target, DensitySeed(seedIndex));
    }

    /// <summary>
    /// 雲を作って範囲を引き伸ばす。GPU の読み戻しを待つので、初期化のときだけ使う。引き伸ばしたあとの、高さと密度の平均を返す
    /// </summary>
    private Vector2 GenerateCloudBlocking(PingPongRenderTexture target, int seedIndex)
    {
        BuildRawCloud(target, seedIndex);
        var request = AsyncGPUReadback.Request(target.read);
        request.WaitForCompletion();
        return RemapCloud(target, ComputeCloudStats(request.GetData<Vector2>()));
    }

    /// <summary>
    /// 雲の密度（cloud の G）に、高さとは独立した、ワープした fBm の尾根のノイズを書く。高さ（R）はそのまま
    /// </summary>
    private void InitializeCloud(PingPongRenderTexture target, int densitySeed)
    {
        shader.SetInts("_Resolution", _res.x, _res.y);
        shader.SetFloat("_Aspect", (float)_res.x / _res.y);
        shader.SetInt("_DensityCellsY", Mathf.RoundToInt(densityCellSize));
        shader.SetInt("_DensityOctaves", densityOctaves);
        shader.SetFloat("_DensityWarp", densityWarp);
        shader.SetFloat("_DensityRidge", densityRidge);
        shader.SetInt("_DensitySeed", densitySeed);

        _initializeCloud.SetTexture("_CloudRead", target.read);
        _initializeCloud.SetTexture("_CloudWrite", target.write);
        _initializeCloud.Dispatch(_res);
        target.Swap();
    }

    // 高さと密度の、最小・最大・平均
    private static CloudStats ComputeCloudStats(NativeArray<Vector2> clouds)
    {
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        double sumX = 0;
        double sumY = 0;
        foreach (var cloud in clouds)
        {
            min = Vector2.Min(min, cloud);
            max = Vector2.Max(max, cloud);
            sumX += cloud.x;
            sumY += cloud.y;
        }
        double count = Mathf.Max(clouds.Length, 1);
        return new CloudStats { Min = min, Max = max, Mean = new Vector2((float)(sumX / count), (float)(sumY / count)) };
    }

    /// <summary>
    /// 雲の高さ（cloud の R）を minHeight〜maxHeight に、密度（G）を minDensity〜maxDensity に、
    /// それぞれ実際の最小値・最大値（stats）を使って引き伸ばす。引き伸ばしたあとの、高さと密度の平均を返す
    /// </summary>
    private Vector2 RemapCloud(PingPongRenderTexture target, CloudStats stats)
    {
        shader.SetInts("_Resolution", _res.x, _res.y);
        shader.SetFloat("_HeightSrcMin", stats.Min.x);
        shader.SetFloat("_HeightSrcMax", stats.Max.x);
        shader.SetFloat("_HeightMin", minHeight);
        shader.SetFloat("_HeightMax", maxHeight);
        shader.SetFloat("_DensitySrcMin", stats.Min.y);
        shader.SetFloat("_DensitySrcMax", stats.Max.y);
        shader.SetFloat("_DensityMin", minDensity);
        shader.SetFloat("_DensityMax", maxDensity);

        _remapCloud.SetTexture("_CloudRead", target.read);
        _remapCloud.SetTexture("_CloudWrite", target.write);
        _remapCloud.Dispatch(_res);
        target.Swap();

        // 引き伸ばしは一次式なので、平均も同じ式で変換できる
        return new Vector2(
            Mathf.Lerp(minHeight, maxHeight, Mathf.InverseLerp(stats.Min.x, stats.Max.x, stats.Mean.x)),
            Mathf.Lerp(minDensity, maxDensity, Mathf.InverseLerp(stats.Min.y, stats.Max.y, stats.Mean.y)));
    }

    /// <summary>
    /// 切り替えのタイマーを進める。Waiting が seedInterval 経ったら新しい雲を作り（Generating）、
    /// 範囲の読み戻しが終わったら引き伸ばして寄せ始め（Blending）、seedBlendDuration 経ったら新しい雲を今の雲にする
    /// </summary>
    private void UpdateSeedTransition()
    {
        switch (_seedPhase)
        {
            case SeedPhase.Waiting:
                if (!seedTransitionEnabled) { return; }
                _seedTimer += Time.deltaTime;
                if (_seedTimer >= seedInterval) { BeginSeedGeneration(); }
                break;

            case SeedPhase.Generating:
                if (!_nextStatsReady) { return; }
                _nextStatsReady = false;
                _nextCloudMean = RemapCloud(_nextCloudPingPong, _nextStats);
                _seedPhase = SeedPhase.Blending;
                _seedTimer = 0f;
                break;

            case SeedPhase.Blending:
                _seedTimer += Time.deltaTime;
                if (_seedTimer >= seedBlendDuration) { FinishSeedBlend(); }
                break;
        }
    }

    [ContextMenu("Start Seed Transition Now")]
    private void StartSeedTransitionNow()
    {
        if (_nextCloudPingPong != null && _seedPhase == SeedPhase.Waiting) { BeginSeedGeneration(); }
    }

    /// <summary>
    /// 次のシードの雲を作って、範囲を求めるために非同期で読み戻す。読み戻しの完了は Generating のあいだ待つ（フレームは止めない）
    /// </summary>
    private void BeginSeedGeneration()
    {
        _nextSeedIndex = _seedIndex + 1;
        _nextStatsReady = false;
        BuildRawCloud(_nextCloudPingPong, _nextSeedIndex);
        _seedPhase = SeedPhase.Generating;
        AsyncGPUReadback.Request(_nextCloudPingPong.read, 0, OnNextCloudReadback);
    }

    private void OnNextCloudReadback(AsyncGPUReadbackRequest request)
    {
        if (_seedPhase != SeedPhase.Generating) { return; }
        if (request.hasError)
        {
            Debug.LogWarning("Nebula: 新しい雲の読み戻しに失敗したので、この切り替えは見送る");
            _seedPhase = SeedPhase.Waiting;
            _seedTimer = 0f;
            return;
        }
        _nextStats = ComputeCloudStats(request.GetData<Vector2>());
        _nextStatsReady = true;
    }

    /// <summary>
    /// 寄せ終わり。寄せ先の雲を今の雲にする（このフレームから、混ぜずにそのまま読む）
    /// </summary>
    private void FinishSeedBlend()
    {
        (_cloudPingPong, _nextCloudPingPong) = (_nextCloudPingPong, _cloudPingPong);
        _cloudMean = _nextCloudMean;
        _seedIndex = _nextSeedIndex;
        _seedPhase = SeedPhase.Waiting;
        _seedTimer = 0f;
    }

    /// <summary>
    /// 今の雲と寄せ先の雲を混ぜて _cloudBlend に書く。
    /// 重みを cos と sin（二乗の和が 1）にして、独立な 2 つのノイズを混ぜてもコントラストが落ちないようにする。
    /// 平均は、それぞれの平均を二乗の重みで補間する。進み方は smoothstep で、始まりと終わりをなだらかにする
    /// </summary>
    private void BlendCloudNow()
    {
        float t = Mathf.Clamp01(_seedTimer / Mathf.Max(seedBlendDuration, 0.001f));
        float angle = t * t * (3f - 2f * t) * (Mathf.PI * 0.5f);

        shader.SetVector("_BlendWeights", new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)));
        shader.SetVector("_BlendMeanA", _cloudMean);
        shader.SetVector("_BlendMeanB", _nextCloudMean);
        _blendCloud.SetTexture("_CloudRead", _cloudPingPong.read);
        _blendCloud.SetTexture("_CloudNextRead", _nextCloudPingPong.read);
        _blendCloud.SetTexture("_CloudBlendWrite", _cloudBlend);
        _blendCloud.Dispatch(_res);
    }

    // 連番のシード番号から、実際のシードを作る。番号 0 は veilSeed そのまま
    private int VeilSeed(int index) { return (int)veilSeed + index * SeedStride; }

    /// <summary>
    /// 手前の塵の密度（veil）をノイズで作って target に書く
    /// </summary>
    private void InitializeVeil(RenderTexture target, int seed)
    {
        shader.SetInts("_Resolution", _res.x, _res.y);
        shader.SetFloat("_Aspect", (float)_res.x / _res.y);
        SetVeilUniforms();
        shader.SetInt("_VeilSeed", seed); // SetVeilUniforms が入れた veilSeed を、この塵のシードで上書きする

        _initializeVeil.SetTexture("_VeilWrite", target);
        _initializeVeil.Dispatch(_res);
    }

    private static float ComputeMean(NativeArray<float> values)
    {
        double sum = 0;
        foreach (var value in values) { sum += value; }
        return (float)(sum / Mathf.Max(values.Length, 1));
    }

    /// <summary>
    /// 塵の切り替えのタイマーを進める。流れは UpdateSeedTransition（雲）と同じで、秒数だけ雲とは別。
    /// 塵は引き伸ばしがないので、読み戻すのは平均だけ
    /// </summary>
    private void UpdateVeilSeedTransition()
    {
        switch (_veilSeedPhase)
        {
            case SeedPhase.Waiting:
                if (!veilSeedTransitionEnabled) { return; }
                _veilSeedTimer += Time.deltaTime;
                if (_veilSeedTimer >= veilSeedInterval) { BeginVeilSeedGeneration(); }
                break;

            case SeedPhase.Generating:
                if (!_nextVeilMeanReady) { return; }
                _nextVeilMeanReady = false;
                _veilSeedPhase = SeedPhase.Blending;
                _veilSeedTimer = 0f;
                break;

            case SeedPhase.Blending:
                _veilSeedTimer += Time.deltaTime;
                if (_veilSeedTimer >= veilSeedBlendDuration) { FinishVeilSeedBlend(); }
                break;
        }
    }

    [ContextMenu("Start Veil Seed Transition Now")]
    private void StartVeilSeedTransitionNow()
    {
        if (_nextVeilPingPong != null && _veilSeedPhase == SeedPhase.Waiting) { BeginVeilSeedGeneration(); }
    }

    private void BeginVeilSeedGeneration()
    {
        _nextVeilSeedIndex = _veilSeedIndex + 1;
        _nextVeilMeanReady = false;
        InitializeVeil(_nextVeilPingPong.read, VeilSeed(_nextVeilSeedIndex));
        _veilSeedPhase = SeedPhase.Generating;
        AsyncGPUReadback.Request(_nextVeilPingPong.read, 0, OnNextVeilReadback);
    }

    private void OnNextVeilReadback(AsyncGPUReadbackRequest request)
    {
        if (_veilSeedPhase != SeedPhase.Generating) { return; }
        if (request.hasError)
        {
            Debug.LogWarning("Nebula: 新しい塵の読み戻しに失敗したので、この切り替えは見送る");
            _veilSeedPhase = SeedPhase.Waiting;
            _veilSeedTimer = 0f;
            return;
        }
        _nextVeilMean = ComputeMean(request.GetData<float>());
        _nextVeilMeanReady = true;
    }

    private void FinishVeilSeedBlend()
    {
        (_veilPingPong, _nextVeilPingPong) = (_nextVeilPingPong, _veilPingPong);
        _veilMean = _nextVeilMean;
        _veilSeedIndex = _nextVeilSeedIndex;
        _veilSeedPhase = SeedPhase.Waiting;
        _veilSeedTimer = 0f;
    }

    /// <summary>
    /// 今の塵と寄せ先の塵を混ぜて _veilBlend に書く。重みの考え方は BlendCloudNow と同じ
    /// </summary>
    private void BlendVeilNow()
    {
        float t = Mathf.Clamp01(_veilSeedTimer / Mathf.Max(veilSeedBlendDuration, 0.001f));
        float angle = t * t * (3f - 2f * t) * (Mathf.PI * 0.5f);

        shader.SetVector("_VeilBlendWeights", new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)));
        shader.SetVector("_VeilBlendMean", new Vector2(_veilMean, _nextVeilMean));
        _blendVeil.SetTexture("_VeilRead", _veilPingPong.read);
        _blendVeil.SetTexture("_VeilNextRead", _nextVeilPingPong.read);
        _blendVeil.SetTexture("_VeilBlendWrite", _veilBlend);
        _blendVeil.Dispatch(_res);
    }

    /// <summary>
    /// 塵を速度場で移流し、アンシャープマスクでぼやけを戻す。read を読んで write に書き、入れ替える
    /// </summary>
    private void AdvectVeilLayer(PingPongRenderTexture veil, RenderTexture velocityField)
    {
        _advectVeil.SetTexture("_VelocityRead", velocityField);
        _advectVeil.SetTexture("_VeilRead", veil.read);
        _advectVeil.SetTexture("_VeilWrite", veil.write);
        _advectVeil.Dispatch(_res);
        veil.Swap();
        SharpenAdvected(_sharpenVeil, veil, velocityField, "_VeilRead", "_VeilWrite", "_VeilUnsharp", veilUnsharp);
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
        shader.SetFloat("_FishVeilCompensation", fishVeilCompensation);
        shader.SetFloat("_FishVeilCompensationMax", fishVeilCompensationMax);
    }

    private void Perlin(PingPongRenderTexture target, int seed, float size, float weight)
    {
        Vector2Int res = Vector2Int.CeilToInt((Vector2)_res / size);

        shader.SetInts("_Resolution", _res.x, _res.y);
        shader.SetInts("_PerlinResolution", res.x, res.y);
        shader.SetInt("_PerlinSeed", seed);
        shader.SetFloat("_PerlinWeight", weight);

        _writeGradients.SetTexture("_GradientWrite", _gradient);
        _writeGradients.Dispatch(res);

        _accumulatePerlin.SetTexture("_GradientRead", _gradient);
        _accumulatePerlin.SetTexture("_CloudRead", target.read);
        _accumulatePerlin.SetTexture("_CloudWrite", target.write);
        _accumulatePerlin.Dispatch(_res);

        target.Swap();
    }
    
    /// <summary>
    /// 光源を更新する。0番はデバッグ用の太陽で常に残し、1番以降に SolarBodies の星を並べる。
    /// MaxStars を超える分は捨てる（SolarBodies のプールは MaxStars - 1 以下のはず）
    /// </summary>
    private void UpdateStars()
    {
        _starData[0] = new StarData
        {
            Position = new Vector3(0.5f, 0.5f, starHeight),
            Euv = sunEuv,
            Optical = sunOptical
        };
        int count = 1;
        if (solarBodies != null)
        {
            solarBodies.GetLights(_solarLights);
            foreach (var light in _solarLights)
            {
                if (count >= MaxStars) { break; }
                _starData[count++] = new StarData
                {
                    Position = new Vector3(light.PositionUv.x, light.PositionUv.y, starHeight),
                    Euv = light.Euv,
                    Optical = light.Optical
                };
            }
        }
        _starCount = count;
        _lightsBuffer.SetData(_starData, 0, 0, count);
    }

    public RenderTexture Tick(RenderTexture velocityField, RenderTexture particleTexture)
    {
        UpdateSeedTransition();
        UpdateVeilSeedTransition();

        shader.SetFloat("_ViewerHeight", viewerHeight);
        shader.SetFloat("_FlowReferenceDistance", flowReferenceDistance);
        shader.SetInts("_Resolution", _res.x, _res.y);
        shader.SetFloat("_DeltaTime", Time.deltaTime);

        UpdateStars();
        float aspect = (float)_res.x / _res.y;
        float maxRadius = Mathf.Sqrt((aspect / 2f) * (aspect / 2f) + 0.5f * 0.5f);
        shader.SetFloat("_Aspect", aspect);
        shader.SetInt("_AngleCount", angleCount);
        shader.SetFloat("_AngleStep", 2f * Mathf.PI / angleCount);
        shader.SetInt("_RadiusCount", radiusCount);
        shader.SetFloat("_MaxRadius", maxRadius);
        shader.SetFloat("_RadiusStep", maxRadius / radiusCount);
        shader.SetInt("_StarCount", _starCount);
        shader.SetFloat("_MinN", minN);
        shader.SetFloat("_DensityContrast", densityContrast);
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
        shader.SetFloat("_ParticleScale", particleScale);
        SetVeilUniforms();
        shader.SetInt("_DebugMode", (int)debugMode);
        shader.SetVector("_DebugRange", debugRange);

        AdvectCloudLayer(_cloudPingPong, velocityField);
        // 寄せている間だけ、寄せ先の雲も同じ流れに乗せる（乗せないと、画面に固定された模様になる）
        bool blending = _seedPhase == SeedPhase.Blending;
        if (blending) { AdvectCloudLayer(_nextCloudPingPong, velocityField); }

        AdvectVeilLayer(_veilPingPong, velocityField);
        bool veilBlending = _veilSeedPhase == SeedPhase.Blending;
        if (veilBlending) { AdvectVeilLayer(_nextVeilPingPong, velocityField); }

        // これ以降は、寄せている間は混ぜた雲・塵を、そうでなければ今のものをそのまま読む
        RenderTexture cloud = _cloudPingPong.read;
        if (blending)
        {
            BlendCloudNow();
            cloud = _cloudBlend;
        }
        RenderTexture veil = _veilPingPong.read;
        if (veilBlending)
        {
            BlendVeilNow();
            veil = _veilBlend;
        }

        _sweepHorizon.SetTexture("_CloudRead", cloud);
        _sweepHorizon.SetTexture("_ShadowMapWrite", _shadowMap);
        _sweepHorizon.SetBuffer("_StarRead", _lightsBuffer);
        _sweepHorizon.Dispatch(new Vector2Int(angleCount, _starCount));

        _illuminateCloud.SetTexture("_CloudRead", cloud);
        _illuminateCloud.SetTexture("_ShadowMapRead", _shadowMap);
        _illuminateCloud.SetBuffer("_StarRead", _lightsBuffer);
        _illuminateCloud.SetTexture("_FluxWrite", _flux);
        _illuminateCloud.SetTexture("_ReflectionWrite", _reflection);
        _illuminateCloud.Dispatch(_res);
        _flux.GenerateMips(); // 霧のぼかしに使う粗いミップレベルを作る

        _compose.SetTexture("_CloudRead", cloud);
        _compose.SetTexture("_FluxRead", _flux);
        _illuminateVeil.SetTexture("_VeilRead", veil);
        _illuminateVeil.SetBuffer("_StarRead", _lightsBuffer);
        _illuminateVeil.SetTexture("_VeilLightWrite", _veilLight);
        _illuminateVeil.Dispatch(_res);

        _compose.SetTexture("_ReflectionRead", _reflection);
        _compose.SetTexture("_VeilRead", veil);
        _compose.SetTexture("_FishRead", _fish);
        _compose.SetTexture("_ParticleRead", particleTexture);
        _compose.SetTexture("_StarFieldRead", _starField);
        _compose.SetTexture("_VeilLightRead", _veilLight);
        _compose.SetTexture("_NebulaWrite", _nebula);
        _compose.Dispatch(_res);

        // Final 以外のときは、Compose の結果を DebugView の表示で上書きする
        if (debugMode != DebugMode.Final)
        {
            _debugView.SetTexture("_CloudRead", cloud);
            _debugView.SetTexture("_FluxRead", _flux);
            _debugView.SetTexture("_ReflectionRead", _reflection);
            _debugView.SetTexture("_VeilRead", veil);
            _debugView.SetTexture("_VeilLightRead", _veilLight);
            _debugView.SetTexture("_ShadowMapRead", _shadowMap);
            _debugView.SetBuffer("_StarRead", _lightsBuffer);
            _debugView.SetTexture("_NebulaWrite", _nebula);
            _debugView.Dispatch(_res);
        }

        debugRawImage.texture = _nebula;
        //Graphics.Blit(_veil.read, _nebula.read, addVeilMat);
        return cloud;
    }

    /// <summary>
    /// 雲を速度場で移流し、アンシャープマスクでぼやけを戻す。read を読んで write に書き、入れ替える
    /// </summary>
    private void AdvectCloudLayer(PingPongRenderTexture cloud, RenderTexture velocityField)
    {
        _advectCloud.SetTexture("_VelocityRead", velocityField);
        _advectCloud.SetTexture("_CloudRead", cloud.read);
        _advectCloud.SetTexture("_CloudWrite", cloud.write);
        _advectCloud.Dispatch(_res);
        cloud.Swap();
        SharpenAdvected(_sharpenCloud, cloud, velocityField, "_CloudRead", "_CloudWrite", "_CloudUnsharp", cloudUnsharp);
    }
    
    /// <summary>
    /// 高さ（R）の土台に Mathf.PerlinNoise を書く。全画素を CPU で計算すると 1 回で数十〜百 ms 止まるので、
    /// 格子 1 つあたり HeightBaseTexelsPerCell テクセルの低解像度で焼いて、バイリニアで拡大する（土台は低周波なので見た目はほぼ同じ）。
    /// offset は読む位置のずらし（格子単位）。シードを変えるのに使う
    /// </summary>
    private static void BakeBaseHeight(RenderTexture target, float cellsY, float offset)
    {
        int h = Mathf.Clamp(Mathf.CeilToInt(cellsY * HeightBaseTexelsPerCell), 2, target.height);
        int w = Mathf.Clamp(Mathf.RoundToInt(h * (float)target.width / target.height), 2, target.width);
        float[] data = new float[w * h * 2];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float px = (x + 0.5f) / h * cellsY + offset;
                float py = (y + 0.5f) / h * cellsY + offset;
                int i = (y * w + x) * 2;
                data[i]     = Mathf.PerlinNoise(px, py);
                data[i + 1] = 0f; // 密度（G）は InitializeCloud が GPU で書く
            }
        }

        var texture = new Texture2D(w, h, TextureFormat.RGFloat, false, true);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.SetPixelData(data, 0);
        texture.Apply(false);

        Graphics.Blit(texture, target);
        Object.Destroy(texture);
    }

    /// <summary>
    /// 移流のあとに、アンシャープマスクでぼやけを戻す。強さは、流れが速い所ほど大きい。read を読んで write に書き、入れ替える。強さが 0 なら何もしない
    /// </summary>
    private void SharpenAdvected(Kernel2D kernel, PingPongRenderTexture target, RenderTexture velocityField, string readName, string writeName, string strengthName, float strength)
    {
        if (strength <= 0f) return;

        shader.SetFloat(strengthName, strength);
        shader.SetFloat("_UnsharpFlowReference", unsharpFlowReference);
        shader.SetFloat("_UnsharpMaxScale", unsharpMaxScale);
        kernel.SetTexture("_VelocityRead", velocityField);
        kernel.SetTexture(readName, target.read);
        kernel.SetTexture(writeName, target.write);
        kernel.Dispatch(_res);
        target.Swap();
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
        _nextCloudPingPong.Dispose();
        if (_cloudBlend != null) _cloudBlend.Release();
        _cloudBlend = null;
        _veilPingPong.Dispose();
        _nextVeilPingPong.Dispose();
        if (_veilBlend != null) _veilBlend.Release();
        _veilBlend = null;
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
