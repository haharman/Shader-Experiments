using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 何も触れていない状態の距離を、角度の箱ごとに覚える。
/// 覚えた距離より近い点だけを「前景」とする（壁など、動かないものを消すため）
/// </summary>
public class BackgroundModel
{
    private readonly float _binDeg;
    private readonly int _binCount;

    private float[] _distances;     // 箱ごとの背景の距離[mm]。0は「背景なし」。記録が終わるまでは null
    private List<float>[] _samples; // 記録中に、箱ごとに集めた距離
    private int _scansLeft;         // 記録に使う残りの周の数

    public bool IsRecording => _scansLeft > 0;
    public bool HasBackground => _distances != null;

    public BackgroundModel(float binDeg)
    {
        _binDeg = binDeg;
        _binCount = Mathf.CeilToInt(360f / binDeg);
    }

    public void BeginRecording(int scanCount)
    {
        _samples = new List<float>[_binCount];
        for (int i = 0; i < _binCount; i++) { _samples[i] = new List<float>(); }
        _scansLeft = scanCount;
    }

    /// <summary>記録中の1周を渡す。決めた周の数に達したら、中央値を背景にして記録を終える</summary>
    public void AddScan(LidarPoint[] points)
    {
        foreach (var p in points)
        {
            if (p.Quality <= 0f || p.Distance <= 0f) continue;
            _samples[BinIndex(p.Angle)].Add(p.Distance);
        }

        _scansLeft--;
        if (_scansLeft == 0) { FinishRecording(); }
    }

    /// <summary>背景より marginMm 以上近ければ true（前景）。背景がない箱の点も true</summary>
    public bool IsForeground(LidarPoint p, float marginMm)
    {
        if (_distances == null) { return true; }
        float background = _distances[BinIndex(p.Angle)];
        return background <= 0f || p.Distance < background - marginMm;
    }

    private void FinishRecording()
    {
        var distances = new float[_binCount];
        int filled = 0;
        for (int i = 0; i < _binCount; i++)
        {
            var samples = _samples[i];
            if (samples.Count == 0) continue;
            samples.Sort();
            distances[i] = samples[samples.Count / 2]; // 中央値
            filled++;
        }
        _distances = distances;
        _samples = null;
        Debug.Log($"[BackgroundModel] 背景を記録しました（{filled}/{_binCount} 箱に背景あり）");
    }

    private int BinIndex(float angleDeg)
    {
        return Mathf.Clamp((int)(angleDeg / _binDeg), 0, _binCount - 1);
    }
}
