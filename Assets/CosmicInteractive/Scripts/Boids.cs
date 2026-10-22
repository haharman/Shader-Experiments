using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem.Controls;
using Random = UnityEngine.Random;

public class Boids : MonoBehaviour
{
    private const int MaxSamplePoints = 200;
    
    [SerializeField] private Boid boidInstance;
    [SerializeField] private ComputeShader shader;
    [SerializeField, Range(1, 200)] private int boidsCount = 10;
    [SerializeField] private SensorInputSource inputSource;
    [Header("Boidが生成、移動可能な領域")]
    [SerializeField] private Vector3 minBounds;
    [SerializeField] private Vector3 maxBounds;
    [Header("表示される領域")] 
    [SerializeField] private Camera displayCamera; // 魚を描くカメラ（Orthographic）。未割り当てなら Main Camera を使う
    [Header("Boidの速度")]
    [SerializeField] private float minVelocity;
    [SerializeField] private float maxVelocity;
    [SerializeField] private float maxSteeringForce;
    [SerializeField] private float rotationSpeed;
    [Header("結合")]
    [SerializeField] private float cohesionRadius;
    [SerializeField] private float cohesionWeight;
    [Header("分離")]
    [SerializeField] private float separationRadius;
    [SerializeField] private float separationWeight;
    [Header("整列")]
    [SerializeField] private float alignmentRadius;
    [SerializeField] private float alignmentWeight;
    [Header("遡上")]
    [SerializeField] private float upstreamWeight;
    [Header("封じ込め")] 
    [SerializeField] private float containmentWeight;

    [Header("逃げ")] 
    [SerializeField] private float escapeRadius;
    [SerializeField] private float escapeWeight;
    [SerializeField] private float escapeDecay;
    [SerializeField] private float maxEscapeVelocity;

    [Header("波打ち")]
    [SerializeField] private float baseWaveSpeed = 5f;      // 通常の波の速さ
    [SerializeField] private float maxWaveSpeed = 40f;      // EscapeVelocity が maxEscapeVelocity のときの波の速さ
    [SerializeField] private float maxAmplitudeScale = 2f;  // 同じときの振幅の倍率（通常は 1）
    [SerializeField] private float waveSpeedSmoothing = 5f; // 大きいほど、速さの変化にすばやく追従する

    [Header("メッシュ")]
    [SerializeField] private float maxScale = 1f;
    [SerializeField] private float minScale = 1f;
    [SerializeField] private Vector3 scale;
    [SerializeField] private Vector3 rotation;
    
    private Boid[] _boids;

    private Vector2[] _uvs;
    private Vector2[] _uvVelocities;
    private Vector2[] _sampleVelocities;
    
    private ComputeBuffer _sampleUvBuffer;
    private ComputeBuffer _sampleVelocityBuffer;
    
    private int _sampleVelocityKernelIndex;
    private Vector3Int _sampleVelocityGroupSize;
    private Vector2Int _res;
    private bool _needsRefresh;

    // 表示される領域（ワールド座標）。Initialize で displayCamera の視野から求める
    private Vector2 minDisplayArea;
    private Vector2 maxDisplayArea;

    private void OnValidate() { _needsRefresh = true; }
    
    public void Initialize(Vector2Int resolution)
    {
        _res = resolution;
        UpdateDisplayArea();
        Refresh();
        
        // ComputeBufferの設定
        int stride = Marshal.SizeOf(typeof(Vector2));
        _sampleUvBuffer = new ComputeBuffer(MaxSamplePoints, stride);
        _sampleVelocityBuffer = new ComputeBuffer(MaxSamplePoints, stride);
        var initial = new Vector2[MaxSamplePoints];
        for (int i = 0; i < MaxSamplePoints; i++)
        {
            initial[i] = new Vector2(0f, 0f);
        }
        _sampleUvBuffer.SetData(initial);
        _sampleVelocityBuffer.SetData(initial);
        
        // カーネル名の取得
        _sampleVelocityKernelIndex = shader.FindKernel("SampleVelocity");

        // GroupSize(Threads数)を取得
        uint x, y, z;
        shader.GetKernelThreadGroupSizes(_sampleVelocityKernelIndex, out x, out y, out z);
        _sampleVelocityGroupSize = new Vector3Int((int)x, (int)y, (int)z);
    }
    
