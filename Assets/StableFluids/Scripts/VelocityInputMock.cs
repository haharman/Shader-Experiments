using UnityEngine;
using UnityEngine.InputSystem;

namespace StableFluids
{
    public readonly struct VelocityInputData
    {
        public VelocityInputData(Vector2 position, Vector2 velocity, float radius)
        {
            PositionUv = position;
            VelocityUv = velocity;
            Radius = radius;
        }
        public readonly Vector2 PositionUv;
        public readonly Vector2 VelocityUv;
        public readonly float Radius;
    }
    
    public class VelocityInputMock : MonoBehaviour
    {
        [SerializeField] private float minVelocityPx;
        [SerializeField] private float radiusPx;
        public bool TryGetVelocityInput(out VelocityInputData velocityInput)
        {
            velocityInput = default;
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
            velocityInput = new VelocityInputData(position, velocity, r);
            return true;
        }
    }
}