using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem.Controls;
using Random = UnityEngine.Random;

public class Boids : MonoBehaviour
{
    [SerializeField] private Boid boidInstance;
    [SerializeField] private ComputeShader shader;
    
    [SerializeField, Range(1, 100)] private int boidsCount = 10;
    [Header("Boidが生成、移動可能な領域")]
    [SerializeField] private Vector3 minBounds;
    [SerializeField] private Vector3 maxBounds;
    [Header("表示される領域")] 
    [SerializeField] private Vector2 minDisplayArea;
    [SerializeField] private Vector2 maxDisplayArea;
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
    [Header("メッシュ")]
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
    private const int MaxSamplePoints = 100;
    private Vector2 _resolution;
    
    private bool _needsRefresh;

    private void OnValidate() { _needsRefresh = true; }
    
    public void Initialize(float resolutionX, float resolutionY)
    {
        _resolution = new Vector2(resolutionX, resolutionY);
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
            Debug.Log($"i[{i}] = {_uvs[i]}");
        }

        _needsRefresh = false;
    }

    public void Tick(RenderTexture velocityField)
    {
        if (_needsRefresh) { Refresh(); }
        // 魚の頭の速度場を読む
        
        SampleVelocity(velocityField);
        
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

            // Separation
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
            // Rotationの更新
            if (_boids[i].velocity.sqrMagnitude > 1e-8f)
            {
                var delta = Quaternion.FromToRotation(_boids[i].transform.forward, _boids[i].velocity);
                var target = delta * _boids[i].transform.rotation;
                _boids[i].transform.rotation = Quaternion.Slerp(_boids[i].transform.rotation, target, rotationSpeed * Time.deltaTime);
            }
            // ワールド速度(unit/s)を表示領域に対するUV速度(uv/s)に変換
            var displaySize = maxDisplayArea - minDisplayArea;
            _uvVelocities[i] = new Vector2(
                _boids[i].velocity.x / displaySize.x,
                _boids[i].velocity.y / displaySize.y);
            
            // Positionの更新
            var position = pos;
            position += _boids[i].velocity * Time.deltaTime;
            _boids[i].transform.position = position;
            _uvs[i] = ((Vector2)position - minDisplayArea) / (maxDisplayArea - minDisplayArea);
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
