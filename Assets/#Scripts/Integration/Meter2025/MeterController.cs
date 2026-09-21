using TMPro;
using UnityEngine;

public class MeterController : MonoBehaviour
{
    // 車両
    [SerializeField]
    private VehicleController m_vehicleController;
    // 車両の情報
    struct VehicleInfo
    {
        public int currentGear; // 現在のギア
        public int prevGear; // 前のギア
        public float engineRPM; // エンジンの回転数
        public float kph; // 速度
        public float accelePedalValue; // アクセルの踏み具合
        public float brakePedalValue; // ブレーキの踏み具合
    }

    VehicleInfo m_vehicleInfo;
    // タイマー(TimeKeeperへ一本化、Meter専用のTimerは表示に使わない)
    [SerializeField]
    private TimeKeeper m_timeKeeper;
    // テキスト
    [SerializeField]
    private TextMeshProUGUI m_gearText; // ギアのテキスト
    [SerializeField]
    private TextMeshProUGUI m_rpmText; // エンジン回転数のテキスト
    [SerializeField]
    private TextMeshProUGUI m_kphText; // 速度のテキスト
    [SerializeField]
    private TextMeshProUGUI m_timerText; // タイマーのテキスト(TotalTime)
    // SectorTimeManager
    [SerializeField]
    private SectorTimeManager m_sectorTimeManager;
    // セクタータイムのテキスト
    [SerializeField]
    private TextMeshProUGUI[] m_sectorTimeText;
    // LED Indicator
    [SerializeField]
    private LED_IndicatorController m_LEDIndicatorController;
    // アクセルのインジケーター
    [SerializeField]
    private ProgressBar m_progressBar_Accele;
    // ブレーキのインジケーター
    [SerializeField]
    private ProgressBar m_progressBar_Brake;
    // 表示済みの値を記録して、同じ値ならUI更新しない
    private int m_lastDisplayGear = int.MinValue;
    private int m_lastDisplayRPM = int.MinValue;
    private int m_lastDisplayKPH = int.MinValue;
    private int m_lastTimerMinutes = int.MinValue;
    private int m_lastTimerSeconds = int.MinValue;
    private int m_lastTimerMilliSeconds = int.MinValue;
    private int m_lastSector1Minutes = int.MinValue;
    private int m_lastSector1Seconds = int.MinValue;
    private int m_lastSector1MilliSeconds = int.MinValue;
    private int m_lastSector2Minutes = int.MinValue;
    private int m_lastSector2Seconds = int.MinValue;
    private int m_lastSector2MilliSeconds = int.MinValue;
    /// <summary>
    /// メソッド名：Init()<br/>
    /// 概要：メーターの初期化処理
    /// </summary>
    public void Init()
    {
        // 表示キャッシュを初期化
        ResetDisplayCache();
        // テキスト初期化
        if (m_gearText != null)
        {
            m_gearText.SetText("N");
        }

        if (m_rpmText != null)
        {
            m_rpmText.SetText("0000");
        }

        if (m_kphText != null)
        {
            m_kphText.SetText("000");
        }

        // 車両の情報を取得
        GetVehicleInfo();
        // LED Indicatorの初期化処理
        if (m_LEDIndicatorController != null)
        {
            m_LEDIndicatorController.Init();
            m_LEDIndicatorController.Starting();
        }

        // アクセルのインジケーターの初期化処理
        if (m_progressBar_Accele != null)
        {
            m_progressBar_Accele.Init();
        }

        // ブレーキのインジケーターの初期化処理
        if (m_progressBar_Brake != null)
        {
            m_progressBar_Brake.Init();
        }
    }

    /// <summary>
    /// メソッド名：UpdateMeter()<br/>
    /// 概要：メーターの更新処理(Update)
    /// </summary>
    public void UpdateMeter()
    {
        if (m_vehicleController == null)
        {
            return;
        }

        // 車両の情報を取得
        GetVehicleInfo();
        // メーターの各パラメータのUIを更新
        UpdateMeterParam_UI();
    }

    /// <summary>
    /// メソッド名：FixedUpdateMeter()<br/>
    /// 概要：メーターの更新処理(FixedUpdate)。タイマーはTimeKeeperが自身のFixedUpdateで計測するためここでは何もしない
    /// </summary>
    public void FixedUpdateMeter()
    {
    }

