using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 工場レイアウトの共通定数。Station A/B、ワークベンチ、ピックアップ／ドロップゾーン。
/// Unity Factory 未導入時も、この座標で手続き生成する。
/// </summary>
public static class FactoryLayout
{
    public const float BuildingXMin = 6.5f;
    public const float BuildingXMax = 41.0f;
    public const float BuildingZMin = 0.0f;
    public const float BuildingZMax = 64.0f;
    public const float CenterX = 24.5f;
    public const float FloorY = 0f;
    public const float AgvFloorY = 0.15f;
    public const float NavSampleY = 0.2f;
    /// <summary>AGV 床面投影の一辺（m）。ニアミス判定の AGV 半径 = この値の半分。</summary>
    public const float AgvFootprintM = 1.0f;

    /// <summary>eHMI 危険度計算用パラメータ。</summary>
    public const float AgvMaxSpeedMps = 2.0f;
    public const float PedestrianSpeedMps = 1.4f;
    public const float EhmiTtcMaxSeconds = 4.0f;
    /// <summary>近接表示上限 (m): (AGV 最大速度 + 歩行速度) × TTC 上限。</summary>
    public const float DisplayDistanceMax = (AgvMaxSpeedMps + PedestrianSpeedMps) * EhmiTtcMaxSeconds;

    public static readonly Vector3 StationA = new Vector3(22f, 0.2f, 9f);
    public static readonly Vector3 StationB = new Vector3(9f, 0.2f, 58f);

    /// <summary>Station A マーカーから通路方向へずらした参加者スポーン（ポール中心を避ける）。</summary>
    public const float PlayerSpawnForwardOffset = 2.0f;

    public static Vector3 PlayerSpawnPosition
    {
        get
        {
            Vector3 forward = SpawnRotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            forward.Normalize();
            return new Vector3(StationA.x, FloorY, StationA.z) + forward * PlayerSpawnForwardOffset;
        }
    }

    /// <summary>Station A から最初の通路ウェイポイントへ向く標準開始向き。</summary>
    public static Quaternion SpawnRotation
    {
        get
        {
            Vector3 next = new Vector3(21.475f, StationA.y, 12f);
            Vector3 fwd = next - StationA;
            fwd.y = 0f;
            return fwd.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(fwd.normalized, Vector3.up)
                : Quaternion.identity;
        }
    }

    public static readonly Vector3[] WorkbenchPositions =
    {
        new Vector3(16.28f, 0f, 12f),
        new Vector3(28.8f,  0f, 12f),
        new Vector3(16.28f, 0f, 30f),
        new Vector3(28.8f,  0f, 30f),
        new Vector3(16.28f, 0f, 48f),
        new Vector3(28.8f,  0f, 48f),
    };

    public static readonly Vector3[] FloorPickupCenters =
    {
        new Vector3(20.0f, 0.30f,  8.0f),
        new Vector3(11.0f, 0.30f, 20.0f),
        new Vector3(11.0f, 0.30f, 44.0f),
        new Vector3(32.0f, 0.30f, 20.0f),
        new Vector3(32.0f, 0.30f, 44.0f),
        new Vector3(22.0f, 0.30f, 52.0f),
    };

    public enum CargoZoneKind { Pickup, Drop }

    public readonly struct CargoZone
    {
        public readonly string Id;
        public readonly Vector3 Center;
        public readonly float HalfX;
        public readonly float HalfZ;
        public readonly CargoZoneKind Kind;

        public CargoZone(string id, Vector3 center, float halfX, float halfZ, CargoZoneKind kind)
        {
            Id = id; Center = center; HalfX = halfX; HalfZ = halfZ; Kind = kind;
        }

        public Vector3 FloorCenter => new Vector3(Center.x, NavSampleY, Center.z);
    }

    static CargoZone[] _pickupZones;
    static CargoZone[] _dropZones;

    public static IReadOnlyList<CargoZone> PickupZones
    {
        get
        {
            if (_pickupZones == null) _pickupZones = BuildPickupZones();
            return _pickupZones;
        }
    }

    public static IReadOnlyList<CargoZone> DropZones
    {
        get
        {
            if (_dropZones == null) _dropZones = BuildDropZones();
            return _dropZones;
        }
    }

    static CargoZone[] BuildPickupZones()
    {
        var list = new List<CargoZone>(FloorPickupCenters.Length + WorkbenchPositions.Length);
        int i = 0;
        foreach (var wb in WorkbenchPositions)
        {
            bool isLeft = wb.x < CenterX;
            float cargoX = isLeft ? wb.x - 1.5f : wb.x + 3.45f;
            list.Add(new CargoZone($"Pickup_Shelf_{++i}", new Vector3(cargoX, 0.2f, wb.z), 0.9f, 1.4f, CargoZoneKind.Pickup));
        }
        int f = 0;
        foreach (var p in FloorPickupCenters)
            list.Add(new CargoZone($"Pickup_Floor_{++f}", new Vector3(p.x, 0.2f, p.z), 1.0f, 1.0f, CargoZoneKind.Pickup));
        return list.ToArray();
    }

    static CargoZone[] BuildDropZones()
    {
        var list = new List<CargoZone>
        {
            new CargoZone("Drop_B", StationB, 2.0f, 2.0f, CargoZoneKind.Drop),
            new CargoZone("Drop_A", StationA, 2.0f, 2.0f, CargoZoneKind.Drop),
            new CargoZone("Drop_S", new Vector3(22.0f, 0.2f, 30.0f), 1.6f, 1.6f, CargoZoneKind.Drop),
            new CargoZone("Drop_N", new Vector3(22.0f, 0.2f, 48.0f), 1.6f, 1.6f, CargoZoneKind.Drop),
            new CargoZone("Drop_W", new Vector3(10.5f, 0.2f, 36.0f), 1.6f, 1.6f, CargoZoneKind.Drop),
            new CargoZone("Drop_E", new Vector3(33.0f, 0.2f, 36.0f), 1.6f, 1.6f, CargoZoneKind.Drop),
        };
        int i = 0;
        foreach (var wb in WorkbenchPositions)
            list.Add(new CargoZone($"Drop_WB_{++i}", new Vector3(wb.x, 0.2f, wb.z), 1.2f, 1.5f, CargoZoneKind.Drop));
        return list.ToArray();
    }

    public static Vector3 Flatten(Vector3 p) => new Vector3(p.x, AgvFloorY, p.z);
}
