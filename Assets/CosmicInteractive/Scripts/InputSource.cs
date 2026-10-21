using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>流体に渡す1件の入力。位置・速度はUV、半径は高さを1としたUV</summary>
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

/// <summary>触れた点1件。星の生成に使う</summary>
public readonly struct PointInputData
{
    public PointInputData(int id, Vector2 position)
    {
        Id = id;
        PositionUv = position;
    }
    public readonly int Id;
    public readonly Vector2 PositionUv;
}

public class InputSource : MonoBehaviour
{
    private const int PointerPointId = -1; // ポインター(マウス)の点のID。センサーのID(1以上)と重ならない

    [SerializeField] private float minVelocityPx;
    [SerializeField] private float pointerRadiusPx;
    [SerializeField] private float boidsRadiusPx;
    [SerializeField] private Boids boids;
    [SerializeField] private SensorInputSource sensor; // 未割り当てなら、センサーの入力は入らない
    
    public bool TryGetInput(out VelocityInputData[] input)
    {
        input = default;
        if (Time.deltaTime <= 0f) { return false; }

        // Boidsの入力（ポインターの状態に関わらず常に入る）。半径はpx→UV(高さ基準)
        var (pos, vel) = boids.GetInputs();
        var boidsRadius = boidsRadiusPx / Screen.height;
        var hasPointer = TryGetPointerInput(out var pointerInput);

        // センサーの入力。out 引数は条件式の中だと代入が保証されないので、先に null で用意する
        VelocityInputData[] sensorInput = null;
        var hasSensor = sensor != null && sensor.TryGetInput(out sensorInput);
        var sensorCount = hasSensor ? sensorInput.Length : 0;

        input = new VelocityInputData[pos.Length + (hasPointer ? 1 : 0) + sensorCount];
        for (int i = 0; i < pos.Length; i++)
        {
            input[i] = new VelocityInputData(pos[i], vel[i], boidsRadius);
        }
        var next = pos.Length;
        if (hasPointer) { input[next++] = pointerInput; }
        if (hasSensor) { sensorInput.CopyTo(input, next); }
        return input.Length > 0;
    }

    /// <summary>
    /// 新しく触れた点を取り出す。星の生成が、1フレームに1回呼ぶ。
    /// センサーの点（取り出すと空になる）と、ポインターを押した瞬間の点を合わせて返す。
    /// ポインターは、センサーなしで星の生成を開発するためのモック
    /// </summary>
    public bool TryGetPointInput(out PointInputData[] input)
    {
        input = default;

        // out 引数は条件式の中だと代入が保証されないので、先に null で用意する
        PointInputData[] sensorInput = null;
        var hasSensor = sensor != null && sensor.TryGetPointInput(out sensorInput);
        var hasPointer = TryGetPointerPoint(out var pointerPoint);
        if (!hasSensor && !hasPointer) { return false; }

        var sensorCount = hasSensor ? sensorInput.Length : 0;
        input = new PointInputData[sensorCount + (hasPointer ? 1 : 0)];
        if (hasSensor) { sensorInput.CopyTo(input, 0); }
        if (hasPointer) { input[sensorCount] = pointerPoint; }
        return true;
    }

    private bool TryGetPointerPoint(out PointInputData data)
    {
        data = default;

        // 押した瞬間のフレームだけ true になる
        if (Pointer.current == null) { return false; }
        if (!Pointer.current.press.wasPressedThisFrame) { return false; }

        var position = Pointer.current.position.ReadValue();
        position.x /= Screen.width;
        position.y /= Screen.height;

        data = new PointInputData(PointerPointId, position);
        return true;
    }

    private bool TryGetPointerInput(out VelocityInputData data)
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

        data = new VelocityInputData(position, velocity, r);
        return true;
    }
}