    /// <summary>
    /// メソッド名：GetVehicleInfo()<br/>
    /// 概要：車両の状態を取得する
    /// </summary>
    private void GetVehicleInfo()
    {
        m_vehicleInfo.currentGear = m_vehicleController.ActiveGear;
        m_vehicleInfo.engineRPM = m_vehicleController.EngineRPM;
        m_vehicleInfo.kph = m_vehicleController.KPH;
        m_vehicleInfo.accelePedalValue = m_vehicleController.Accel;
        m_vehicleInfo.brakePedalValue = m_vehicleController.Brake;
    }

    /// <summary>
    /// メソッド名：UpdateMeterPram_UI()<br/>
    /// 概要：各パラメータのUIを更新
    /// </summary>
    private void UpdateMeterParam_UI()
    {
        UpdateGear_UI();
        UpdateRPM_UI();
        UpdateKPH_UI();
        UpdateLEDIndicator_UI();
        UpdateTimer_UI();
        UpdateSectorTime_UI();
        UpdateProgressBar_UI();
    }

    /// <summary>
    /// メソッド名：UpdateGear_UI()<br/>
    /// 概要：ギアのUI更新
    /// </summary>
    private void UpdateGear_UI()
    {
        if (m_gearText == null)
        {
            return;
        }

        // ギアが前回表示値から変わっていなければ更新しない
        if (m_vehicleInfo.currentGear == m_lastDisplayGear)
        {
            return;
        }

        if (m_vehicleInfo.currentGear > 0)
        {
            m_gearText.SetText("{0}", m_vehicleInfo.currentGear);
        }
        else if (m_vehicleInfo.currentGear == 0)
        {
            m_gearText.SetText("N");
        }
        else
        {
            m_gearText.SetText("R");
        }

        m_lastDisplayGear = m_vehicleInfo.currentGear;
        m_vehicleInfo.prevGear = m_vehicleInfo.currentGear;
    }

    /// <summary>
    /// メソッド名：UpdateRPM_UI()<br/>
    /// 概要：回転数のUI更新
    /// </summary>
    private void UpdateRPM_UI()
    {
        if (m_rpmText == null)
        {
            return;
        }

        int displayRPM = Mathf.Clamp(Mathf.RoundToInt(m_vehicleInfo.engineRPM), 0, 9999);
        // 表示値が同じなら更新しない
        if (displayRPM == m_lastDisplayRPM)
        {
            return;
        }

        m_rpmText.SetText("{0:0000}", displayRPM);
        m_lastDisplayRPM = displayRPM;
    }

    /// <summary>
    /// メソッド名：UpdateKPH_UI()<br/>
    /// 概要：速度のUI更新
    /// </summary>
    private void UpdateKPH_UI()
    {
        if (m_kphText == null)
        {
            return;
        }

        int displayKPH = Mathf.Clamp(Mathf.RoundToInt(m_vehicleInfo.kph), 0, 999);
        // 表示値が同じなら更新しない
        if (displayKPH == m_lastDisplayKPH)
        {
            return;
        }

        m_kphText.SetText("{0:000}", displayKPH);
        m_lastDisplayKPH = displayKPH;
    }

    /// <summary>
    /// メソッド名：UpdateLEDIndicator_UI()<br/>
    /// 概要：回転数のLEDインジケーターのUIを更新
    /// </summary>
    private void UpdateLEDIndicator_UI()
    {
        if (m_LEDIndicatorController != null)
        {
            m_LEDIndicatorController.Run(m_vehicleInfo.engineRPM);
        }
    }

    /// <summary>
    /// メソッド名：UpdateTimer_UI()<br/>
    /// 概要：タイマーの更新処理
    /// </summary>
    private void UpdateTimer_UI()
    {
        if (m_timerText == null || m_timeKeeper == null)
        {
            return;
        }

        int minutes = m_timeKeeper.CurrentMinutes;
        int seconds = m_timeKeeper.CurrentSeconds;
        int milliSeconds = m_timeKeeper.CurrentMilliseconds;
        // 表示値が同じなら更新しない
        if (minutes == m_lastTimerMinutes && seconds == m_lastTimerSeconds && milliSeconds == m_lastTimerMilliSeconds)
        {
            return;
        }

        m_timerText.SetText("{0:0}:{1:00}.{2:000}", minutes, seconds, milliSeconds);
        m_lastTimerMinutes = minutes;
        m_lastTimerSeconds = seconds;
        m_lastTimerMilliSeconds = milliSeconds;
    }

