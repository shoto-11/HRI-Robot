using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// FactoryHRISetup がエディタで書き込み、AGVSpawner がランタイムで読む。
/// </summary>
public class FactoryData : MonoBehaviour
{
    public List<Vector3> WorkbenchPositions = new List<Vector3>();
}
