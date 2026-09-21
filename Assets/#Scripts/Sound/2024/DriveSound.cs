using UnityEngine;
using FMODUnity;

public class DriveSound : MonoBehaviour
{
    // 停止直前にブレーキ摩擦音を消すための音量立ち上がり速度
    [SerializeField, Min(0.1f)]
    float m_brakeSoundFullSpeedKph = 10f;
    // ペダル操作で摩擦音が突然切り替わらないための応答時間
    [SerializeField, Min(0.01f)]
    float m_brakeSoundResponseSeconds = 0.08f;
    // 前回のブレーキ音要求を引き継ぐ補間値
    float m_brakeSoundAmount;
    // 制動力を変更せず走行中の接地状態とペダル量だけを摩擦音へ渡す関数
    void UpdateBrakeSound()
    {
        // 停止に近づくほど音を弱め静止中の鳴り続けを防ぐ
        float speedRatio = Mathf.Clamp01(Mathf.Abs(m_vehicle.KPH) / Mathf.Max(0.1f, m_brakeSoundFullSpeedKph));
        float target = Mathf.Clamp01(m_vehicle.Brake) * speedRatio;
        float response = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, m_brakeSoundResponseSeconds));
        m_brakeSoundAmount = Mathf.Lerp(m_brakeSoundAmount, target, response);
        // 待機中や空中ではブレーキ入力があっても路面との摩擦音を出さない
        if (m_vehicle.IsPullUp || m_vehicle.GroundedWheelCount == 0 || speedRatio <= 0f)
        {
            m_brakeSoundAmount = 0f;
        }

        SendSoundParameter("BRAKE", m_brakeSoundAmount);
    }

    // アクセルオフのミッション音を強調する量でエンジン音量は変更しない
    [SerializeField, Range(0f, 0.1f)]
    float m_missionCoastVolumeOffset = 0.04f;
    // 停止時のペダル操作ではミッション音を強調しないための最低速度
    [SerializeField, Min(0f)]
    float m_missionCoastMinimumKPH = 10f;
    // オンとオフで音量が急変しないための切替秒数
    [SerializeField, Min(0.01f)]
    float m_missionCoastBlendSeconds = 0.2f;
    // バックタービンだけを補い共有TURBOVOLを上げ直さないための音量差
    [SerializeField, Range(0f, 18f)]
    float m_backTurbinGainDb = 16f;
    // 既存アンチラグ音が他の連続音へ埋もれないための専用音量差
    [SerializeField, Range(0f, 12f)]
    float m_antilagGainDb = 3f;
    // エンジンやタービンを増幅せず既存アンチラグ音だけを補強する参照
    readonly BackTurbinGain m_antilagGain = new BackTurbinGain("str t yar");
    // アクセルオフのミッション音量を滑らかに補間する割合
    float m_missionCoastBlend;
    // バックタービン音源だけに接続した音量補正器
    readonly BackTurbinGain m_backTurbinGain = new BackTurbinGain();
    // 既存バンクを変更せずミッション音だけに変速時の揺れを加える設定
    [SerializeField]
    bool m_missionPitchEnabled = true;
    // 変速と踏み直しでミッション音を揺らす幅をセント単位で調整する設定
    [SerializeField, Range(0f, 100f)]
    float m_missionPitchCents = 60f;
    // 各ギアの音程が上がり切る速度で実際の変速条件には使わない
    [SerializeField]
    float[] m_missionSoundTopKPH =
    {
        30f,
        80f,
        100f,
        130f,
        160f,
        230f
    };
    // 各ギアへ入った時の音程を既存音源からの半音差で調整する
    [SerializeField]
    float[] m_missionSoundLowSemitones =
    {
        -18f,
        -17f,
        -14f,
        -11f,
        -7f,
        -4f
    };
    // ギア内で加速した時に上昇する音程の幅
    [SerializeField, Range(0f, 12f)]
    float m_missionSoundRiseSemitones = 8f;
    // 追加した音程補正だけをシーン終了時に解放するための参照
    readonly MissionPitchEffect m_missionPitch = new MissionPitchEffect();
    // エンジン音と同じ入力切替でミッション音の揺れを開始するための通知
    bool m_missionPitchTriggered;
    [SerializeField]
    float m_turboSoundMinimumRPM = 3000f;
    [SerializeField, Range(0f, 0.2f)]
    float m_soundOffThreshold = 0.03f;
    // 約0.95秒の既存音源を途中で切らず最後まで鳴らす保持時間
    [SerializeField, Range(0.05f, 2f)]
    float m_backTurbinDuration = 1f;
    // 同じ変速で繰り返し発火しないよう前回入力と音の残り時間を保持する
    bool m_previousSoundReleased = true;
    int m_previousSoundGear;
    // 変速直後に回転が落ちても直前の過給状態で解放音を判定するための回転数
    float m_previousTurboSoundRPM;
    float m_backTurbinRemaining;
    // 踏み直した瞬間の回転数で音の立ち上がり時間を確定するための秒数
    float m_currentTurboAttackSeconds = 0.9f;
    // 既存FMODの再生区間0.01～0.99内へ入れるための発火値
    const float BackTurbinTriggerValue = 0.5f;
    // 踏み直し時のタービン音の立ち上がりを回転数別に調整する設定
    [SerializeField]
    AnimationCurve m_turboAttackSeconds = new AnimationCurve(new Keyframe(3000f, 0.9f), new Keyframe(4500f, 0.45f), new Keyframe(5500f, 0.22f));
    // 指定回転以上でタービン音の立ち上がりが完了するための回転数
    [SerializeField, Min(1f)]
    float m_turboPitchPlateauRPM = 2500f;
    // アクセルOFFで次の踏み直しへ音の立ち上がりを戻す時間
    [SerializeField, Min(0.01f)]
    float m_turboReleaseSeconds = 0.15f;
    // 同じバンク不整合を毎フレーム通知しないための設定名の記録
    readonly System.Collections.Generic.HashSet<string> m_failedSoundParameters = new System.Collections.Generic.HashSet<string>();
    // 旧バンクにないCOASTへ毎フレーム無効な送信をしないための接続状態
    bool m_hasCoastParameter;
    // 録音済みのレブ音を燃料カットのたびに先頭から再生しないための保持状態
    bool m_revSoundHeld;
    // カウント中の全開だけ既存レブ音を単独再生するための音量制御
    readonly CountdownRevIsolation m_countdownRevIsolation = new CountdownRevIsolation();
    // 全開ペダルの小さな入力誤差で専用音が途切れないための判定値
    const float CountdownFullThrottle = 0.98f;
    // 燃料復帰中の短い回転低下ではレブ音を止めないための回転差
    [SerializeField, Min(0f)]
    float m_revSoundReleaseMarginRPM = 250f;
    // 既存バンクのタービン音程へ重なるRPM補正だけを打ち消す設定
    [SerializeField]
    bool m_compensateTurboRPMPitch = true;
    // バンクへ送った音用回転数とタービン補正の回転数を一致させる記録
    float m_soundRPM;
    // 既存DriveバンクのRPM音程カーブを半音単位で再現する関数
    static float GetBankTurboRPMPitch(float rpm)
    {
        // FMOD編集データの三点を使用しエンジン本体のRPMは変更しない
        const float peakRPM = 7093.3054f;
        const float maximumRPM = 10000f;
        const float lowPitch = -7f;
        const float peakPitch = 0.802639f;
        const float highPitch = -3.5f;
        if (rpm <= peakRPM)
        {
            return Mathf.Lerp(lowPitch, peakPitch, Mathf.Clamp01(rpm / peakRPM));
        }

        return Mathf.Lerp(peakPitch, highPitch, Mathf.InverseLerp(peakRPM, maximumRPM, rpm));
    }

    // タービンだけの既存音程補正を相殺して踏み直しの立ち上がりを送る関数
    static float CalculateTurboParameter(float spool, float rpm, float plateauRPM)
    {
        // TURBOの音程幅は既存バンクのマイナス11半音からプラス8半音
        const float pitchSpan = 19f;
        if (spool <= 0f)
        {
            return 0f;
        }

        // 旧式は立ち上がりの下限で補正値がゼロへ切り詰められ高回転ほど開始音程が変わっていた
        // 全回転域で再現できる開始音程から2500回転時の最高音程まで同じ音階を使う
        const float minimumParameterPitch = -11f;
        const float maximumBankRPMPitch = 0.802639f;
        float lowPitch = minimumParameterPitch + maximumBankRPMPitch;
        float highPitch = minimumParameterPitch + pitchSpan + GetBankTurboRPMPitch(plateauRPM);
        float requestedPitch = Mathf.Lerp(lowPitch, highPitch, Mathf.Clamp01(spool));
        return Mathf.Clamp01((requestedPitch - GetBankTurboRPMPitch(rpm) - minimumParameterPitch) / pitchSpan);
    }

    // バンクへの送信失敗を一度だけ通知して無音の原因を見つけられるようにする関数
    void SendSoundParameter(string parameter, float value)
    {
        FMOD.RESULT result = m_driveEvent.setParameterByName(parameter, value);
        if (result == FMOD.RESULT.OK || !m_failedSoundParameters.Add(parameter))
        {
            return;
        }

        AppLog.LogError($"[DriveSound] {parameter}を送信できません: {result}。イベントと読み込んだバンクを確認してください。", this);
    }

    // 変速とアクセル切替の音だけを短く揺らす演出設定で実エンジン回転は変えない
    [SerializeField, Range(0f, 200f)]
    float m_pitchWobbleMaximumRPM = 100f;
    [SerializeField, Range(0.1f, 1f)]
    float m_pitchWobbleSeconds = 0.35f;
    [SerializeField]
    Vector2 m_pitchWobbleFrequencyHz = new Vector2(8f, 15f);
    // 切替の初回だけ揺れを始めるため前回入力と経過時間を保持する
    bool m_pitchStateInitialized;
    bool m_previousPitchThrottleOn;
    int m_previousPitchGear;
    float m_pitchWobbleElapsed = float.PositiveInfinity;
    float m_pitchWobblePhase;
    // 回転に応じた減衰サイン波を音用RPMだけへ加える関数
    float CalculateSoundRPM()
    {
        // ペダルのゼロ付近のノイズで演出が繰り返されないよう切替幅を設ける
        bool throttleOn = m_previousPitchThrottleOn ? m_vehicle.Accel > m_soundOffThreshold : m_vehicle.Accel > m_soundOffThreshold * 2f;
        bool changed = m_pitchStateInitialized && (throttleOn != m_previousPitchThrottleOn || m_previousPitchGear != m_vehicle.ActiveGear);
        // 入力の切替を音源ごとに独立した音程補正へ渡す
        m_missionPitchTriggered = changed;
        if (changed)
        {
            m_pitchWobbleElapsed = 0f;
            m_pitchWobblePhase = 0f;
        }

        m_pitchStateInitialized = true;
        m_previousPitchThrottleOn = throttleOn;
        m_previousPitchGear = m_vehicle.ActiveGear;
        // 音の揺れは停止中やリミッター中には加えず既存のREV演出を優先する
        if (m_vehicle.IsPullUp || m_vehicle.Engine.RevLimiterCut)
        {
            m_pitchWobbleElapsed = float.PositiveInfinity;
        }

        float duration = Mathf.Max(0.01f, m_pitchWobbleSeconds);
        if (m_pitchWobbleElapsed >= duration)
        {
            return m_vehicle.EngineRPM;
        }

        float rpmRatio = Mathf.Clamp01(m_vehicle.EngineRPM / Mathf.Max(1f, m_vehicle.Engine.OverRevRPM));
        float frequency = Mathf.Lerp(m_pitchWobbleFrequencyHz.x, m_pitchWobbleFrequencyHz.y, rpmRatio);
        m_pitchWobblePhase += 2f * Mathf.PI * Mathf.Max(0f, frequency) * Time.deltaTime;
        m_pitchWobbleElapsed += Time.deltaTime;
        float envelope = Mathf.Pow(1f - Mathf.Clamp01(m_pitchWobbleElapsed / duration), 2f);
        float offset = Mathf.Sin(m_pitchWobblePhase) * envelope * Mathf.Max(0f, m_pitchWobbleMaximumRPM) * rpmRatio;
        return Mathf.Clamp(m_vehicle.EngineRPM + offset, 0f, m_vehicle.Engine.OverRevRPM);
    }

    [SerializeField]
    VehicleController m_vehicle;
    [SerializeField]
    AnimationCurve m_turbolag;
    [SerializeField]
    EventReference m_driveEventRef;
    FMOD.Studio.EventInstance m_driveEvent;
    Transmission m_mission;
    // ターボ・バックタービン音用変数
    public float m_turboComp = 0.0f; // 加圧の割合
    // エンブレ用
    float m_EngineBreak = 0f;
    float m_EG_DampingRatio = 1.0f;
    [Header("PartsVolume")]
    [SerializeField, Range(0.0f, 1.0f)]
    float m_EngineVolume;
    float m_RestEnginVolume;
    [SerializeField, Range(0.0f, 1.0f)]
    float m_MissionVolume;
    float m_RestMissionVolume;
    [SerializeField, Range(0.0f, 1.0f)]
    float m_TurboVolume;
    float m_RestTurboVolume;
    [SerializeField, Range(0.0f, 1.0f)]
    float m_RoadVolume;
    float m_RestRoadVolume;
    [SerializeField, Range(0.0f, 1.0f)]
    float m_GearUpVolume;
    float m_RestGearUpVolume;
    [SerializeField, Range(0.0f, 1.0f)]
    float m_GearDownVolume;
    float m_RestGearDownVolume;
    [SerializeField, Range(0.0f, 1.0f)]
    float m_BreakVolume;
    float m_RestBreakVolume;
    [SerializeField]
    float m_UpdateInterval = 0.5f;
    float m_UpdateTime;
    private void Start()
    {
        m_mission = m_vehicle.Transmission;
        m_driveEvent = RuntimeManager.CreateInstance(m_driveEventRef);
        // 旧バンクと改修後バンクを区別して未接続のアンチラグ条件を通知する
        m_driveEvent.getDescription(out var soundDescription);
        m_hasCoastParameter = soundDescription.getParameterDescriptionByName("COAST", out _) == FMOD.RESULT.OK;
        if (!m_hasCoastParameter)
        {
            Debug.LogWarning("[DriveSound] COASTは未接続です。既存AntilugはRPMとGASで再生します。3000～4500rpmの追加条件は未対応です。", this);
        }

        m_driveEvent.start();
        RuntimeManager.AttachInstanceToGameObject(m_driveEvent, gameObject.transform);
        // ボリュームの初期値を保存
        m_RestEnginVolume = m_EngineVolume;
        m_RestMissionVolume = m_MissionVolume;
        m_RestTurboVolume = m_TurboVolume;
        m_RestRoadVolume = m_RoadVolume;
        m_RestGearUpVolume = m_GearUpVolume;
        m_RestGearDownVolume = m_GearDownVolume;
        m_RestBreakVolume = m_BreakVolume;
        m_driveEvent.setParameterByName("ENGINEVOL", m_EngineVolume);
        m_driveEvent.setParameterByName("MISSIONVOL", m_MissionVolume);
        m_driveEvent.setParameterByName("TURBOVOL", m_TurboVolume);
        m_driveEvent.setParameterByName("ROADVOL", m_RoadVolume);
        m_driveEvent.setParameterByName("GEARUPVOL", m_GearUpVolume);
        m_driveEvent.setParameterByName("GEARDOWNVOL", m_GearDownVolume);
        m_driveEvent.setParameterByName("BREAKVOL", m_BreakVolume);
    }

    private void Update()
    {
        // エンジン音にのみ揺れを渡し速度連動のミッション音や物理計算を変えない
        // 音用の回転揺れまで含めてタービンの不要な音程変化を相殺する
        m_soundRPM = CalculateSoundRPM();
        SendSoundParameter("RPM", m_soundRPM);
        // 速度とギアの既存音程を残してミッション音源だけに減衰する揺れを重ねる
        bool missionPitchAllowed = m_missionPitchEnabled && !m_vehicle.IsPullUp && !m_vehicle.Engine.RevLimiterCut && m_vehicle.ActiveGear > 0;
        float missionRPMRatio = m_vehicle.EngineRPM / Mathf.Max(1f, m_vehicle.Engine.OverRevRPM);
        // 既存バンクの速度音程と二重加算せず資料のギア別テーブルとの差分だけを補正する
        float missionCorrection = CalculateMissionCorrection(m_vehicle.KPH, m_vehicle.ActiveGear);
        m_missionPitch.Tick(m_driveEvent, m_missionPitchTriggered, missionPitchAllowed, missionRPMRatio, Time.deltaTime, m_pitchWobbleSeconds, m_missionPitchCents, m_pitchWobbleFrequencyHz.x, m_pitchWobbleFrequencyHz.y, missionCorrection);
        // 走行中も燃料カットと吸気応答を音へ渡し全開音が鳴り続ける食い違いを防ぐ
        SendSoundParameter("GAS", m_vehicle.Engine.CombustionThrottle);
        SendSoundParameter("GEAR", m_vehicle.ActiveGear);
        SendSoundParameter("SPEED", Mathf.Abs(m_vehicle.KPH));
        // 制動入力を直送せず停止と接地を考慮した音専用の値を送る
        UpdateBrakeSound();
        m_driveEvent.setParameterByName("HORN", m_vehicle.IsHorn);
        // エンジンミュート
        // シフトチェンジ
        float engineMute = 0f;
        float ShiftUp = 0f;
        float ShiftDown = 0f;
        // 待機中の一速準備で空ぶかし音を消さず走行中の変速音だけを切り替える
        if (m_mission.IsGearChanging && !m_vehicle.IsPullUp)
        {
            // ATは駆動力を残す変速なので音も全消音せず実際のトルク低下へ追従する
            // 駆動力が切れてもエンジンは回るため消音せずRPMと変速通知で音を変える
            engineMute = 0f;
            if (m_mission.IsShiftUp)
            {
                ShiftUp = 1f;
            }
            else
            {
                ShiftDown = 1f;
                m_EngineBreak = 1f;
            }
        }

        SendSoundParameter("SHIFTUP", ShiftUp);
        // 既存DriveのBackfireはSHIFTDOWNで接続されているため送信失敗を見逃さない
        SendSoundParameter("SHIFTDOWN", ShiftDown);
        m_driveEvent.setParameterByName("MUTE", engineMute);
        // 変速通知は音量と分けATの部分消音中も変速イベントとして通知する
        // 既存バンクの正式名にそろえ送信失敗も記録する
        SendSoundParameter("GEARCHANGE", m_mission.IsGearChanging && !m_vehicle.IsPullUp ? 1f : 0f);
        SetLaunchSound();
        SetTurboSound();
        SetCOAST();
        SetRevSound();
        SetEngineBreakSound();
        SetVolume();
        // 通常音量の更新後に走行中のアクセルオフ分だけを加える
        bool coasting = !m_vehicle.IsPullUp && m_vehicle.ActiveGear != 0 && Mathf.Abs(m_vehicle.KPH) >= m_missionCoastMinimumKPH && m_vehicle.Accel <= m_soundOffThreshold;
        m_missionCoastBlend = Mathf.MoveTowards(m_missionCoastBlend, coasting ? 1f : 0f, Time.deltaTime / Mathf.Max(0.01f, m_missionCoastBlendSeconds));
        SendSoundParameter("MISSIONVOL", Mathf.Clamp01(m_MissionVolume + m_missionCoastVolumeOffset * m_missionCoastBlend));
        m_backTurbinGain.Tick(m_driveEvent, m_backTurbinGainDb);
        m_antilagGain.Tick(m_driveEvent, m_antilagGainDb);
        // 走行中はエンジンの枝を消音せずREVLIMITパラメーターで専用音を重ねる
        bool soloRev = ShouldSoloCountdownRev(m_vehicle.IsPullUp, m_vehicle.Accel, m_revSoundHeld);
        m_countdownRevIsolation.Tick(m_driveEvent, soloRev);
    }

    // 待機中に全開でリミッターへ当たり続けている区間だけ専用音を選ぶ関数
    internal static bool ShouldSoloCountdownRev(bool waiting, float throttle, bool limiterHeld)
    {
        return waiting && throttle >= CountdownFullThrottle && limiterHeld;
    }

    // 速度と実ギアからミッション専用の音程差を求めエンジンとタービンには適用しない関数
    float CalculateMissionCorrection(float speed, int gear)
    {
        // 配列設定が不足した場合は既存バンクの音程を維持する
        if (gear < 1 || gear > 6 || m_missionSoundTopKPH == null || m_missionSoundLowSemitones == null)
        {
            return 0f;
        }

        if (m_missionSoundTopKPH.Length < gear || m_missionSoundLowSemitones.Length < gear)
        {
            return 0f;
        }

        float startSpeed = gear == 1 ? 0f : m_missionSoundTopKPH[gear - 2];
        float endSpeed = Mathf.Max(startSpeed + 1f, m_missionSoundTopKPH[gear - 1]);
        float progress = Mathf.InverseLerp(startSpeed, endSpeed, Mathf.Abs(speed));
        float target = m_missionSoundLowSemitones[gear - 1] + progress * m_missionSoundRiseSemitones;
        // 既存Driveバンクの速度カーブとギア補正を相殺するための元音程
        const float bankSpeedKnee = 40.02463f;
        float bankSpeedPitch = speed <= bankSpeedKnee ? Mathf.Lerp(-21f, -14f, Mathf.InverseLerp(0f, bankSpeedKnee, speed)) : Mathf.Lerp(-14f, 6f, Mathf.InverseLerp(bankSpeedKnee, 250f, speed));
        float bankGearPitch = gear <= 4 ? gear * 1.5f : gear == 5 ? 8.25f : 9.75f;
        return target - bankSpeedPitch - bankGearPitch;
    }

    // //ギアチェンジ時のバックタービン音補正用
    void SetTurboSound()
    {
        bool released = m_vehicle.Accel <= m_soundOffThreshold;
        // Nから走行段への選択を走行中のシフトアップ音と取り違えない
        bool shiftedUp = !m_vehicle.IsPullUp && m_previousSoundGear > 0 && m_vehicle.ActiveGear > m_previousSoundGear;
        // アクセル解除や変速で回転が下がったフレームも切替直前の回転数を含めて判定する
        bool highRPM = Mathf.Max(m_vehicle.EngineRPM, m_previousTurboSoundRPM) >= m_turboSoundMinimumRPM;
        // アクセルOFFまたはアクセル中のシフトアップ開始だけ発火する
        if (highRPM && ((!m_previousSoundReleased && released) || (!released && shiftedUp)))
        {
            m_backTurbinRemaining = m_backTurbinDuration;
        }

        // 発火後に回転が下がっても音源の余韻は保持時間まで残す
        // 値1は既存音源の再生範囲外なので範囲の中央へ送る
        SendSoundParameter("BACKTURBIN", m_backTurbinRemaining > 0f ? BackTurbinTriggerValue : 0f);
        m_backTurbinRemaining = Mathf.Max(0f, m_backTurbinRemaining - Time.deltaTime);
        // 踏み直しでは実過給圧を変えず音だけ低い音程から立ち上げ直す
        if (!released && m_previousSoundReleased)
        {
            // 回転が上昇しても踏み直し時に選んだ立ち上がり時間を途中で短縮しない
            m_turboComp = 0f;
            m_currentTurboAttackSeconds = Mathf.Max(0.01f, m_turboAttackSeconds.Evaluate(m_vehicle.EngineRPM));
        }

        float target = released ? 0f : Mathf.Clamp01(m_vehicle.EngineRPM / Mathf.Max(1f, m_turboPitchPlateauRPM));
        float attack = m_currentTurboAttackSeconds;
        float duration = released ? m_turboReleaseSeconds : attack;
        m_turboComp = Mathf.MoveTowards(m_turboComp, target, Time.deltaTime / Mathf.Max(0.01f, duration));
        // RPMに重なる音程を相殺し2500回転以上で速度とともに音階が上がることを防ぐ
        float turboParameter = m_compensateTurboRPMPitch ? CalculateTurboParameter(m_turboComp, m_soundRPM, m_turboPitchPlateauRPM) : m_turboComp;
        SendSoundParameter("TURBO", turboParameter);
        m_previousSoundReleased = released;
        m_previousSoundGear = m_vehicle.ActiveGear;
        // 次の入力切替で使う回転数を音の更新が完了してから記録する
        m_previousTurboSoundRPM = m_vehicle.EngineRPM;
    }

    void SetLaunchSound()
    {
        // 待機中の音を実際の燃料カットと同じフレームで切り替える
        if (!m_vehicle.IsPullUp)
        {
            return;
        }

        // 踏み始めと燃料カット復帰の吸気応答を回転計算と共通化して音だけ先行するのを防ぐ
        m_driveEvent.setParameterByName("GAS", m_vehicle.Engine.CombustionThrottle);
    }

    // // コースト(惰性走行)の条件
    // // アクセルが0.3以下
    // // エンジンRPMが5500以上
    void SetCOAST()
    {
        // 高回転のアクセルOFF中は既存のアンチラグ用パラメーターを維持する
        // 待機中は燃料カットに連動し走行中は高回転のアクセルOFF中に鳴らす
        bool launchCut = m_vehicle.IsPullUp && m_vehicle.Accel > m_soundOffThreshold && m_vehicle.Engine.RevLimiterCut;
        bool active = (m_vehicle.Accel <= m_soundOffThreshold || launchCut) && m_vehicle.EngineRPM >= m_turboSoundMinimumRPM;
        // COASTがバンクへ組み込まれていない場合は無音の原因を一度通知する
        if (m_hasCoastParameter)
        {
            SendSoundParameter("COAST", active ? 1f : 0f);
        }
    }

    void SetRevSound()
    {
        // 回転差で判定を推測せずエンジンが燃料を切っている間だけ既存のREV音へ通知する
        // 燃料のオンオフではなくリミッターに当たり続けている区間で既存ループを再生する
        var engine = m_vehicle.Engine;
        float limit = engine.TemporaryRevLimitRPM > 0f ? Mathf.Min(engine.TemporaryRevLimitRPM, engine.OverRevRPM) : engine.OverRevRPM;
        bool throttleHeld = m_vehicle.Accel > m_soundOffThreshold;
        bool shifting = m_mission.IsGearChanging && !m_vehicle.IsPullUp;
        if (!throttleHeld || shifting || m_vehicle.EngineRPM < limit - m_revSoundReleaseMarginRPM)
        {
            m_revSoundHeld = false;
        }
        // 描画フレーム間の短い燃料カットを取り逃しても上限到達時にレブ音を開始する
        else if (engine.RevLimiterCut || m_vehicle.EngineRPM >= limit)
        {
            m_revSoundHeld = true;
        }

        float revSound = m_revSoundHeld ? 1f : 0f;
        // 燃料カット通知がバンクへ届かない場合も診断できるようにする
        SendSoundParameter("REVLIMIT", revSound);
    }

    void SetEngineBreakSound()
    {
        float EngineBreak_vol = m_EngineBreak;
        if (m_vehicle.EngineRPM < 3000f)
        {
            EngineBreak_vol = 0f;
        }

        m_driveEvent.setParameterByName("ENGINEBREAK", EngineBreak_vol);
        // 次の変速まで減衰値が負へ増え続けないよう音量範囲へ収める
        m_EngineBreak = Mathf.Max(0f, m_EngineBreak - Time.deltaTime * m_EG_DampingRatio);
    }

    // 部品ごとの音量調整
    void SetVolume()
    {
        m_UpdateTime += Time.deltaTime;
        if (m_UpdateInterval > m_UpdateTime)
        {
            return;
        }

        m_UpdateTime = 0.0f;
        // 変更がある場合パラメータを更新
        if (m_RestEnginVolume != m_EngineVolume)
        {
            m_driveEvent.setParameterByName("ENGINEVOL", m_EngineVolume);
            m_RestEnginVolume = m_EngineVolume;
        }

        if (m_RestMissionVolume != m_MissionVolume)
        {
            m_driveEvent.setParameterByName("MISSIONVOL", m_MissionVolume);
            m_RestMissionVolume = m_MissionVolume;
        }

        if (m_RestTurboVolume != m_TurboVolume)
        {
            m_driveEvent.setParameterByName("TURBOVOL", m_TurboVolume);
            m_RestTurboVolume = m_TurboVolume;
        }

        if (m_RestRoadVolume != m_RoadVolume)
        {
            m_driveEvent.setParameterByName("ROADVOL", m_RoadVolume);
            m_RestRoadVolume = m_RoadVolume;
        }

        if (m_RestGearUpVolume != m_GearUpVolume)
        {
            m_driveEvent.setParameterByName("GEARUPVOL", m_GearUpVolume);
            m_RestGearUpVolume = m_GearUpVolume;
        }

        if (m_RestGearDownVolume != m_GearDownVolume)
        {
            m_driveEvent.setParameterByName("GEARDOWNVOL", m_GearDownVolume);
            m_RestGearDownVolume = m_GearDownVolume;
        }

        if (m_RestBreakVolume != m_BreakVolume)
        {
            m_driveEvent.setParameterByName("BREAKVOL", m_BreakVolume);
            m_RestBreakVolume = m_BreakVolume;
        }
    }

    // コンポーネントを無効にした時も追加の音程補正が残らないようにする関数
    void OnDisable()
    {
        // 追加した音量補正を残さず次の再生へ持ち越さない
        m_backTurbinGain.Dispose();
        m_antilagGain.Dispose();
        m_missionCoastBlend = 0f;
        // 無効化された後の走行音へ待機中の消音を残さない
        m_countdownRevIsolation.Dispose();
        m_revSoundHeld = false;
        m_missionPitch.Dispose();
    }

    void OnDestroy()
    {
        // 音源破棄前にバックタービン専用の補正器を解放する
        m_backTurbinGain.Dispose();
        m_antilagGain.Dispose();
        // イベントを破棄する前に待機音専用の消音状態を解除する
        m_countdownRevIsolation.Dispose();
        // イベントを停止する前にミッション音へ追加した補正器を外す
        m_missionPitch.Dispose();
        m_driveEvent.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
        m_driveEvent.release();
    }
}

