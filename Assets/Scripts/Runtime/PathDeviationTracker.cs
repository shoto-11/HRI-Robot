using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>参加者の移動経路を記録し、走行距離（水平）を計測する。</summary>
[DisallowMultipleComponent]
public class PathDeviationTracker : MonoBehaviour
{
    public Vector3[] optimalPathWaypoints =
    {
        new Vector3(22f, 0.2f, 9f),
        new Vector3(9f, 0.2f, 58f),
    };

    [SerializeField] float recordInterval = 0.5f;
    [SerializeField] bool showRuntimePath = false;

    public float TotalPathLength { get; private set; }
    public bool IsTracking { get; private set; }
    public List<Vector3> RecordedPath { get; } = new();
    public List<PathSample> Samples { get; } = new();

    public readonly struct PathSample
    {
        public readonly float Time;
        public readonly Vector3 Position;
        public PathSample(float time, Vector3 position) { Time = time; Position = position; }
    }

    Transform _player;
    Coroutine _recordRoutine;
    LineRenderer _pathLine;
    Vector3? _lastSamplePos;

    void Start()
    {
        var xrOrigin = GameObject.Find("XR Origin");
        _player = xrOrigin?.transform ?? Camera.main?.transform;
        RecalculatePath();
    }

    public void StartTracking()
    {
        ResetTracker();
        IsTracking = true;
        _recordRoutine = StartCoroutine(RecordLoop());
    }

    public void StopTracking()
    {
        if (!IsTracking) return;
        if (_recordRoutine != null) StopCoroutine(_recordRoutine);
        IsTracking = false;
    }

    public void ResetTracker()
    {
        StopTracking();
        TotalPathLength = 0f;
        _lastSamplePos = null;
        RecordedPath.Clear();
        Samples.Clear();
    }

    [ContextMenu("NavMesh から経路を自動計算")]
    public void RecalculatePath()
    {
        if (optimalPathWaypoints == null || optimalPathWaypoints.Length < 2) return;
        Vector3 from = optimalPathWaypoints[0];
        Vector3 to = optimalPathWaypoints[optimalPathWaypoints.Length - 1];

        if (!NavMesh.SamplePosition(from, out var a, 4f, NavMesh.AllAreas)) return;
        if (!NavMesh.SamplePosition(to, out var b, 4f, NavMesh.AllAreas)) return;
        var path = new NavMeshPath();
        if (NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.corners.Length >= 2)
            optimalPathWaypoints = path.corners;
        UpdateRuntimePathLine();
    }

    void UpdateRuntimePathLine()
    {
        if (!showRuntimePath || optimalPathWaypoints == null || optimalPathWaypoints.Length < 2)
        {
            if (_pathLine != null) _pathLine.enabled = false;
            return;
        }
        if (_pathLine == null)
        {
            var go = new GameObject("OptimalPathLine");
            go.transform.SetParent(transform, false);
            _pathLine = go.AddComponent<LineRenderer>();
            _pathLine.useWorldSpace = true;
            var shader = Shader.Find("Sprites/Default");
            if (shader != null) _pathLine.material = new Material(shader);
        }
        _pathLine.enabled = true;
        _pathLine.startWidth = 0.06f;
        _pathLine.endWidth = 0.04f;
        _pathLine.positionCount = optimalPathWaypoints.Length;
        _pathLine.SetPositions(optimalPathWaypoints);
    }

    IEnumerator RecordLoop()
    {
        float start = Time.time;
        while (IsTracking)
        {
            if (_player != null)
            {
                Vector3 pos = _player.position;
                float t = Time.time - start;
                RecordedPath.Add(pos);
                Samples.Add(new PathSample(t, pos));
                if (_lastSamplePos.HasValue)
                {
                    Vector3 a = _lastSamplePos.Value; a.y = 0f;
                    Vector3 b = pos; b.y = 0f;
                    TotalPathLength += Vector3.Distance(a, b);
                }
                _lastSamplePos = pos;
            }
            yield return new WaitForSeconds(recordInterval);
        }
    }

    public List<PathSample> CopySamples() => new(Samples);
}
