using System.Collections.Generic;
using UnityEngine;

/// <summary>Nebula に渡す、星1つ分の光源。位置は画面のUV(原点は左下)</summary>
public struct SolarLight
{
    public Vector2 PositionUv;
    public float Euv;
    public float Optical;
}

/// <summary>
/// 触れた点に星を生成し、手に追従させ、離れたら慣性で飛ばして画面中央へ引き寄せる。
/// 星は固定数をプールして使い回す（増え続けない）。
/// 内部の座標は「画面中央が原点・縦が1」のV座標。パラメーターの長さは、すべてこの単位
/// </summary>
public class SolarBodies : MonoBehaviour
{
    private const float MaxDeltaTime = 0.1f;
    private const float MaxStep = 1f / 120f; // 引力の積分の刻み。周回しても軌道が崩れないようにする

    /// <summary>プールの上限。Nebula の光源は MaxStars(10) 個で、そのうち0番はデバッグ用の太陽なので、星は9個まで</summary>
    public const int MaxPoolSize = 9;

    [SerializeField] private SolarBody bodyPrefab;
    [SerializeField] private SensorInputSource inputSource;
    [SerializeField, Range(1, MaxPoolSize)] private int poolSize = MaxPoolSize;

    [Header("表示")]
    [SerializeField] private Camera displayCamera; // 星を描くカメラ（Orthographic）。未割り当てなら Main Camera を使う
    [SerializeField] private float worldZ;

    [Header("質量（生成のたびに星ごとにランダムに決める。t = 質量を min〜max で 0〜1 にした値）")]
    [SerializeField, Min(0.1f)] private float minMass = 1.5f;
    [SerializeField, Min(0.1f)] private float maxMass = 6f;   // 大きいほど重い。引力・抵抗による速度の変化と、手への追従が遅くなる
    [SerializeField, Min(1f)] private float massBias = 2f;    // t = u^massBias（uは0〜1の一様乱数）。大きいほど軽い星が多い。1で一様

