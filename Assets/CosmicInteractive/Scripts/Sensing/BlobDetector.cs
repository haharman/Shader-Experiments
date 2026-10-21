using System.Collections.Generic;
using UnityEngine;

/// <summary>板座標に変換した1点</summary>
public readonly struct BoardPoint
{
    public BoardPoint(Vector2 position, float distance, float angleRad)
    {
        Position = position;
        Distance = distance;
        AngleRad = angleRad;
    }
    public readonly Vector2 Position; // 板座標[mm]。x右・y下、原点はセンサーの回転中心
    public readonly float Distance;   // センサーからの距離[mm]
    public readonly float AngleRad;   // センサーが測った角度[ラジアン]（取り付けのずれを引く前）
}

/// <summary>隣り合う点をまとめた塊</summary>
public readonly struct Blob
{
    public Blob(Vector2 center, float width, int pointCount)
    {
        Center = center;
        Width = width;
        PointCount = pointCount;
    }
    public readonly Vector2 Center; // 板座標[mm]。含まれる点の平均
    public readonly float Width;    // 並びの最初と最後の点の距離[mm]
    public readonly int PointCount;
}

public static class BlobDetector
{
    /// <summary>
    /// 角度の昇順に並んだ点を、隣との間隔で区切って塊にする。小さい塊（指など）は result に入れない。
    /// </summary>
    public static void Detect(
        List<BoardPoint> points,
        float gapBaseMm,
        float gapFactor,
        int minPoints,
        float minWidthMm,
        List<Blob> result)
    {
        result.Clear();

        int start = 0; // 今の塊の最初の点の添字
        for (int i = 1; i <= points.Count; i++)
        {
            // 末尾に来たか、i-1 番目と i 番目の間が開いていたら、[start, i) が1つの塊
            bool isEnd = i == points.Count || IsGap(points[i - 1], points[i], gapBaseMm, gapFactor);
            if (!isEnd) continue;

            AddIfLargeEnough(points, start, i, minPoints, minWidthMm, result);
            start = i;
        }
    }

    private static bool IsGap(BoardPoint a, BoardPoint b, float gapBaseMm, float gapFactor)
    {
        float deltaAngleRad = Mathf.Abs(b.AngleRad - a.AngleRad);
        float meanDistance = (a.Distance + b.Distance) * 0.5f;
        float gap = gapBaseMm + gapFactor * meanDistance * deltaAngleRad;
        return Vector2.Distance(a.Position, b.Position) > gap;
    }

    private static void AddIfLargeEnough(
        List<BoardPoint> points, int start, int endExclusive, int minPoints, float minWidthMm, List<Blob> result)
    {
        int count = endExclusive - start;
        if (count < minPoints) return;

        float width = Vector2.Distance(points[start].Position, points[endExclusive - 1].Position);
        if (width < minWidthMm) return;

        Vector2 sum = Vector2.zero;
        for (int i = start; i < endExclusive; i++) { sum += points[i].Position; }

        result.Add(new Blob(sum / count, width, count));
    }
}
