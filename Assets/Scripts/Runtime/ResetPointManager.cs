using System.Collections.Generic;
using UnityEngine;

namespace HRIRobot.Experiment
{
    /// <summary>
    /// リセットポイント（仕様書 2.1）: 歩行者がこの地点を通過すると、登録済みの周辺車両を
    /// 初期位置・初期速度に戻し、試行間の再現性を確保する。
    /// </summary>
    public class ResetPointManager : MonoBehaviour
    {
        [System.Serializable]
        public class VehicleState
        {
            public Transform vehicle;
            [HideInInspector] public Vector3 initialPosition;
            [HideInInspector] public Quaternion initialRotation;
            [HideInInspector] public float initialSpeed;
        }

        [Header("この地点で初期状態に戻す車両")]
        public List<VehicleState> managedVehicles = new List<VehicleState>();

        [Header("歩行者判定用（未指定ならタグ 'Player' を使用）")]
        public Transform pedestrian;
        public float triggerRadius = 1.0f;
        public bool resetOnce = true;

        bool hasReset;

        void Start()
        {
            foreach (var v in managedVehicles)
            {
                if (v.vehicle == null) continue;
                v.initialPosition = v.vehicle.position;
                v.initialRotation = v.vehicle.rotation;
                var risk = v.vehicle.GetComponent<HRIRobot.Risk.VehicleRiskCalculator>();
                v.initialSpeed = risk != null ? risk.currentSpeed : 0f;
            }
        }

        void Update()
        {
            if (resetOnce && hasReset) return;

            var target = pedestrian;
            if (target == null)
            {
                var go = GameObject.FindGameObjectWithTag("Player");
                if (go != null) target = go.transform;
            }
            if (target == null) return;

            if (Vector3.Distance(transform.position, target.position) <= triggerRadius)
            {
                ResetVehicles();
                hasReset = true;
            }
        }

        [ContextMenu("Reset Vehicles Now")]
        public void ResetVehicles()
        {
            foreach (var v in managedVehicles)
            {
                if (v.vehicle == null) continue;
                v.vehicle.SetPositionAndRotation(v.initialPosition, v.initialRotation);
                var risk = v.vehicle.GetComponent<HRIRobot.Risk.VehicleRiskCalculator>();
                if (risk != null) risk.currentSpeed = v.initialSpeed;
            }
        }

        public void ArmForNextTrial() => hasReset = false;
    }
}
