using System;
using System.IO.Ports;
using System.Threading;
using System.Collections.Generic;
using UnityEngine;

public class RpLidarC1 : MonoBehaviour
{
    private const int ReadTimeoutMs = 200;
    private const int DescriptorTimeoutMs = 2000;
    private const int PacketSize = 5;
    private const byte Sync = 0xA5;
    private const byte Stop = 0x25;
    private const byte Scan = 0x20;
    private static readonly byte[] ExpectedDescriptor = { 0xA5, 0x5A, 0x05, 0x00, 0x00, 0x40, 0x81 };

    // 先頭を Stopped にする（State を代入するまでの初期値が、先頭の値になるため）
    public enum LidarState
    {
        Stopped,
        Idling,
        Running,
        Stalled,
        Failed,
    }

    [SerializeField] private string portName = "COM3";
    [SerializeField] private int baudRate = 460800;
    [SerializeField] private float firstScanTimeoutSec = 5f;
    [SerializeField] private float scanTimeoutSec = 1f;
    [SerializeField] private float stalledToFailedSec = 60f;

    public LidarState State { get; private set; }
    public event Action<LidarPoint[]> ScanReceived;

    private SerialPort _port;
    private Thread _readThread;
    private volatile bool _stopRequested;
    private volatile bool _readerEnded;
    private LidarPoint[] _latestScan;
    private float _stateSince;
    private float _lastScanTime;

    private void Start()
    {
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
        _port.ReadTimeout = ReadTimeoutMs;

        _readThread = new Thread(Reader) { IsBackground = true, Name = "RpLidarC1 Reader" };
        _readThread.Start();
        SetState(LidarState.Idling);
    }

    private void Update()
    {
        if (State == LidarState.Stopped || State == LidarState.Failed) return;

        float now = Time.realtimeSinceStartup;

        LidarPoint[] scan = Interlocked.Exchange(ref _latestScan, null);
        if (scan != null)
        {
            _lastScanTime = now;
            if (State != LidarState.Running) SetState(LidarState.Running);
            ScanReceived?.Invoke(scan);
            return;
        }

        if (_readerEnded)
        {
            SetState(LidarState.Failed);
            return;
        }

        switch (State)
        {
            case LidarState.Idling:
                if (now - _stateSince > firstScanTimeoutSec) SetState(LidarState.Stalled);
                break;
            case LidarState.Running:
                if (now - _lastScanTime > scanTimeoutSec) SetState(LidarState.Stalled);
                break;
            case LidarState.Stalled:
                if (now - _stateSince > stalledToFailedSec)
                {
                    _stopRequested = true;
                    SetState(LidarState.Failed);
                }
                break;
        }
    }

    private void OnDestroy()
    {
        _stopRequested = true;
        _readThread?.Join(1000);
    }

    private void SetState(LidarState next)
    {
        Debug.Log($"[RpLidarC1] {State} → {next}");
        State = next;
        _stateSince = Time.realtimeSinceStartup;
    }

    private void Reader()
    {
        try
        {
            Open();

            byte[] descriptor = new byte[7];
            ReadExactly(descriptor, 0, descriptor.Length, DescriptorTimeoutMs);

            bool error = false;

            if (descriptor.Length != ExpectedDescriptor.Length) error = true;
            for (int i = 0; i < descriptor.Length; i++)
            {
                if (descriptor[i] != ExpectedDescriptor[i]) error = true;
            }

            if (error)
            {
                Debug.LogError("[RpLidarC1] 想定外の descriptor: " + BitConverter.ToString(descriptor));
                return;
            }

            Debug.Log("[RpLidarC1] descriptor OK");

            ReadScan();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogError("[RpLidarC1] 読み込みスレッドで例外: " + e);
        }
        finally
        {
            Close();
            _readerEnded = true;
        }
    }

    private void Open()
    {
        _port.Open();

        Send(Sync, Stop);
        Thread.Sleep(100);
        _port.DiscardInBuffer();
        Send(Sync, Scan);
    }

    private void Close()
    {
        if (!_port.IsOpen) return;
        try { Send(Sync, Stop); } catch (Exception e) { Debug.LogException(e); }
        try { _port.Close(); } catch (Exception e) { Debug.LogException(e); }
    }

    private void Send(params byte[] bytes)
    {
        _port.Write(bytes, 0, bytes.Length);
    }

    private void ReadScan()
    {
        byte[] packet = new byte[PacketSize];
        var points = new List<LidarPoint>();
        bool inRevolution = false; // 最初の S=1 を見つけるまでは false

        // データが来ない間の判定は Update の状態遷移が受け持つので、ここでは待ち続ける
        ReadExactly(packet, 0, PacketSize, Timeout.Infinite);

        while (!_stopRequested)
        {
            if (IsValidPacket(packet))
            {
                bool isStart = (packet[0] & 1) == 1;
                if (isStart)
                {
                    if (inRevolution)
                    {
                        var array = points.ToArray();
                        Interlocked.Exchange(ref _latestScan, array);
                        points.Clear();
                    }
                    inRevolution = true;
                }

                if (inRevolution) { points.Add(Decode(packet)); }

                ReadExactly(packet, 0, PacketSize, Timeout.Infinite);
            }
            else
            {
                // 区切りがずれている。1バイト捨てて詰め、末尾に1バイト足して再検査する
                Array.Copy(packet, 1, packet, 0, PacketSize - 1);
                ReadExactly(packet, PacketSize - 1, 1, Timeout.Infinite);
            }
        }
    }

    private static bool IsValidPacket(byte[] p)
    {
        bool startBit = (p[0] & 1) == 1;
        bool inverseBit = ((p[0] >> 1) & 1) == 1;
        bool checkBit = (p[1] & 1) == 1;
        return startBit != inverseBit && checkBit;
    }

    private static LidarPoint Decode(byte[] p)
    {
        float quality = p[0] >> 2;
        float angle = ((p[2] << 7) | (p[1] >> 1)) / 64f;
        float distance = (p[3] | (p[4] << 8)) / 4f;
        return new LidarPoint(distance: distance, angle: angle, quality: quality);
    }

    private void ReadExactly(byte[] buffer, int offset, int count, int timeoutMs)
    {
        int totalRead = 0;
        var stopWatch = System.Diagnostics.Stopwatch.StartNew();

        while (totalRead < count)
        {
            if (_stopRequested) { throw new OperationCanceledException(); }
            if (timeoutMs != Timeout.Infinite && stopWatch.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException();
            try { totalRead += _port.Read(buffer, offset + totalRead, count - totalRead); }
            catch (TimeoutException) { }
        }
    }
}
