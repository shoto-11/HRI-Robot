using UnityEngine;

/// <summary>ピックアップ／ドロップゾーンのマーカー。子の PickupBox を BoxPickupPool が収集する。</summary>
public class CargoZoneMarker : MonoBehaviour
{
    public FactoryLayout.CargoZoneKind Kind = FactoryLayout.CargoZoneKind.Pickup;
    public bool EnabledForGameplay = true;

    public Transform[] GetPickupBoxes()
    {
        var list = new System.Collections.Generic.List<Transform>();
        foreach (Transform child in transform)
        {
            if (child != null && (child.name.Contains("PickupBox") || child.name.Contains("PlasticBox")))
                list.Add(child);
        }
        return list.ToArray();
    }
}
