using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>流体に渡す1件の入力。位置・速度はUV、半径は高さを1としたUV</summary>
public readonly struct VelocityInputData
{
    public VelocityInputData(int id, Vector2 position, Vector2 velocity, float radius)
    {
        Id = id;
        PositionUv = position;
        VelocityUv = velocity;
        Radius = radius;
    }

    public readonly int Id;
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

/// <summary>
/// RpLidarC1 の1周ごとの点群から、手のひらなどの塊を検出・追跡し、
/// 流体への入力（VelocityInputData）と、触れた点（PointInputData）として取り出せる形にする。
/// センサーなしで開発できるよう、ポインター（マウス・タッチ）とBoidsの入力も、ここでまとめて返す。
/// ポインターも、センサーと同じ TouchTracker（ID付け・速度）と BuildOutputs（出力への変換）を通る
/// </summary>
[DefaultExecutionOrder(-100)] // ポインターの追跡を、他のスクリプトの Update より先に済ませる
public class SensorInputSource : MonoBehaviour
{
    /// <summary>入力1系統（センサー、またはポインター）の追跡と、取り出し待ちの出力</summary>
    private sealed class TouchSource
    {
        public TouchSource(Func<int> newId) { Tracker = new TouchTracker(newId); }
        public readonly TouchTracker Tracker;
        public readonly List<VelocityInputData> Velocities = new List<VelocityInputData>();
        public readonly List<PointInputData> Points = new List<PointInputData>();
    }

    [SerializeField] private RpLidarC1 lidar; // 未割り当てなら、センサーの入力は入らない（ポインターとBoidsだけ動く）

    [Header("Boids")]
    [SerializeField] private float boidsRadiusPx;
    [SerializeField] private Boids boids;

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
    [SerializeField] private float graceSeconds = 0.3f; // 見えなくなってから、同じIDとして扱う時間[秒]。超えたあとに触れ直すと新しいID（センサーもポインターも共通）
    [SerializeField, Range(0.05f, 1f)] private float velocitySmoothing = 0.3f; // 1で平滑化なし。小さいほどなめらか

    [Header("Output")]
    [SerializeField] private float minSpeedMmPerSec = 250f;
    [SerializeField] private float radiusMm = 100f;

    [Header("Debug")]
    [SerializeField] private bool logTouches;

    private BackgroundModel _background;
    private TouchSource _sensor;
    private TouchSource _pointer;
    private const int NoStarId = -1; // Boids の入力。星に結びつかないので、どの星のIDとも一致しない値
    private int _nextTouchId = 1; // センサーとポインターで共通。IDが重ならない
    private readonly List<Blob> _pointerBlobs = new List<Blob>();
    private readonly List<BoardPoint> _boardPoints = new List<BoardPoint>();
    private readonly List<Blob> _blobs = new List<Blob>();
    private int _foregroundCount; // 直近の周で、背景差分を通った点の数（ログ用）
    private float _nextLogTime;

    private void Awake()
    {
        _sensor = new TouchSource(NewTouchId);
        _pointer = new TouchSource(NewTouchId);
    }

    private int NewTouchId() { return _nextTouchId++; }

    private void Start()
    {
        if (lidar == null)
        {
            Debug.LogWarning("[SensorInputSource] lidar が未割り当てです。ポインターとBoidsの入力だけ動きます");
            return;
        }
        _background = new BackgroundModel(backgroundBinDeg);
        lidar.ScanReceived += HandleScan;
    }

    private void Update()
    {
        UpdatePointer();

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

    /// <summary>
    /// 流体用。Boids・ポインター・センサーの入力を1つの配列にまとめて返す。
    /// Boidsは、ポインターやセンサーの状態に関わらず常に入る
    /// </summary>
    public bool TryGetVelocityInput(out VelocityInputData[] input)
    {
        input = default;
        if (Time.deltaTime <= 0f) { return false; }

        // Boidsの入力。半径はpx→UV(高さ基準)
        var (pos, vel) = boids.GetInputs();
        var boidsRadius = boidsRadiusPx / Screen.height;
        var pointerInputs = _pointer.Velocities;

        // out 引数は条件式の中だと代入が保証されないので、先に null で用意する
        VelocityInputData[] sensorInput = null;
        var hasSensor = TryGetSensorInput(out sensorInput);
        var sensorCount = hasSensor ? sensorInput.Length : 0;

        input = new VelocityInputData[pos.Length + pointerInputs.Count + sensorCount];
        for (int i = 0; i < pos.Length; i++)
        {
            input[i] = new VelocityInputData(NoStarId, pos[i], vel[i], boidsRadius);
        }
        var next = pos.Length;
        pointerInputs.CopyTo(input, next);
        next += pointerInputs.Count;
        if (hasSensor) { sensorInput.CopyTo(input, next); }
        return input.Length > 0;
    }

    /// <summary>
    /// 星用。新しく触れた点を取り出す（1フレームに1回呼ぶ）。
    /// センサーの点とポインターの点を合わせて返し、取り出した点は空にする
    /// </summary>
    public bool TryGetPointInput(out PointInputData[] input)
    {
        input = default;

        // out 引数は条件式の中だと代入が保証されないので、先に null で用意する
        PointInputData[] sensorInput = null;
        var hasSensor = TryGetSensorPointInput(out sensorInput);
        var pointerPoints = _pointer.Points;
        if (!hasSensor && pointerPoints.Count == 0) { return false; }

        var sensorCount = hasSensor ? sensorInput.Length : 0;
        input = new PointInputData[sensorCount + pointerPoints.Count];
        if (hasSensor) { sensorInput.CopyTo(input, 0); }
        pointerPoints.CopyTo(input, sensorCount);
        pointerPoints.Clear();
        return true;
    }

    /// <summary>センサーの流体用入力。次の周が届くまでは、同じ値を毎フレーム返す</summary>
    private bool TryGetSensorInput(out VelocityInputData[] input)
    {
        input = default;
        if (lidar == null || lidar.State != RpLidarC1.LidarState.Running) { return false; }
        if (_background == null || _background.IsRecording) { return false; }
        if (_sensor.Velocities.Count == 0) { return false; }

        input = _sensor.Velocities.ToArray();
        return true;
    }

    /// <summary>センサーの星用入力。今回の周で新しく触れた点を返し、取り出した点は空にする</summary>
    private bool TryGetSensorPointInput(out PointInputData[] input)
    {
        input = default;
        if (_sensor.Points.Count == 0) { return false; }

        input = _sensor.Points.ToArray();
        _sensor.Points.Clear();
        return true;
    }

    /// <summary>
    /// ポインター（マウス・タッチ）の押している位置を、センサーの塊と同じ板座標[mm]にして、同じ追跡に通す。
    /// 押している間は塊が1つ、離すと塊なし。離してから graceSeconds 以内に押し直せば同じID
    /// </summary>
    private void UpdatePointer()
    {
        _pointerBlobs.Clear();
        if (Pointer.current != null && Pointer.current.press.isPressed)
        {
            var uv = Pointer.current.position.ReadValue();
            uv.x /= Screen.width;
            uv.y /= Screen.height;
            _pointerBlobs.Add(new Blob(ToBoardMm(uv), 0f, 1));
        }

        // ポインターは1つしかないので、距離で対応づける必要はない（素早く動かしても同じIDのまま）
        _pointer.Tracker.Update(_pointerBlobs, Time.realtimeSinceStartup, float.PositiveInfinity, graceSeconds, velocitySmoothing);
        BuildOutputs(_pointer);
    }

    private void StartBackgroundRecording()
    {
        _background.BeginRecording(backgroundScanCount);
        _sensor.Tracker.Clear();
        _sensor.Velocities.Clear();
        _sensor.Points.Clear();
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

        _sensor.Tracker.LogEvents = logTouches;
        _pointer.Tracker.LogEvents = logTouches;
        ToBoardPoints(points);
        BlobDetector.Detect(_boardPoints, blobGapBaseMm, blobGapFactor, minBlobPoints, minBlobWidthMm, _blobs);
        _sensor.Tracker.Update(_blobs, Time.realtimeSinceStartup, maxMatchMm, graceSeconds, velocitySmoothing);
        BuildOutputs(_sensor);

        if (logTouches && Time.realtimeSinceStartup >= _nextLogTime)
        {
            _nextLogTime = Time.realtimeSinceStartup + 1f;
            Debug.Log($"[SensorInputSource] points={points.Length} foreground={_foregroundCount} inBoard={_boardPoints.Count} blobs={_blobs.Count} tracks={_sensor.Tracker.Tracks.Count}");
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

    /// <summary>追跡中のトラックを、流体用の入力と星用の点にして、source に並べ直す。センサーもポインターも共通</summary>
    private void BuildOutputs(TouchSource source)
    {
        source.Velocities.Clear();
        source.Points.Clear(); // 取り出されなかった古い点は捨てる

        float radiusUv = radiusMm / boardHeightMm;
        foreach (var track in source.Tracker.Tracks)
        {
            if (track.MissedScans > 0) continue; // 最新の周で見えていない

            Vector2 positionUv = ToUv(track.PositionMm);

            if (track.IsNew)
            {
                source.Points.Add(new PointInputData(track.Id, positionUv));
                if (logTouches) { Debug.Log($"[SensorInputSource] touch id={track.Id} uv={positionUv}"); }
            }

            if (track.VelocityMm.magnitude >= minSpeedMmPerSec)
            {
                Vector2 velocityUv = new Vector2(
                    track.VelocityMm.x / boardWidthMm,
                    -track.VelocityMm.y / boardHeightMm);
                source.Velocities.Add(new VelocityInputData(track.Id, positionUv, velocityUv, radiusUv));
            }
        }
    }

    /// <summary>画面のUV（原点は左下） → 板座標[mm]。ToUv の逆</summary>
    private Vector2 ToBoardMm(Vector2 uv)
    {
        return new Vector2((uv.x - 0.5f) * boardWidthMm, (1f - uv.y) * boardHeightMm);
    }

    /// <summary>板座標[mm] → 画面のUV（原点は左下）</summary>
    private Vector2 ToUv(Vector2 boardMm)
    {
        return new Vector2(0.5f + boardMm.x / boardWidthMm, 1f - boardMm.y / boardHeightMm);
    }
}
