using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>プレイヤーと AGV のニアミス（AGV 床面 1.0 m 四方＋プレイヤー半径）をカウントする。</summary>
[DisallowMultipleComponent]
public class CollisionCounter : MonoBehaviour
{
    public readonly struct CollisionEvent
    {
        public readonly float Time;
        public readonly int RobotId;
        public readonly Vector3 Position;
        public CollisionEvent(float time, int robotId, Vector3 position)
        {
            Time = time;
            RobotId = robotId;
            Position = position;
        }
    }

    [Tooltip("AGV 床面の一辺（m）。半径 = 半分。既定は FactoryLayout.AgvFootprintM と一致。")]
    [SerializeField] float agvFootprintM = FactoryLayout.AgvFootprintM;
    [SerializeField] float cooldownPerAgv = 1.5f;
    [SerializeField] float bodyHeightBelowHead = 1.75f;
    [SerializeField] float bodyRadius = 0.3f;
    [SerializeField] Transform playerHeadTransform;

    public int Count { get; private set; }
    public IReadOnlyList<CollisionEvent> Events => _events;

    readonly Dictionary<int, float> _lastHitTime = new();
    readonly List<CollisionEvent> _events = new();
    Transform _xrOrigin;
    CharacterController _characterController;

    void Update()
    {
        if (MeasurementHub.Instance == null || MeasurementHub.Instance.Timer == null || !MeasurementHub.Instance.Timer.IsRunning)
            return;
        if (!TryGetPlayerBodyCapsule(out Vector3 bottom, out Vector3 top, out float capsuleRadius))
            return;

        float now = Time.time;
        float agvRadius = agvFootprintM * 0.5f;
        float hitThreshold = agvRadius + capsuleRadius;

        foreach (var agent in FindObjectsByType<AGVAgent>(FindObjectsSortMode.None))
        {
            Vector3 p = agent.transform.position;
            float dist = DistancePointToSegment(p, bottom, top);
            if (dist > hitThreshold) continue;

            int id = agent.GetInstanceID();
            if (_lastHitTime.TryGetValue(id, out float last) && now - last < cooldownPerAgv) continue;

            _lastHitTime[id] = now;
            Count++;
            int robotId = agent.Index + 1;
            float t = MeasurementHub.Instance.Timer.ElapsedTime;
            _events.Add(new CollisionEvent(t, robotId, p));
            Debug.Log($"[衝突] 人間と AGV {agent.name} (ID={robotId}) が接触（累計={Count} 距離={dist:F2}m t={t:F2}s）");
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "collision_log.txt");
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{agent.name}\t{dist:F3}\n");
            }
            catch { /* ignore IO */ }
        }
    }

    bool TryGetPlayerBodyCapsule(out Vector3 bottom, out Vector3 top, out float radius)
    {
        var head = ResolveHead();
        if (head == null)
        {
            bottom = top = Vector3.zero;
            radius = bodyRadius;
            return false;
        }

        if (_xrOrigin == null)
        {
            _xrOrigin = GameObject.Find("XR Origin")?.transform;
            if (_xrOrigin != null)
                _characterController = _xrOrigin.GetComponent<CharacterController>();
        }

        if (_characterController != null && _xrOrigin != null)
        {
            Vector3 center = _xrOrigin.TransformPoint(_characterController.center);
            float half = Mathf.Max(0f, _characterController.height * 0.5f - _characterController.radius);
            bottom = center - Vector3.up * half;
            top = center + Vector3.up * half;
            radius = _characterController.radius;
            return true;
        }

        Vector3 headPos = head.position;
        float floorY = _xrOrigin != null ? _xrOrigin.position.y : headPos.y - bodyHeightBelowHead;
        top = headPos;
        bottom = new Vector3(headPos.x, floorY, headPos.z);
        radius = bodyRadius;
        return true;
    }

    Transform ResolveHead()
    {
        if (playerHeadTransform != null) return playerHeadTransform;
        if (Camera.main != null) { playerHeadTransform = Camera.main.transform; return playerHeadTransform; }
        var xr = GameObject.Find("XR Origin");
        if (xr != null) { playerHeadTransform = xr.transform; return playerHeadTransform; }
        return null;
    }

    static float DistancePointToSegment(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float lenSq = ab.sqrMagnitude;
        if (lenSq < 1e-8f) return Vector3.Distance(point, a);
        float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / lenSq);
        return Vector3.Distance(point, a + ab * t);
    }

    public void ResetCount()
    {
        Count = 0;
        _lastHitTime.Clear();
        _events.Clear();
    }

    public string FormatEvents()
    {
        if (_events.Count == 0) return "()";
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("(");
        for (int i = 0; i < _events.Count; i++)
        {
            if (i > 0) sb.Append(",");
            var e = _events[i];
            sb.Append("(")
                .Append(e.Time.ToString("F2", inv)).Append(",")
                .Append(e.RobotId).Append(",")
                .Append(e.Position.x.ToString("F3", inv)).Append(",")
                .Append(e.Position.y.ToString("F3", inv)).Append(",")
                .Append(e.Position.z.ToString("F3", inv))
                .Append(")");
        }
        sb.Append(")");
        return sb.ToString();
    }
}
