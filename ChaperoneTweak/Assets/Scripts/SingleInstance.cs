using System.Diagnostics;
using System.Threading;
using UnityEngine;

/// <summary>
/// Ensures only one instance of ChaperoneTweak runs at a time.
/// If another instance is already running, it is killed before this one continues.
/// Attach to any persistent root GameObject; runs before all other scripts.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class SingleInstance : MonoBehaviour
{
    const string MutexName = "Global\\ChaperoneTweakSingleInstance";

    static Mutex _mutex;

    void Awake()
    {
        bool createdNew;
        _mutex = new Mutex(true, MutexName, out createdNew);

        if (!createdNew)
        {
            // Another instance owns the mutex — kill it, then re-acquire.
            KillOtherInstances();
            _mutex.Close();
            _mutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew)
                UnityEngine.Debug.LogWarning("[SingleInstance] Could not acquire mutex after killing other instance.");
        }

        DontDestroyOnLoad(gameObject);
    }

    void OnApplicationQuit()
    {
        if (_mutex != null) { _mutex.ReleaseMutex(); _mutex.Close(); _mutex = null; }
    }

    static void KillOtherInstances()
    {
        int self = Process.GetCurrentProcess().Id;
        string name = Process.GetCurrentProcess().ProcessName;
        foreach (var p in Process.GetProcessesByName(name))
        {
            if (p.Id == self) continue;
            try
            {
                p.Kill();
                p.WaitForExit(3000);
                UnityEngine.Debug.Log("[SingleInstance] Killed previous instance (PID " + p.Id + ")");
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogWarning("[SingleInstance] Could not kill PID " + p.Id + ": " + e.Message);
            }
        }
    }
}