    [Header("色温度（t → 曲線 → minTemperature〜maxTemperature）")]
    [SerializeField, Min(1000f)] private float minTemperature = 1500f;
    [SerializeField, Min(1000f)] private float maxTemperature = 30000f;
    [SerializeField] private AnimationCurve temperatureCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("Nebula への光（Euv, Optical = 最大値 × 曲線(t) × Scale/maxScale × フェードイン）")]
    [SerializeField, Min(0f)] private float maxEuv = 1f;
    [SerializeField, Min(0f)] private float maxOptical = 3f;
    [SerializeField] private AnimationCurve euvCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 0f), new Keyframe(0.5f, 0.125f, 0.75f, 0.75f), new Keyframe(1f, 1f, 3f, 3f));        // t^3
    [SerializeField] private AnimationCurve opticalCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 0f), new Keyframe(0.5f, 0.25f, 1f, 1f), new Keyframe(1f, 1f, 2f, 2f));               // t^2
    [SerializeField, Min(0f)] private float lightFadeInTime = 0.3f; // 生成から、光が最大になるまでの時間[秒]

    [Header("重さ")]
    [SerializeField, Range(0f, 1f)] private float scaleMotionInfluence = 1f; // 1で、移動量がScaleに比例する（小さい星ほど、画面上の動きが狭い）。0で影響なし

    [Header("追従")]
    [SerializeField, Min(0.1f)] private float followStrength = 20f; // 大きいほど、手の位置へ強く追う。追いつくまでの時間は 星の質量 / followStrength [秒] 程度

    [Header("引力（力 = gravity / max(r, softening)^exponent、加速度 = 力 / mass）")]
    [SerializeField] private float gravity = 0.3f;
    [SerializeField] private float exponent = 1f;             // 0:一定 1:距離に反比例 2:逆2乗（周回しやすい）
    [SerializeField] private float softening = 0.05f;         // 中央付近で引力が発散しないための下限距離
    [SerializeField] private float drag = 0.15f;              // 速度に比例する抵抗（力）。0だと周回し続ける
    [SerializeField] private float maxSpeed = 3f;             // 1秒あたり、画面の高さの何倍まで

    [Header("遠近法")]
    [SerializeField] private float maxScale = 1f;
    [SerializeField] private float minScale = 0.05f;
    [SerializeField] private float farRadiusV = 0.5f;         // この距離以上で maxScale
    [SerializeField] private float vanishRadiusV = 0.03f;     // この距離未満になった慣性中の星は、消えたとみなす
    [SerializeField] private float scaleExponent = 1f;
    [SerializeField] private float scaleSmoothing = 8f;       // 追従↔慣性で Scale が跳ばないための追従の速さ

    [Header("回収")]
    [SerializeField] private float despawnMarginV = 0.5f;     // 表示領域の外へ、これを超えたら回収
    [SerializeField] private float nearCenterRadiusV = 0.1f;  // プール満杯時、この距離より中央に近い慣性中の星は取り上げ候補

    private readonly Stack<SolarBody> _free = new Stack<SolarBody>();
    private readonly List<SolarBody> _active = new List<SolarBody>();
    private readonly Dictionary<int, SolarBody> _byId = new Dictionary<int, SolarBody>();

    private float _aspect;
    private Vector2 _worldCenter;
    private float _worldHeight;
    private long _spawnSerial;
    private bool _ready;

    private void OnValidate()
    {
        farRadiusV = Mathf.Max(farRadiusV, vanishRadiusV + 0.001f);
        softening = Mathf.Max(softening, 1e-4f);
        maxMass = Mathf.Max(maxMass, minMass + 0.001f);
        maxTemperature = Mathf.Max(maxTemperature, minTemperature);
        poolSize = Mathf.Min(poolSize, MaxPoolSize);
    }

    public void Initialize(Vector2Int resolution)
    {
        _aspect = (float)resolution.x / resolution.y;

        if (bodyPrefab == null) { Debug.LogError("[SolarBodies] bodyPrefab を割り当ててください"); return; }
        if (inputSource == null) { Debug.LogError("[SolarBodies] inputSource を割り当ててください"); return; }
        if (!ResolveDisplayCamera()) { return; }
        UpdateDisplayArea();

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Destroy(transform.GetChild(i).gameObject);
        }
        _free.Clear();
        _active.Clear();
        _byId.Clear();
        if (poolSize > MaxPoolSize)
        {
            Debug.LogWarning($"[SolarBodies] poolSize は Nebula の光源の数の都合で {MaxPoolSize} までです");
            poolSize = MaxPoolSize;
        }
        for (int i = 0; i < poolSize; i++)
        {
            var body = Instantiate(bodyPrefab, transform);
            body.gameObject.SetActive(false);
            _free.Push(body);
        }
        _ready = true;
    }

    public void Tick()
    {
        if (!_ready) { return; }
        var dt = Mathf.Min(Time.deltaTime, MaxDeltaTime);
        if (dt <= 0f) { return; }
        UpdateDisplayArea();

        SpawnFromPoints();
        ApplyVelocityInputs();

        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var body = _active[i];
            if (body.IsFollowing) { Follow(body, dt); } else { Fly(body, dt); }

            var r = body.PositionV.magnitude;
            if (OutsideDistance(body.PositionV) > despawnMarginV || (!body.IsFollowing && r < vanishRadiusV))
            {
                Release(body);
                continue;
            }

            var targetScale = body.IsFollowing ? maxScale : ComputeScale(r);
            body.Scale = Mathf.Lerp(body.Scale, targetScale, 1f - Mathf.Exp(-scaleSmoothing * dt));
            ApplyTransform(body);
        }
    }

    /// <summary>触れた点ごとに星を1つ出す（またはプールが空なら取り上げる）。最初は慣性の状態で、同じIDのVelocityInputが来たら追従する</summary>
    private void SpawnFromPoints()
    {
        if (!inputSource.TryGetPointInput(out PointInputData[] points)) { return; }

        foreach (var point in points)
        {
            if (_byId.ContainsKey(point.Id)) { continue; }
            var body = Acquire();
            if (body == null) { continue; }

            body.Id = point.Id;
            body.SpawnSerial = _spawnSerial++;
            body.PositionV = UvToV(point.PositionUv);
            body.VelocityV = Vector2.zero;
            body.FollowTargetV = body.PositionV;
            body.IsFollowing = false;
            body.Scale = maxScale;
            body.MassT = Mathf.Pow(Random.value, massBias);
            body.Mass = Mathf.Lerp(minMass, maxMass, body.MassT);
            body.SpawnTime = Time.time;
            body.SetTemperature(Mathf.Lerp(minTemperature, maxTemperature, Mathf.Clamp01(temperatureCurve.Evaluate(body.MassT))));
            body.gameObject.SetActive(true);
            _active.Add(body);
            _byId[point.Id] = body;
            ApplyTransform(body);
        }
    }

    /// <summary>
    /// 動いている星を、Nebula に渡す光源として result に詰める（result は先にクリアする）。
    /// 光の強さは質量(曲線)で決まり、Scale が小さい星ほど、また生成直後ほど弱い
    /// </summary>
    public void GetLights(List<SolarLight> result)
    {
        result.Clear();
        foreach (var body in _active)
        {
            var size = maxScale > 0f ? Mathf.Clamp01(body.Scale / maxScale) : 0f;
            var fade = lightFadeInTime > 0f ? Mathf.Clamp01((Time.time - body.SpawnTime) / lightFadeInTime) : 1f;
            var gain = size * fade;
            result.Add(new SolarLight
            {
                PositionUv = VToUv(body.PositionV),
                Euv = maxEuv * Mathf.Max(0f, euvCurve.Evaluate(body.MassT)) * gain,
                Optical = maxOptical * Mathf.Max(0f, opticalCurve.Evaluate(body.MassT)) * gain
            });
        }
    }

    /// <summary>VelocityInput のあるIDの星だけ、追従の対象にする。VelocityInput の Velocity は使わない</summary>
    private void ApplyVelocityInputs()
    {
        foreach (var body in _active) { body.IsFollowing = false; }

        if (!inputSource.TryGetVelocityInput(out VelocityInputData[] inputs)) { return; }
        foreach (var input in inputs)
        {
            if (!_byId.TryGetValue(input.Id, out var body)) { continue; } // 星に結びつかない入力（Boidsなど）は無視
            body.IsFollowing = true;
            body.FollowTargetV = UvToV(input.PositionUv);
        }
    }

    /// <summary>
    /// 手の位置を目指して、なめらかに加減速しながら動く（最高速度は maxSpeed）。
    /// 速度を持ったまま動くので、追従↔慣性の切り替えで速度が跳ばない。手から離れたときの初速度は、この速度のまま
    /// </summary>
    private void Follow(SolarBody body, float dt)
    {
        var velocity = body.VelocityV;
        body.PositionV = Vector2.SmoothDamp(body.PositionV, body.FollowTargetV, ref velocity, body.Mass / followStrength, maxSpeed, dt);
        body.VelocityV = velocity;
    }

    /// <summary>
    /// 慣性で動きつつ、画面中央へ引き寄せられる。
    /// 重いほど速度が変わりにくく、Scale が小さいほど画面上の移動量が小さい（遠くにあるものほど、動きが狭く見える）
    /// </summary>
    private void Fly(SolarBody body, float dt)
    {
        var position = body.PositionV;
        var velocity = body.VelocityV;
        var motionScale = Mathf.Lerp(1f, body.Scale / maxScale, scaleMotionInfluence);
        var remaining = dt;
        while (remaining > 0f)
        {
            var h = Mathf.Min(remaining, MaxStep);
            remaining -= h;

            var r = position.magnitude;
            var force = -drag * velocity;
            if (r > 1e-5f)
            {
                force -= position / r * (gravity / Mathf.Pow(Mathf.Max(r, softening), exponent));
            }
            velocity = Vector2.ClampMagnitude(velocity + force / body.Mass * h, maxSpeed);
            position += velocity * (motionScale * h);
        }
        body.PositionV = position;
        body.VelocityV = velocity;
    }

    /// <summary>中央に近いほど小さく（擬似的な遠近法）</summary>
    private float ComputeScale(float r)
    {
        var t = Mathf.Clamp01((r - vanishRadiusV) / (farRadiusV - vanishRadiusV));
        return Mathf.Lerp(minScale, maxScale, Mathf.Pow(t, scaleExponent));
    }

    private SolarBody Acquire()
    {
        if (_free.Count == 0)
        {
            var victim = SelectVictim();
            if (victim == null) { return null; }
            Release(victim);
        }
        return _free.Pop();
    }

    /// <summary>
    /// プールが満杯のとき取り上げる星を選ぶ。
    /// 1. 表示領域の外（マージン内）の星のうち、最も古いもの
    /// 2. 画面中央にかなり近い慣性中の星のうち、最も中央に近いもの
    /// 3. 最も古いもの
    /// </summary>
    private SolarBody SelectVictim()
    {
        SolarBody outside = null, nearCenter = null, oldest = null;
        var nearCenterR = float.MaxValue;
        foreach (var body in _active)
        {
            if (oldest == null || body.SpawnSerial < oldest.SpawnSerial) { oldest = body; }

            if (OutsideDistance(body.PositionV) > 0f)
            {
                if (outside == null || body.SpawnSerial < outside.SpawnSerial) { outside = body; }
                continue;
            }

            var r = body.PositionV.magnitude;
            if (!body.IsFollowing && r < nearCenterRadiusV && r < nearCenterR)
            {
                nearCenter = body;
                nearCenterR = r;
            }
        }
        return outside ?? nearCenter ?? oldest;
    }

    private void Release(SolarBody body)
    {
        _byId.Remove(body.Id);
        _active.Remove(body);
        body.gameObject.SetActive(false);
        _free.Push(body);
    }

    /// <summary>表示領域の外側までの距離（V）。領域の中なら0以下</summary>
    private float OutsideDistance(Vector2 positionV)
    {
        return Mathf.Max(Mathf.Abs(positionV.x) - _aspect * 0.5f, Mathf.Abs(positionV.y) - 0.5f);
    }

    /// <summary>画面のUV（原点は左下） → V座標（中央が原点・縦が1）</summary>
    private Vector2 UvToV(Vector2 uv)
    {
        return new Vector2((uv.x - 0.5f) * _aspect, uv.y - 0.5f);
    }

    /// <summary>V座標 → 画面のUV（原点は左下）</summary>
    private Vector2 VToUv(Vector2 v)
    {
        return new Vector2(v.x / _aspect + 0.5f, v.y + 0.5f);
    }

    private void ApplyTransform(SolarBody body)
    {
        body.transform.position = new Vector3(
            _worldCenter.x + body.PositionV.x * _worldHeight,
            _worldCenter.y + body.PositionV.y * _worldHeight,
            worldZ);
        body.transform.localScale = Vector3.one * body.Scale;
    }

    private bool ResolveDisplayCamera()
    {
        if (displayCamera == null)
        {
            displayCamera = Camera.main;
            if (displayCamera == null)
            {
                Debug.LogError("[SolarBodies] displayCamera を割り当ててください");
                return false;
            }
            Debug.LogWarning($"[SolarBodies] displayCamera が未割り当てなので、{displayCamera.name} を使います");
        }
        if (!displayCamera.orthographic)
        {
            Debug.LogError("[SolarBodies] displayCamera は Orthographic にしてください");
            return false;
        }
        return true;
    }

    /// <summary>V座標をワールドに変えるための、表示領域の中心と高さ。Boids と同じく、RT のアスペクト比で幅を決める</summary>
    private void UpdateDisplayArea()
    {
        _worldCenter = displayCamera.transform.position;
        _worldHeight = displayCamera.orthographicSize * 2f;
    }
}
