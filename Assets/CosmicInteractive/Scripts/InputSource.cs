using UnityEngine;
using UnityEngine.InputSystem;

public readonly struct InputData
{
    public InputData(Vector2 position, Vector2 velocity, float radius)
    {
        PositionUv = position;
        VelocityUv = velocity;
        Radius = radius;
    }
    public readonly Vector2 PositionUv;
    public readonly Vector2 VelocityUv;
    public readonly float Radius;
}

public class InputSource : MonoBehaviour
{
    [SerializeField] private float minVelocityPx;
    [SerializeField] private float pointerRadiusPx;
    [SerializeField] private float boidsRadiusPx;
    [SerializeField] private Boids boids;
    
    public bool TryGetInput(out InputData[] input)
    {
        input = default;
        if (Time.deltaTime <= 0f) { return false; }

        // Boidsの入力（ポインターの状態に関わらず常に入る）。半径はpx→UV(高さ基準)
        var (pos, vel) = boids.GetInputs();
        var boidsRadius = boidsRadiusPx / Screen.height;
        var hasPointer = TryGetPointerInput(out var pointerInput);

        input = new InputData[pos.Length + (hasPointer ? 1 : 0)];
        for (int i = 0; i < pos.Length; i++)
        {
            input[i] = new InputData(pos[i], vel[i], boidsRadius);
        }
        if (hasPointer) { input[pos.Length] = pointerInput; }
        return input.Length > 0;
    }

    private bool TryGetPointerInput(out InputData data)
    {
        data = default;

        // ポインターが押されているか
        if (Pointer.current == null) { return false; }
        if (!Pointer.current.press.isPressed) { return false; }

        // UV速度
        var velocity = Pointer.current.delta.ReadValue() / Time.deltaTime;
        if (velocity.sqrMagnitude < minVelocityPx * minVelocityPx) { return false; }
        velocity.x /= Screen.width;
        velocity.y /= Screen.height;

        // UV位置
        var position = Pointer.current.position.ReadValue();
        position.x /= Screen.width;
        position.y /= Screen.height;

        // 影響半径
        var r = pointerRadiusPx / Screen.height;

        data = new InputData(position, velocity, r);
        return true;
    }
}