// 共有ボリュームを変えずアクセルオフ音の音源だけを補正するクラス
internal sealed class BackTurbinGain : System.IDisposable
{
    // 既存バンクのバックタービン音以外へ音量補正を掛けないための識別名
    readonly string sourceName;
    // 既存のバックタービン検査との互換性を維持するコンストラクター
    public BackTurbinGain() : this("ya_front_off_turbo_bov_only")
    {
    }

    // 単独音源の名前を指定して別の音へ補正しないためのコンストラクター
    public BackTurbinGain(string name)
    {
        sourceName = name;
    }

    // 補正器を取り外すための再生チャンネルと補正器の参照
    FMOD.Channel channel;
    FMOD.DSP gain;
    // 音源の生成と終了を追跡して専用音だけへ音量差を適用する関数
    public void Tick(FMOD.Studio.EventInstance sound, float decibels)
    {
        if (gain.hasHandle() && !Matches(channel))
        {
            Dispose();
        }

        if (!gain.hasHandle())
        {
            if (sound.getChannelGroup(out var root) != FMOD.RESULT.OK || !Find(root, 0, out channel))
            {
                return;
            }

            if (channel.getSystemObject(out var core) != FMOD.RESULT.OK)
            {
                return;
            }

            if (core.createDSPByType(FMOD.DSP_TYPE.FADER, out gain) != FMOD.RESULT.OK)
            {
                return;
            }

            // 接続前に音量を設定して一瞬だけ未補正の値を通すことを避ける
            gain.setParameterFloat((int)FMOD.DSP_FADER.GAIN, Mathf.Clamp(decibels, 0f, 18f));
            if (channel.addDSP(FMOD.CHANNELCONTROL_DSP_INDEX.TAIL, gain) != FMOD.RESULT.OK)
            {
                Dispose();
                return;
            }
        }

        if (gain.setParameterFloat((int)FMOD.DSP_FADER.GAIN, Mathf.Clamp(decibels, 0f, 18f)) != FMOD.RESULT.OK)
        {
            Dispose();
        }
    }

