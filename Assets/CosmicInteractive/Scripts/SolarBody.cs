using UnityEngine;

/// <summary>
/// SolarBodies が管理する星1つ分の状態。
/// 位置・速度は、画面中央を原点・縦を1とする座標（V座標）で持つ
/// </summary>
public class SolarBody : MonoBehaviour
{
    private static readonly int TemperatureId = Shader.PropertyToID("_Temperature");

    private Renderer[] _renderers;
    private MaterialPropertyBlock _block;

    public int Id;
    public long SpawnSerial;     // 生成順。小さいほど古い
    public Vector2 PositionV;
    public Vector2 VelocityV;
    public Vector2 FollowTargetV;
    public bool IsFollowing;     // 今フレーム、VelocityInput に追従しているか
    public float Scale;
    public float Mass;           // この星の質量。動きの重さ(慣性・追従の遅さ)に使う
    public float MassT;          // 質量を minMass〜maxMass で 0〜1 に正規化した値。温度・光の強さの曲線の入力
    public float SpawnTime;      // 生成した Time.time。光のフェードインに使う

    /// <summary>この星だけの色温度(K)を設定する。マテリアルは共有のまま、Renderer ごとの MaterialPropertyBlock で上書きする</summary>
    public void SetTemperature(float kelvin)
    {
        if (_renderers == null)
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _block = new MaterialPropertyBlock();
        }
        foreach (var r in _renderers)
        {
            r.GetPropertyBlock(_block);
            _block.SetFloat(TemperatureId, kelvin);
            r.SetPropertyBlock(_block);
        }
    }
}
