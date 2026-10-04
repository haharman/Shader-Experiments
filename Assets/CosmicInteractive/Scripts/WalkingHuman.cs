using UnityEngine;

public class WalkingHuman : MonoBehaviour
{
    [SerializeField] private float cycleLength = 1.5f;
    [SerializeField] private string stateName = "Walk";
    [SerializeField] private float exhibitMinX;
    [SerializeField] private float exhibitMaxX;
    [SerializeField] private float walkBorderX;
    [SerializeField, Range(0f, 5f)] private float minSpeed = 0.5f;
    [SerializeField, Range(0f, 5f)] private float maxSpeed = 3f;
    
    private Animator _animator;
    private Transform _transform;
    private float _startX;
    private bool _initialized;
    private float _speed;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _transform = GetComponent<Transform>();
        if(_animator == null) { Debug.LogError("[WalkByX] Animatorコンポーネントが見つかりません");}

        _animator.speed = 0f;
        _initialized = true;
        
        // minSpeed~maxSpeedの範囲で、正負をランダムに分布させる
        _speed = Random.Range(minSpeed - maxSpeed, maxSpeed - minSpeed);
        _speed = _speed > 0f ? minSpeed + _speed : -minSpeed + _speed;
        
        // 向きを設定
        float rotationY = _speed > 0f ? 90f : -90f;
        _transform.rotation = Quaternion.Euler(0f, rotationY, 0f);
        
        // exhibit領域の外側、walkBorderの内側にランダムに分布させる
        float x = Random.Range(-walkBorderX, walkBorderX);
        x = x > 0 ? exhibitMaxX + x : exhibitMinX + x;
        _transform.position = new Vector3(x, _transform.position.y, _transform.position.z);
        _startX = x;
    }

    private void Update()
    {
        if (!_initialized) return;
        
        var position = _transform.position;
        position.x += _speed * Time.deltaTime;
        
        if (position.x > exhibitMaxX + walkBorderX)
        {
            position.x -= exhibitMaxX - exhibitMinX + 2f * walkBorderX;
        }

        if (position.x < exhibitMinX - walkBorderX)
        {
            position.x += exhibitMaxX - exhibitMinX + 2f * walkBorderX;
        }
        
        _transform.position = position;

        float t = Mathf.Repeat(Mathf.Sign(_speed) * (position.x - _startX) / cycleLength, 1);
        Debug.Log(t);
        _animator.Play(stateName, 0, t);
    }
}