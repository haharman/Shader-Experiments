using UnityEngine;

public class Boid : MonoBehaviour
{
    public Vector3 velocity;
    public Transform model;
    public Vector3 escapeVelocity;

    private static readonly int PhaseId = Shader.PropertyToID("_Phase");
    private static readonly int AmplitudeScaleId = Shader.PropertyToID("_AmplitudeScale");

    private Renderer[] _renderers;
    private MaterialPropertyBlock _block;
    private float _phase;
    private float _waveSpeed;
    private float _amplitudeScale;
    private bool _waveInitialized;

    /// <summary>
    /// 波打ちの位相を進めて、このBoidのマテリアルに渡す。
    /// 速さと振幅の倍率を目標に向けてなめらかに近づける。速さは積分して位相にする（速さを変えても位相が跳ばない）
    /// </summary>
    public void AdvanceWave(float targetSpeed, float targetAmplitudeScale, float smoothing, float deltaTime)
    {
        if (!_waveInitialized)
        {
            _renderers = GetComponentsInChildren<Renderer>();
            _block = new MaterialPropertyBlock();
            _phase = Random.value * Mathf.PI * 2f; // 個体ごとに位相をずらす
            _waveSpeed = targetSpeed;
            _amplitudeScale = targetAmplitudeScale;
            _waveInitialized = true;
        }

        var follow = 1f - Mathf.Exp(-smoothing * deltaTime);
        _waveSpeed = Mathf.Lerp(_waveSpeed, targetSpeed, follow);
        _amplitudeScale = Mathf.Lerp(_amplitudeScale, targetAmplitudeScale, follow);
        _phase += _waveSpeed * deltaTime;

        foreach (var r in _renderers)
        {
            r.GetPropertyBlock(_block);
            _block.SetFloat(PhaseId, _phase);
            _block.SetFloat(AmplitudeScaleId, _amplitudeScale);
            r.SetPropertyBlock(_block);
        }
    }
    
    void Start()
    {
        //var b = GetComponent<MeshFilter>().sharedMesh.bounds;
        //Debug.Log($"min.x={b.min.x}  max.x={b.max.x}  size.x={b.size.x}");
    }
}