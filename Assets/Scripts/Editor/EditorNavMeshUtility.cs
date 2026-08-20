using UnityEditor;

/// <summary>エディタでの NavMesh ベイク（非推奨 API を一箇所に集約）</summary>
internal static class EditorNavMeshUtility
{
    public static void BakeNavMesh()
    {
#pragma warning disable CS0618
        UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
#pragma warning restore CS0618
    }

    public static void ClearNavMeshes()
    {
#pragma warning disable CS0618
        UnityEditor.AI.NavMeshBuilder.ClearAllNavMeshes();
#pragma warning restore CS0618
    }
}
