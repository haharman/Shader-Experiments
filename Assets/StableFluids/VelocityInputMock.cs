using UnityEngine;
using UnityEngine.InputSystem;

namespace StableFluids
{
    public readonly struct VelocityInputData
    {
        public VelocityInputData(Vector3 position, Vector3 velocity, float radius)
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
        [SerializeField] private float minVelocity;
        [SerializeField] private float radius;
        private VelocityInputData _velocityInputData;
        public bool TryGetVelocityInput(out VelocityInputData velocityInput)
        {
            // ポインターが押されているか
            var isPressed = Pointer.current.press.isPressed;
            if (!isPressed) { velocityInput = default; return false; }
            
            // ポインターのUV速度
            var velocity = Pointer.current.delta.ReadValue() / Time.deltaTime;
            velocity.x /= Screen.width;
            velocity.y /= Screen.height;
            
            if (_velocityInputData.Radius < minVelocity)
            {
                velocityInput = default;
                return false;
            }
            
            // ポインターのUV位置
            var position = Pointer.current.position.ReadValue();
            position.x /= Screen.width;
            position.y /= Screen.height;
            
            var r = radius / Screen.height;
            
            _velocityInputData = new VelocityInputData(position, velocity, r);
            velocityInput = _velocityInputData;
            return true;
        }
    }
}