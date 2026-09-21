using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 概要　：スタート/ゴールを通過したときにアクションを起こすプログラム
/// </summary>
public class Line_StartFinish : MonoBehaviour
{
    [System.Serializable]
    private enum LineMode
    {
        Start,
        Finish
    }

    [FormerlySerializedAs("_lineMode")]
    [SerializeField]
    private LineMode m_lineMode;
    private BoxCollider m_boxCollider;
    [FormerlySerializedAs("_timeKeeper")]
    [SerializeField]
    private TimeKeeper m_timeKeeper;
    [FormerlySerializedAs("_isChecked")]
    [SerializeField]
    private bool m_bIsChecked = false; // ラインを通過済みか
#region
    public bool IsChecked => m_bIsChecked;

#endregion
    private void Start()
    {
        m_boxCollider = GetComponent<BoxCollider>();
        m_bIsChecked = false;
    }

    private void OnTriggerEnter(Collider _other)
    {
        if (m_bIsChecked == false)
        {
            switch (m_lineMode)
            {
                case LineMode.Start:
                    m_timeKeeper?.ControlActiveFlag(true);
                    break;
                case LineMode.Finish:
                    m_timeKeeper?.ControlActiveFlag(false);
                    GameManager.Instance.ResultTime = m_timeKeeper.RetrieveSavedTotalTime();
                    GameManager.Instance.SetLapTimes(m_timeKeeper.RetrieveSavedLapTimes());
                    break;
            }

            m_bIsChecked = true;
        }
    }
}