    // チャンネルが別の音へ再利用されていないことを確認する関数
    bool Matches(FMOD.Channel candidate)
    {
        if (candidate.isPlaying(out bool playing) != FMOD.RESULT.OK || !playing)
        {
            return false;
        }

        if (candidate.getCurrentSound(out var source) != FMOD.RESULT.OK)
        {
            return false;
        }

        if (source.getName(out string name, 256) != FMOD.RESULT.OK)
        {
            return false;
        }

        return System.IO.Path.GetFileNameWithoutExtension(name).Equals(sourceName, System.StringComparison.OrdinalIgnoreCase);
    }

    // Driveイベントの範囲内から対象の単独音源を探す関数
    bool Find(FMOD.ChannelGroup group, int depth, out FMOD.Channel result)
    {
        result = default;
        if (depth > 16)
        {
            return false;
        }

        if (group.getNumChannels(out int count) == FMOD.RESULT.OK)
        {
            for (int i = 0; i < count; i++)
            {
                if (group.getChannel(i, out var candidate) != FMOD.RESULT.OK || !Matches(candidate))
                {
                    continue;
                }

                result = candidate;
                return true;
            }
        }

        if (group.getNumGroups(out int children) != FMOD.RESULT.OK)
        {
            return false;
        }

        for (int i = 0; i < children; i++)
        {
            if (group.getGroup(i, out var child) == FMOD.RESULT.OK && Find(child, depth + 1, out result))
            {
                return true;
            }
        }

        return false;
    }

