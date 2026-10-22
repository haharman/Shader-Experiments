using UnityEngine;

/// <summary>
/// 開発用。VelocityInputData を Game ビューに重ねて描く。
/// 円 = 影響半径、線 = 速度（arrowSeconds 秒ぶん進んだ位置）。
/// useMock を ON にすると、センサーなしでダミーの入力を描く（表示の向きの確認用）
/// </summary>
public class VelocityInputDebugView : MonoBehaviour
{
    private const int CircleSegments = 32;
    private const float ArrowHeadPx = 12f;

    [SerializeField] private SensorInputSource sensor;
    [SerializeField] private bool useMock;
    [SerializeField] private float arrowSeconds = 0.5f;
    [SerializeField] private float lineWidthPx = 3f;
    [SerializeField] private Color color = Color.cyan;

    private void OnGUI()
    {
        // OnGUI は1フレームに何度も呼ばれる。描くのは Repaint のときだけ
        if (Event.current.type != EventType.Repaint) return;

        VelocityInputData[] inputs = null;
        bool hasInput;
        if (useMock) { inputs = MockInputs(); hasInput = true; }
        else if (sensor != null) { hasInput = sensor.TryGetVelocityInput(out inputs); }
        else { hasInput = false; }

        GUI.color = color;
        GUI.Label(new Rect(10f, 10f, 500f, 24f), hasInput ? $"VelocityInput: {inputs.Length}" : "VelocityInput: none");
        if (!hasInput) return;

        foreach (var d in inputs) { DrawInput(d); }
    }

    private void DrawInput(VelocityInputData d)
    {
        Vector2 center = UvToScreen(d.PositionUv);

        // 半径は高さを1としたUV。縦横どちらにも px では Screen.height を掛ける
        DrawCircle(center, d.Radius * Screen.height);

        // 速度はUV/秒。x は幅、y は高さで割ってあるので、それぞれ掛け戻す。y は画面の下向きが正なので反転する
        Vector2 offset = new Vector2(d.VelocityUv.x * Screen.width, -d.VelocityUv.y * Screen.height) * arrowSeconds;
        Vector2 tip = center + offset;
        DrawLine(center, tip);

        // 矢じり: 進行方向と反対向きを、±30°振った2本
        if (offset.sqrMagnitude > 1f)
        {
            Vector2 back = -offset.normalized * ArrowHeadPx;
            DrawLine(tip, tip + Rotate(back, 30f));
            DrawLine(tip, tip + Rotate(back, -30f));
        }
    }

    /// <summary>UV（原点は左下）→ GUI座標（原点は左上、px）</summary>
    private static Vector2 UvToScreen(Vector2 uv)
    {
        return new Vector2(uv.x * Screen.width, (1f - uv.y) * Screen.height);
    }

    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
    }

    private void DrawCircle(Vector2 center, float radiusPx)
    {
        Vector2 previous = center + new Vector2(radiusPx, 0f);
        for (int i = 1; i <= CircleSegments; i++)
        {
            float angle = i * Mathf.PI * 2f / CircleSegments;
            Vector2 next = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radiusPx;
            DrawLine(previous, next);
            previous = next;
        }
    }

    /// <summary>細長い四角を、始点を軸に回して線にする</summary>
    private void DrawLine(Vector2 from, Vector2 to)
    {
        Vector2 delta = to - from;
        float length = delta.magnitude;
        if (length < 0.01f) return;

        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        Matrix4x4 saved = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, from);
        GUI.DrawTexture(new Rect(from.x, from.y - lineWidthPx * 0.5f, length, lineWidthPx), Texture2D.whiteTexture);
        GUI.matrix = saved;
    }

    /// <summary>円を左回り（UVのyが上向きで見て反時計回り）に回る、ダミーの入力1件</summary>
    private static VelocityInputData[] MockInputs()
    {
        float t = Time.time;
        Vector2 position = new Vector2(0.5f + 0.3f * Mathf.Cos(t), 0.5f + 0.3f * Mathf.Sin(t));
        Vector2 velocity = new Vector2(-Mathf.Sin(t), Mathf.Cos(t)) * 0.3f; // 位置の微分の向き（円の接線）
        return new[] { new VelocityInputData(-1, position, velocity, 0.1f) };
    }
}