    /// <summary>
    /// 表示される領域（ワールドの XY）を、魚を描くカメラの視野から求める。
    /// 魚は RT（resolution）にそのアスペクト比で描かれ、RT が画面いっぱいに引き伸ばされるので、
    /// 画面（Canvas）ではなく、RT のアスペクト比で視野の幅を決める。解像度が変わっても、ここで追従する
    /// </summary>
    private void UpdateDisplayArea()
    {
        if (displayCamera == null)
        {
            displayCamera = Camera.main;
            if (displayCamera == null)
            {
                Debug.LogError("[Boids] displayCamera を割り当ててください");
                return;
            }
            Debug.LogWarning($"[Boids] displayCamera が未割り当てなので、{displayCamera.name} を使います");
        }
        if (!displayCamera.orthographic)
        {
            Debug.LogError("[Boids] displayCamera は Orthographic にしてください");
            return;
        }

        Vector2 center = displayCamera.transform.position;
        float halfHeight = displayCamera.orthographicSize;
        float halfWidth = halfHeight * _res.x / _res.y;
        minDisplayArea = center - new Vector2(halfWidth, halfHeight);
        maxDisplayArea = center + new Vector2(halfWidth, halfHeight);
    }

    private void Refresh()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Destroy(transform.GetChild(i).gameObject);
        }
        _uvs = new Vector2[boidsCount];
        _uvVelocities = new Vector2[boidsCount];
        _sampleVelocities = new Vector2[boidsCount];
        
        _boids = new Boid[boidsCount];
        for (int i = 0; i < boidsCount; i++)
        {
            var position = new Vector3(
                Random.Range(minBounds.x, maxBounds.x),
                Random.Range(minBounds.y, maxBounds.y),
                Random.Range(minBounds.z, maxBounds.z));
            _boids[i] = Instantiate(boidInstance, position, Random.rotation, this.transform);
            _boids[i].model.transform.localScale = scale;
            _boids[i].velocity = Random.insideUnitSphere * Random.Range(minVelocity, maxVelocity);
            _boids[i].model.localRotation = Quaternion.Euler(rotation);
            _uvs[i] = ((Vector2)position - minDisplayArea) / (maxDisplayArea - minDisplayArea);
        }

        _needsRefresh = false;
    }

    public void Tick(RenderTexture velocityField)
    {
        if (_needsRefresh) { Refresh(); }
        // 魚の頭の速度場を読む
        
        SampleVelocity(velocityField);

        // 触れた点（UV）を、Boid と同じ座標系（ワールドの XY）に変換する。
        // TryGetPointInput は呼ぶと空になるので、Boid ごとのループの外で1回だけ呼ぶ
        var displaySize = maxDisplayArea - minDisplayArea;
        Vector2[] touchPositions = null;
        if (inputSource.TryGetPointInput(out PointInputData[] points))
        {
            touchPositions = new Vector2[points.Length];
            for (int p = 0; p < points.Length; p++)
            {
                touchPositions[p] = minDisplayArea + points[p].PositionUv * displaySize;
            }
        }
        
        for (int i = 0; i < boidsCount; i++)
        {
            Vector3 targetVelocity = Vector3.zero;
            Vector3 pos = _boids[i].transform.position;
            Vector3 vel = _boids[i].velocity;
            
            Vector3 separationVelocitySum = Vector3.zero;
            int separationCount = 0;
            
            Vector3 cohesionPositionSum = Vector3.zero;
            int cohesionCount = 0;

            Vector3 alignmentVelocitySum = Vector3.zero;
            int alignmentCount = 0;

            bool escape = false;
            
            for (int j = 0; j < boidsCount; j++)
            {
                if (j == i) continue;
                var dist = Vector3.Distance(pos, _boids[j].transform.position);
                
                if (dist < separationRadius)
                {
                    var dir = (pos - _boids[j].transform.position).normalized;
                    separationVelocitySum += dir / dist;// * dist);
                    separationCount++;
                }

                if (dist < cohesionRadius)
                {
                    cohesionPositionSum += _boids[j].transform.position;
                    cohesionCount++;
                }

                if (dist < alignmentRadius)
                {
                    alignmentVelocitySum += _boids[j].velocity;
                    alignmentCount++;
                }
            }

            if (touchPositions != null)
            {
                foreach (var touchPosition in touchPositions)
                {
                    var dist = Vector2.Distance(touchPosition, pos);
                    if (dist < escapeRadius)
                    {
                        Vector3 escapeAngle = new Vector3(pos.x - touchPosition.x, pos.y - touchPosition.y, 0f).normalized;
                        _boids[i].escapeVelocity += escapeAngle * Mathf.Min(Mathf.Clamp01(escapeRadius/dist) * escapeWeight, maxEscapeVelocity);
                        escape = true;
                    }
                }
            }
            
            if (separationCount > 0)
            {
                targetVelocity += separationVelocitySum * separationWeight;
            }

            if (cohesionCount > 0)
            {
                var avr = cohesionPositionSum / cohesionCount;
                var dir = (avr - pos).normalized;
                targetVelocity += dir * cohesionWeight;
            }

            if (alignmentCount > 0)
            {
                var avr = alignmentVelocitySum / alignmentCount;
                targetVelocity += avr * alignmentWeight;
            }

            // Upsteam
            Vector3 upstreamVelocity = new Vector3(_sampleVelocities[i].x, _sampleVelocities[i].y, 0) * -1f;
            targetVelocity += upstreamVelocity * upstreamWeight;
            
            // Containment
            Vector3 containmentVelocity = Vector3.Min(maxBounds - pos, Vector3.zero);
            containmentVelocity += Vector3.Max(minBounds - pos, Vector3.zero);
            targetVelocity += containmentVelocity * containmentWeight;
            
            // Velocityの更新
            Vector3 steeringForce = Vector3.ClampMagnitude(targetVelocity - _boids[i].velocity, maxSteeringForce);
            var v = _boids[i].velocity + steeringForce * Time.deltaTime;
            _boids[i].velocity = v.normalized * Mathf.Clamp(v.magnitude, minVelocity, maxVelocity);
            
            // EscapeVelocityの更新
            _boids[i].escapeVelocity *= escapeDecay;
            if(_boids[i].velocity.magnitude < 0.1f) _boids[i].velocity = Vector3.zero;
            
            // 波打ちの更新: EscapeVelocity が maxEscapeVelocity に近いほど、速く大きく波打つ
            var escapeRatio = maxEscapeVelocity > 0f ? Mathf.Clamp01(_boids[i].escapeVelocity.magnitude / maxEscapeVelocity) : 0f;
            var waveSpeed = Mathf.Lerp(baseWaveSpeed, maxWaveSpeed, escapeRatio);
            var amplitudeScale = Mathf.Lerp(1f, maxAmplitudeScale, escapeRatio);
            _boids[i].AdvanceWave(waveSpeed, amplitudeScale, waveSpeedSmoothing, Time.deltaTime);
            
            // Rotationの更新
            if (escape)
            {
                _boids[i].transform.rotation = Quaternion.LookRotation(_boids[i].velocity + _boids[i].escapeVelocity);
            }
            else
            {
                if ((_boids[i].velocity + _boids[i].escapeVelocity).sqrMagnitude > 1e-8f)
                {
                    var delta = Quaternion.FromToRotation(_boids[i].transform.forward, (_boids[i].velocity + _boids[i].escapeVelocity));
                    var target = delta * _boids[i].transform.rotation;
                    _boids[i].transform.rotation = Quaternion.Slerp(_boids[i].transform.rotation, target, rotationSpeed * Time.deltaTime);
                }
            }
            
            // ワールド速度(unit/s)を表示領域に対するUV速度(uv/s)に変換
            _uvVelocities[i] = new Vector2(
                _boids[i].velocity.x / displaySize.x,
                _boids[i].velocity.y / displaySize.y);
            
            // Positionの更新
            var position = pos;
            position += (_boids[i].velocity + _boids[i].escapeVelocity) * Time.deltaTime;
            _boids[i].transform.position = position;
            _uvs[i] = ((Vector2)position - minDisplayArea) / (maxDisplayArea - minDisplayArea);

            // Scaleの更新
            _boids[i].transform.localScale = Vector3.Lerp(Vector3.one * minScale, Vector3.one * maxScale, Mathf.InverseLerp(minBounds.z, maxBounds.z, position.z));
        }
    }

    private void SampleVelocity(RenderTexture velocityField)
    {
        int count = _uvs.Length;
        _sampleUvBuffer.SetData(_uvs);
        shader.SetInt("_SamplePoints", count);
        shader.SetTexture(_sampleVelocityKernelIndex, "_VelocityFieldRead", velocityField);
        shader.SetBuffer(_sampleVelocityKernelIndex,"_SampleUvBufferRead", _sampleUvBuffer);
        shader.SetBuffer(_sampleVelocityKernelIndex, "_SampleVelocityBufferWrite", _sampleVelocityBuffer);
        shader.Dispatch(_sampleVelocityKernelIndex, 
            (MaxSamplePoints + _sampleVelocityGroupSize.x - 1) / _sampleVelocityGroupSize.x,
            1, 1);
        _sampleVelocityBuffer.GetData(_sampleVelocities, 0, 0, count);
    }

    public (Vector2[], Vector2[]) GetInputs()
    {
        return (_uvs, _uvVelocities);
    }

    private void OnDestroy()
    {
        _sampleUvBuffer?.Release();
        _sampleVelocityBuffer?.Release();
    }
}