    /// <summary>
    /// メソッド名：UpdateSectorTime_UI()<br/>
    /// 概要：SectorTimeのUI更新
    /// </summary>
    private void UpdateSectorTime_UI()
    {
        if (m_sectorTimeManager == null || m_sectorTimeText == null || m_sectorTimeText.Length <= (int)SectorNumber.Sector2)
        {
            return;
        }

        TimerInfo sector1Time = m_sectorTimeManager.GetSectorTime(SectorNumber.Sector1);
        TimerInfo sector2Time = m_sectorTimeManager.GetSectorTime(SectorNumber.Sector2);
        UpdateSector1TimeText(sector1Time);
        UpdateSector2TimeText(sector2Time);
    }

    private void UpdateSector1TimeText(TimerInfo sectorTime)
    {
        TextMeshProUGUI targetText = m_sectorTimeText[(int)SectorNumber.Sector1];
        if (targetText == null)
        {
            return;
        }

        int minutes = Mathf.Clamp(Mathf.FloorToInt(sectorTime.minutes), 0, 99);
        int seconds = Mathf.Clamp(Mathf.FloorToInt(sectorTime.seconds), 0, 99);
        int milliSeconds = Mathf.Clamp(Mathf.FloorToInt(sectorTime.milliSecond), 0, 999);
        if (minutes == m_lastSector1Minutes && seconds == m_lastSector1Seconds && milliSeconds == m_lastSector1MilliSeconds)
        {
            return;
        }

        targetText.SetText("{0:0}:{1:00}.{2:000}", minutes, seconds, milliSeconds);
        m_lastSector1Minutes = minutes;
        m_lastSector1Seconds = seconds;
        m_lastSector1MilliSeconds = milliSeconds;
    }

    private void UpdateSector2TimeText(TimerInfo sectorTime)
    {
        TextMeshProUGUI targetText = m_sectorTimeText[(int)SectorNumber.Sector2];
        if (targetText == null)
        {
            return;
        }

        int minutes = Mathf.Clamp(Mathf.FloorToInt(sectorTime.minutes), 0, 99);
        int seconds = Mathf.Clamp(Mathf.FloorToInt(sectorTime.seconds), 0, 99);
        int milliSeconds = Mathf.Clamp(Mathf.FloorToInt(sectorTime.milliSecond), 0, 999);
        if (minutes == m_lastSector2Minutes && seconds == m_lastSector2Seconds && milliSeconds == m_lastSector2MilliSeconds)
        {
            return;
        }

        targetText.SetText("{0:0}:{1:00}.{2:000}", minutes, seconds, milliSeconds);
        m_lastSector2Minutes = minutes;
        m_lastSector2Seconds = seconds;
        m_lastSector2MilliSeconds = milliSeconds;
    }

    /// <summary>
    /// メソッド名：UpdateProgressBar_UI()<br/>
    /// 概要：ProgressBarのUI更新
    /// </summary>
    private void UpdateProgressBar_UI()
    {
        if (m_progressBar_Accele != null)
        {
            m_progressBar_Accele.SlideProgressBar(m_vehicleInfo.accelePedalValue);
        }

        if (m_progressBar_Brake != null)
        {
            m_progressBar_Brake.SlideProgressBar(m_vehicleInfo.brakePedalValue);
        }
    }

    private void ResetDisplayCache()
    {
        m_lastDisplayGear = int.MinValue;
        m_lastDisplayRPM = int.MinValue;
        m_lastDisplayKPH = int.MinValue;
        m_lastTimerMinutes = int.MinValue;
        m_lastTimerSeconds = int.MinValue;
        m_lastTimerMilliSeconds = int.MinValue;
        m_lastSector1Minutes = int.MinValue;
        m_lastSector1Seconds = int.MinValue;
        m_lastSector1MilliSeconds = int.MinValue;
        m_lastSector2Minutes = int.MinValue;
        m_lastSector2Seconds = int.MinValue;
        m_lastSector2MilliSeconds = int.MinValue;
    }
}
