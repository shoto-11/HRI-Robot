using System;
using UnityEngine;

namespace HRIRobot.Risk
{
    /// <summary>
    /// 危険度連動型eHMI: 車両ごとに各横断ラインに対するTTCと危険度スコアを算出する。
    /// 仕様書 3.2〜3.4 準拠。経路計画によるウェイポイント列に沿って走行する前提とし、
    /// 直線予測・ヨーレート予測は行わない。
    /// </summary>
    public class VehicleRiskCalculator : MonoBehaviour
    {
        [Serializable]
        public class CrossingLine
        {
            public string label;
            public Transform start;
            public Transform end;

            [Range(0f, 1f)] public float currentScore;
            public bool isVisible;
            [NonSerialized] public float currentTTC = Mathf.Infinity;
        }

        [Header("横断ライン（本環境では3か所）")]
        public CrossingLine[] crossingLines;

        [Header("経路計画によるウェイポイント列")]
        public Vector3[] plannedPath;

        [Header("歩行者参照")]
        public Transform pedestrian;

        [Header("現在速度 (m/s)")]
        public float currentSpeed;

        [Header("パラメータ")]
        [Tooltip("表示判定・スコア正規化の両方に使用。予備実験で調整。")]
        public float ttcMax = 4f;
        [Tooltip("TTC計算不可の車でも、この距離以内なら表示。")]
        public float displayDistanceMax = 25f;
        [Tooltip("ガンマ補正。危険域を早めに強調。")]
        public float gamma = 0.6f;
        [Tooltip("表示対象の車に限り、完全に透明にはしない下限値。")]
        public float scoreFloor = 0.08f;
        [Tooltip("停止判定のしきい値速度 (m/s)。")]
        public float stopSpeedThreshold = 0.3f;

        public bool IsStopped => currentSpeed < stopSpeedThreshold;

        void Update()
        {
            if (pedestrian == null || plannedPath == null || plannedPath.Length < 2 || crossingLines == null)
                return;

            float distToPedestrian = Vector3.Distance(transform.position, pedestrian.position);

            foreach (var line in crossingLines)
            {
                if (line?.start == null || line.end == null)
                    continue;

                float ttc = ComputeTTCAlongPath(line.start.position, line.end.position);
                line.currentTTC = ttc;

                bool ttcVisible = !float.IsInfinity(ttc) && ttc <= ttcMax;
                bool distanceVisible = distToPedestrian <= displayDistanceMax;
                line.isVisible = ttcVisible || distanceVisible;

                if (!line.isVisible)
                {
                    line.currentScore = 0f;
                    continue;
                }

                float r = float.IsInfinity(ttc)
                    ? 0f
                    : Mathf.Pow(Mathf.Clamp01(1f - ttc / ttcMax), gamma);

                line.currentScore = Mathf.Max(scoreFloor, r);
            }
        }

        /// <summary>
        /// 現在位置に最も近いウェイポイントから経路を先読みし、対象の横断ラインとの交点までの
        /// 経路長を積算してTTCを求める。交点が見つからなければ Infinity（無関係）を返す。
        /// </summary>
        float ComputeTTCAlongPath(Vector3 lineStart, Vector3 lineEnd)
        {
            int startIdx = FindClosestWaypointIndex(transform.position, plannedPath);
            float accumulatedDist = 0f;
            float speed = Mathf.Max(currentSpeed, 0.1f);

            for (int i = startIdx; i < plannedPath.Length - 1; i++)
            {
                Vector3 a = (i == startIdx) ? transform.position : plannedPath[i];
                Vector3 b = plannedPath[i + 1];

                if (SegmentsIntersect(a, b, lineStart, lineEnd, out Vector3 hit))
                {
                    accumulatedDist += Vector3.Distance(a, hit);
                    return accumulatedDist / speed;
                }

                accumulatedDist += Vector3.Distance(a, b);
                if (accumulatedDist / speed > ttcMax)
                    break;
            }

            return Mathf.Infinity;
        }

        bool SegmentsIntersect(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4, out Vector3 hit)
        {
            hit = Vector3.zero;
            Vector2 a = new Vector2(p1.x, p1.z), b = new Vector2(p2.x, p2.z);
            Vector2 c = new Vector2(p3.x, p3.z), d = new Vector2(p4.x, p4.z);

            Vector2 r = b - a, s = d - c;
            float denom = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(denom) < 0.0001f) return false;

            float t = ((c.x - a.x) * s.y - (c.y - a.y) * s.x) / denom;
            float u = ((c.x - a.x) * r.y - (c.y - a.y) * r.x) / denom;

            if (t >= 0f && t <= 1f && u >= 0f && u <= 1f)
            {
                Vector2 p = a + t * r;
                hit = new Vector3(p.x, p1.y, p.y);
                return true;
            }
            return false;
        }

        static int FindClosestWaypointIndex(Vector3 pos, Vector3[] path)
        {
            int best = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < path.Length; i++)
            {
                float d = Vector3.Distance(pos, path[i]);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }
    }
}
