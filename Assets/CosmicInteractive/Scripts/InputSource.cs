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
    [SerializeField] private float radiusPx;
    
    public bool TryGetInput(out InputData[] input)
    {
        input = default;
        if (Time.deltaTime <= 0f) { return false; }
        
        // ポインターが押されているか
        if (Pointer.current == null) { return false; }
        var isPressed = Pointer.current.press.isPressed;
        if (!isPressed) { return false; }
        
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
        var r = radiusPx / Screen.height;
        
        // return
        input = new InputData[2];
        input[0] = new InputData(position, velocity, r);
        input[1] = new InputData(Vector2.zero, Vector2.right, r/2);
        return true;
    }
}