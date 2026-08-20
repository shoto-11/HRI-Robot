using UnityEngine;

public enum AGVPhase
{
    MovingToPickup,
    DwellAtPickup,
    MovingToDrop,
    DwellAtDrop,
}

[System.Serializable]
public class AGVMissionPlan
{
    public Vector3[] pathToPickup;
    public Vector3[] pathToDrop;
    public float dwellDurationAtPickup = 2.0f;
    public float dwellDurationAtDrop = 2.0f;
    public Transform Box;
    public Vector3 PlacePos;
}
