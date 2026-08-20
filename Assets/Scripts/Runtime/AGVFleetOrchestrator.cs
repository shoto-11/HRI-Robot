using System.Collections.Generic;
using UnityEngine;

/// <summary>全 AGV を 0.1 秒固定クロックで一括更新する。</summary>
[DisallowMultipleComponent]
public class AGVFleetOrchestrator : MonoBehaviour
{
    public static AGVFleetOrchestrator Instance { get; private set; }

    sealed class Runner
    {
        public AGVAgent Agent;
        public AGVMissionPlan Plan;
        public int HoldRemaining;
        public int PlanRetryCooldown;
        public bool Complete = true;
    }

    readonly List<Runner> _runners = new();
    float _accum;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void BeginSession(IEnumerable<AGVAgent> agents)
    {
        _runners.Clear();
        _accum = 0f;
        if (agents == null) return;
        foreach (var agent in agents)
        {
            if (agent == null) continue;
            _runners.Add(new Runner { Agent = agent, Complete = true });
        }
    }

    public void Unregister(AGVAgent agent)
    {
        for (int i = _runners.Count - 1; i >= 0; i--)
            if (_runners[i].Agent == agent)
                _runners.RemoveAt(i);
    }

    void Update()
    {
        if (_runners.Count == 0) return;
        _accum += Time.deltaTime;
        while (_accum >= AGVAgent.TrajectoryStepSeconds)
        {
            _accum -= AGVAgent.TrajectoryStepSeconds;
            StepAll();
        }
    }

    void StepAll()
    {
        for (int i = 0; i < _runners.Count; i++)
            StepRunner(_runners[i]);
    }

    void StepRunner(Runner r)
    {
        var agent = r.Agent;
        if (agent == null) return;

        if (r.Complete || r.Plan == null)
        {
            if (r.PlanRetryCooldown > 0)
            {
                r.PlanRetryCooldown--;
                return;
            }
            if (!agent.TryPlanMission(out var plan))
            {
                r.PlanRetryCooldown = 5;
                return;
            }
            r.Plan = plan;
            r.HoldRemaining = 0;
            r.Complete = false;
        }

        if (r.HoldRemaining > 0)
        {
            r.HoldRemaining--;
            agent.TickMotion(AGVAgent.TrajectoryStepSeconds);
            return;
        }

        switch (agent.currentPhase)
        {
            case AGVPhase.MovingToPickup:
                agent.TickMotion(AGVAgent.TrajectoryStepSeconds);
                if (agent.HasArrivedCurrentLeg())
                {
                    agent.BeginDwellPickup();
                    r.HoldRemaining = HoldFrames(r.Plan.dwellDurationAtPickup);
                }
                break;
            case AGVPhase.DwellAtPickup:
                agent.BeginMoveToDrop();
                break;
            case AGVPhase.MovingToDrop:
                agent.TickMotion(AGVAgent.TrajectoryStepSeconds);
                if (agent.HasArrivedCurrentLeg())
                {
                    agent.BeginDwellDrop();
                    r.HoldRemaining = HoldFrames(r.Plan.dwellDurationAtDrop);
                }
                break;
            case AGVPhase.DwellAtDrop:
                agent.EndMission();
                r.Complete = true;
                r.Plan = null;
                break;
        }
    }

    static int HoldFrames(float seconds) =>
        Mathf.Max(1, Mathf.CeilToInt(seconds / AGVAgent.TrajectoryStepSeconds));
}