    // シーン終了と音源終了時に追加した補正器だけを解放する関数
    public void Dispose()
    {
        if (!gain.hasHandle())
        {
            return;
        }

        channel.removeDSP(gain);
        gain.release();
        gain.clearHandle();
    }
}

// FMODの保存データを変えず全開上限でRevSound以外の駆動音を消すクラス
internal sealed class CountdownRevIsolation : System.IDisposable
{
    // この制御が変更したグループだけ元の消音状態へ戻すための記録
    readonly System.Collections.Generic.Dictionary<FMOD.ChannelGroup, bool> m_previousMute = new();
    // 不正な音声階層を深く探索し続けないための上限
    const int MaximumDepth = 16;
    // 専用音が接続できた場合だけ通常駆動音を抑える関数
    public void Tick(FMOD.Studio.EventInstance sound, bool active)
    {
        // アクセル解除や上限からの復帰では同じフレームで通常の音へ戻す
        if (!active)
        {
            Dispose();
            return;
        }

        if (!sound.isValid() || sound.getChannelGroup(out var root) != FMOD.RESULT.OK)
        {
            Dispose();
            return;
        }

        // バンクの構成が違う場合に車の音がすべて消える事故を防ぐ
        if (!ContainsRevSource(root, 0))
        {
            Dispose();
            return;
        }

        MuteDrivingGroups(root, 0);
    }

