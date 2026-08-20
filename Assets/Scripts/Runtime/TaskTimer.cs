using UnityEngine;

[DisallowMultipleComponent]
public class TaskTimer : MonoBehaviour
{
    public float ElapsedTime { get; private set; }
    public bool IsRunning { get; private set; }
    float _startTime;

    void Update()
    {
        if (IsRunning)
            ElapsedTime = Time.time - _startTime;
    }

    public void StartTiming()
    {
        _startTime = Time.time;
        ElapsedTime = 0f;
        IsRunning = true;
    }

    public void StopTiming()
    {
        if (!IsRunning) return;
        ElapsedTime = Time.time - _startTime;
        IsRunning = false;
    }

    public void ResetTimer()
    {
        ElapsedTime = 0f;
        IsRunning = false;
    }
}
