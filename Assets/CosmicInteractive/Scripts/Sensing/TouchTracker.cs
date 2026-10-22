using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>追跡している塊1つ。値は TouchTracker だけが書き換える</summary>
public class TrackedBlob
{
    public int Id;
    public Vector2 PositionMm;  // 板座標[mm]
    public Vector2 VelocityMm;  // 板座標の速度[mm/秒]
    public float LastSeenTime;  // 最後に見えた時刻[秒]
    public int MissedScans;     // 連続して見えなかった周の数。0なら最新の周で見えている
    public bool IsNew;          // 最新の周で初めて現れたか
}

/// <summary>
/// 周ごとの塊を、前の周の塊と近い順に対応づけてIDを付け、移動量から速度を出す。
/// 見えなくなってから graceSeconds 秒を超えたトラックは消え、そのあとに現れた塊は新しいIDになる
/// </summary>
public class TouchTracker
{
    private readonly struct Pair
    {
        public Pair(float distance, int track, int blob)
        {
            Distance = distance;
            Track = track;
            Blob = blob;
        }
        public readonly float Distance;
        public readonly int Track; // _tracks の添字
        public readonly int Blob;  // blobs の添字
    }

    private readonly List<TrackedBlob> _tracks = new List<TrackedBlob>();
    private readonly List<Pair> _pairs = new List<Pair>();
    private readonly Func<int> _newId; // 新しいIDを返す。複数のトラッカーでIDが重ならないよう、持ち主から受け取る

    public TouchTracker(Func<int> newId)
    {
        _newId = newId ?? throw new ArgumentNullException(nameof(newId));
    }

    public IReadOnlyList<TrackedBlob> Tracks => _tracks;

    /// <summary>true なら、トラックが消えたときにログを出す（調整用）</summary>
    public bool LogEvents;

    public void Clear()
    {
        _tracks.Clear();
    }

    public void Update(List<Blob> blobs, float time, float maxMatchMm, float graceSeconds, float velocitySmoothing)
    {
        // 前の周の「新しい」印を消す
        foreach (var track in _tracks) { track.IsNew = false; }

        // 1. 距離が maxMatchMm 以下の組を、近い順に並べる
        _pairs.Clear();
        for (int t = 0; t < _tracks.Count; t++)
        {
            for (int b = 0; b < blobs.Count; b++)
            {
                float distance = Vector2.Distance(_tracks[t].PositionMm, blobs[b].Center);
                if (distance <= maxMatchMm) { _pairs.Add(new Pair(distance, t, b)); }
            }
        }
        _pairs.Sort(CompareByDistance);

        // 2, 3. 近い組から順に対応づけ、速度を出す
        var trackMatched = new bool[_tracks.Count];
        var blobMatched = new bool[blobs.Count];
        foreach (var pair in _pairs)
        {
            if (trackMatched[pair.Track] || blobMatched[pair.Blob]) continue;
            trackMatched[pair.Track] = true;
            blobMatched[pair.Blob] = true;

            var track = _tracks[pair.Track];
            Vector2 center = blobs[pair.Blob].Center;
            float dt = time - track.LastSeenTime;
            if (track.MissedScans > 0)
            {
                // 見えない間に動いた分は、速度として信頼できない（離して別の所を触った跳びが、速度になってしまう）
                track.VelocityMm = Vector2.zero;
            }
            else if (dt > 0f)
            {
                // 今の周の差だけから出した速度は、位置のゆれがそのまま出る。前の速度との平均を取ってなめらかにする
                Vector2 rawVelocity = (center - track.PositionMm) / dt;
                track.VelocityMm = Vector2.Lerp(track.VelocityMm, rawVelocity, velocitySmoothing);
            }
            track.PositionMm = center;
            track.LastSeenTime = time;
            track.MissedScans = 0;
        }

        // 4. 対応づかなかったトラックは、最後に見えてから graceSeconds 秒を超えたら消す。後ろから消すと、前の添字がずれない
        for (int t = _tracks.Count - 1; t >= 0; t--)
        {
            if (trackMatched[t]) continue;
            _tracks[t].MissedScans++;
            if (time - _tracks[t].LastSeenTime > graceSeconds)
            {
                if (LogEvents) { Debug.Log($"[TouchTracker] lost id={_tracks[t].Id} positionMm={_tracks[t].PositionMm}"); }
                _tracks.RemoveAt(t);
            }
        }

        // 5. 対応づかなかった塊は、新しいトラックにする（速度は0）
        for (int b = 0; b < blobs.Count; b++)
        {
            if (blobMatched[b]) continue;
            _tracks.Add(new TrackedBlob
            {
                Id = _newId(),
                PositionMm = blobs[b].Center,
                VelocityMm = Vector2.zero,
                LastSeenTime = time,
                MissedScans = 0,
                IsNew = true,
            });
        }
    }

    private static int CompareByDistance(Pair a, Pair b)
    {
        return a.Distance.CompareTo(b.Distance);
    }
}