    // ビルドでグループ名が失われても録音済みレブ音の名前で再生先を識別する関数
    public static bool ContainsRevSource(FMOD.ChannelGroup group, int depth)
    {
        if (depth > MaximumDepth)
        {
            return false;
        }

        // FMODバンク内に保存された専用音源だけを選び通常エンジン音と区別する
        if (group.getNumChannels(out int channels) == FMOD.RESULT.OK)
        {
            for (int i = 0; i < channels; i++)
            {
                if (group.getChannel(i, out var channel) != FMOD.RESULT.OK)
                {
                    continue;
                }

                if (channel.getCurrentSound(out var source) != FMOD.RESULT.OK)
                {
                    continue;
                }

                if (source.getName(out string name, 256) != FMOD.RESULT.OK)
                {
                    continue;
                }

                if (name.StartsWith("new limiter from wrc yaris_01", System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        if (group.getNumGroups(out int count) != FMOD.RESULT.OK)
        {
            return false;
        }

        // 親イベント以外へ探索を広げず他車やUIの音へ影響させない
        for (int i = 0; i < count; i++)
        {
            if (group.getGroup(i, out var child) == FMOD.RESULT.OK && ContainsRevSource(child, depth + 1))
            {
                return true;
            }
        }

        return false;
    }

    // レブ音へ通じる枝を残し同じDriveイベント内の他の枝だけ消音する関数
    void MuteDrivingGroups(FMOD.ChannelGroup group, int depth)
    {
        if (depth > MaximumDepth)
        {
            return;
        }

        if (group.getNumGroups(out int count) != FMOD.RESULT.OK)
        {
            return;
        }

        // 遅れて生成された音声グループも次フレームで消音へ含める
        for (int i = 0; i < count; i++)
        {
            if (group.getGroup(i, out var child) != FMOD.RESULT.OK)
            {
                continue;
            }

            if (ContainsRevSource(child, depth + 1))
            {
                // 再生先が再生成された時に以前の消音状態をレブ音へ残さない
                if (m_previousMute.TryGetValue(child, out bool previous))
                {
                    child.setMute(previous);
                    m_previousMute.Remove(child);
                }

                MuteDrivingGroups(child, depth + 1);
            }
            else
            {
                if (!m_previousMute.ContainsKey(child) && child.getMute(out bool muted) == FMOD.RESULT.OK)
                {
                    m_previousMute.Add(child, muted);
                }

                if (m_previousMute.ContainsKey(child))
                {
                    child.setMute(true);
                }
            }
        }
    }

    // アクセル解除や走行開始や無効化で変更前の消音状態へ戻す関数
    public void Dispose()
    {
        foreach (var item in m_previousMute)
        {
            item.Key.setMute(item.Value);
        }

        m_previousMute.Clear();
    }
}
