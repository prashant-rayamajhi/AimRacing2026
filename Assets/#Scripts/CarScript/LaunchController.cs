// 以前の発進制御を確認用として残す
// foreach(TireSound Wheel in m_tiresound)
// 低速時のスリップ上書き候補は現在使用しない
using System.Collections.Generic;
using UnityEngine;

public class LaunchController : MonoBehaviour
{
    [SerializeField]
    VehicleController m_vehicle;
    [SerializeField]
    List<TireSound> m_tiresound;
    [SerializeField]
    float m_launchRPM;
    [SerializeField]
    bool m_active = false;
    [SerializeField]
    UI_Meter m_meter;
    float m_flashingRPM;
    public bool Active { get => m_active; set => m_active = value; }

    private void Start()
    {
        if (m_vehicle == null)
        {
            m_vehicle = FindAnyObjectByType<VehicleController>();
        }

        if (m_vehicle == null)
        {
            enabled = false;
            return;
        }

        // 以前の発進制御を確認用として残す
        if (m_meter != null)
            m_flashingRPM = m_meter.FlashingRPM;
        // 点滅だけを設定し、VehicleControllerの待機制限と競合するエンジン上限の書き換えは行わない
        if (m_meter != null)
        {
            m_meter.FlashingRPM = m_launchRPM;
        }
    }

    private void FixedUpdate()
    {
        if (m_vehicle.KPH > 60f)
        {
            m_active = false;
            if (m_meter != null)
                m_meter.FlashingRPM = m_flashingRPM;
        }

        foreach (TireSound Wheel in m_tiresound)
        {
            if (Wheel == null)
            {
                continue;
            }

            if (m_active)
            {
            // 発進演出中のタイヤ音へ一定のスリップ量を渡す
            }
            else
            {
            }
        }
    }
}
