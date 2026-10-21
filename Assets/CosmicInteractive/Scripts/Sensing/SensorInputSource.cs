using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// RpLidarC1 の1周ごとの点群から、手のひらなどの塊を検出・追跡し、
/// 流体への入力（VelocityInputData）と、触れた点（PointInputData）として取り出せる形にする
/// </summary>
public class SensorInputSource : MonoBehaviour
{
    [SerializeField] private RpLidarC1 lidar;

    [Header("Board（キャリブレーションができたら、この値を書き込む）")]
    [SerializeField] private float angleOffsetDeg;
    [SerializeField] private float boardWidthMm = 6000f;
    [SerializeField] private float boardHeightMm = 1200f;

    [Header("Background")]
    [SerializeField] private float backgroundBinDeg = 1f;
    [SerializeField] private int backgroundScanCount = 20;
    [SerializeField] private float backgroundMarginMm = 50f;

    [Header("Blob")]
    [SerializeField] private float blobGapBaseMm = 40f;
    [SerializeField] private float blobGapFactor = 1.5f;
    [SerializeField] private int minBlobPoints = 2;
    [SerializeField] private float minBlobWidthMm = 35f;

    [Header("Tracking")]
    [SerializeField] private float maxMatchMm = 300f;
    [SerializeField] private int graceScans = 2;
    [SerializeField, Range(0.05f, 1f)] private float velocitySmoothing = 0.3f; // 1で平滑化なし。小さいほどなめらか

    [Header("Output")]
    [SerializeField] private float minSpeedMmPerSec = 250f;
    [SerializeField] private float radiusMm = 100f;

    [Header("Debug")]
    [SerializeField] private bool logTouches;

    private BackgroundModel _background;
    private readonly TouchTracker _tracker = new TouchTracker();
    private readonly List<BoardPoint> _boardPoints = new List<BoardPoint>();
    private readonly List<Blob> _blobs = new List<Blob>();
    private readonly List<VelocityInputData> _velocityInputs = new List<VelocityInputData>();
    private readonly List<PointInputData> _pendingPoints = new List<PointInputData>();
    private int _foregroundCount; // 直近の周で、背景差分を通った点の数（ログ用）
    private float _nextLogTime;

    private void Start()
    {
        if (lidar == null)
        {
            Debug.LogError("[SensorInputSource] lidar を割り当ててください");
            return;
        }
        _background = new BackgroundModel(backgroundBinDeg);
        lidar.ScanReceived += HandleScan;
    }

    private void Update()
    {
        if (_background == null) return;

        // 人が黒板の前にいる状態で背景を記録してしまったとき、B キーでやり直す
        if (Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame)
        {
            StartBackgroundRecording();
        }
    }

    private void OnDestroy()
    {
        if (lidar != null) { lidar.ScanReceived -= HandleScan; }
    }

    /// <summary>流体用。次の周が届くまでは、同じ値を毎フレーム返す</summary>
    public bool TryGetInput(out VelocityInputData[] input)
    {
        input = default;
        if (lidar == null || lidar.State != RpLidarC1.LidarState.Running) { return false; }
        if (_background == null || _background.IsRecording) { return false; }
        if (_velocityInputs.Count == 0) { return false; }

        input = _velocityInputs.ToArray();
        return true;
    }

    /// <summary>星用。今回の周で新しく触れた点を返し、取り出した点は空にする</summary>
    public bool TryGetPointInput(out PointInputData[] input)
    {
        input = default;
        if (_pendingPoints.Count == 0) { return false; }

        input = _pendingPoints.ToArray();
        _pendingPoints.Clear();
        return true;
    }

    private void StartBackgroundRecording()
    {
        _background.BeginRecording(backgroundScanCount);
        _tracker.Clear();
        _velocityInputs.Clear();
        _pendingPoints.Clear();
        Debug.Log("[SensorInputSource] 背景の記録を開始します。何も触らないでください");
    }

    private void HandleScan(LidarPoint[] points)
    {
        // 最初の周が届いたとき、背景を自動で記録する
        if (!_background.HasBackground && !_background.IsRecording) { StartBackgroundRecording(); }

        if (_background.IsRecording)
        {
            _background.AddScan(points);
            return;
        }

        _tracker.LogEvents = logTouches;
        ToBoardPoints(points);
        BlobDetector.Detect(_boardPoints, blobGapBaseMm, blobGapFactor, minBlobPoints, minBlobWidthMm, _blobs);
        _tracker.Update(_blobs, Time.realtimeSinceStartup, maxMatchMm, graceScans, velocitySmoothing);
        BuildOutputs();

        if (logTouches && Time.realtimeSinceStartup >= _nextLogTime)
        {
            _nextLogTime = Time.realtimeSinceStartup + 1f;
            Debug.Log($"[SensorInputSource] points={points.Length} foreground={_foregroundCount} inBoard={_boardPoints.Count} blobs={_blobs.Count} tracks={_tracker.Tracks.Count}");
        }
    }

    /// <summary>
    /// 点を板座標（mm。x右・y下、原点はセンサーの回転中心）に変換し、黒板の範囲内の前景だけを _boardPoints に残す。
    /// 並びは入力のまま（角度の昇順）
    /// </summary>
    private void ToBoardPoints(LidarPoint[] points)
    {
        _boardPoints.Clear();
        _foregroundCount = 0;
        float halfWidth = boardWidthMm * 0.5f;

        foreach (var p in points)
        {
            if (p.Quality <= 0f || p.Distance <= 0f) continue;
            if (!_background.IsForeground(p, backgroundMarginMm)) continue;
            _foregroundCount++;

            float a = (p.Angle - angleOffsetDeg) * Mathf.Deg2Rad;
            float x = p.Distance * Mathf.Sin(a);
            float y = -p.Distance * Mathf.Cos(a);
            if (x < -halfWidth || x > halfWidth || y < 0f || y > boardHeightMm) continue;

            _boardPoints.Add(new BoardPoint(new Vector2(x, y), p.Distance, p.Angle * Mathf.Deg2Rad));
        }
    }

    private void BuildOutputs()
    {
        _velocityInputs.Clear();
        _pendingPoints.Clear(); // 取り出されなかった古い点は捨てる

        float radiusUv = radiusMm / boardHeightMm;
        foreach (var track in _tracker.Tracks)
        {
            if (track.MissedScans > 0) continue; // 最新の周で見えていない

            Vector2 positionUv = ToUv(track.PositionMm);

            if (track.IsNew)
            {
                _pendingPoints.Add(new PointInputData(track.Id, positionUv));
                if (logTouches) { Debug.Log($"[SensorInputSource] touch id={track.Id} uv={positionUv}"); }
            }

            if (track.VelocityMm.magnitude >= minSpeedMmPerSec)
            {
                Vector2 velocityUv = new Vector2(
                    track.VelocityMm.x / boardWidthMm,
                    -track.VelocityMm.y / boardHeightMm);
                _velocityInputs.Add(new VelocityInputData(positionUv, velocityUv, radiusUv));
            }
        }
    }

    /// <summary>板座標[mm] → 画面のUV（原点は左下）</summary>
    private Vector2 ToUv(Vector2 boardMm)
    {
        return new Vector2(0.5f + boardMm.x / boardWidthMm, 1f - boardMm.y / boardHeightMm);
    }
}
