using UnityEngine;

// 観客の拍手、歓声、呼びかけをランダムに切り替えるクラス
// 観客アニメーションのフレームを毎フレーム更新から変更
public class PeopleAnimScript : MonoBehaviour
{
    // 観客アニメーションの状態を切り替えるためのAnimator
    private Animator m_visitorAnimator;
    // 各Animatorパラメーターを高速かつ安全に指定するためのID
    private static readonly int GoClapId = Animator.StringToHash("goClap");
    private static readonly int GoCoolId = Animator.StringToHash("goCool");
    private static readonly int GoCallId = Animator.StringToHash("goCall");
    private static readonly int IsChangeableId = Animator.StringToHash("isChangeble");
    [Header("軽量化設定")]
    [SerializeField, Range(1, 30)]
    private int m_updateFrameInterval = 15;
    // 現在の観客アニメーションを維持する残りフレーム数
    private int m_animationFrames;
    // 観客ごとにアニメーション開始をずらす残りフレーム数
    private int m_waitFrames;
    // 拍手、歓声、呼びかけのうち再生するアニメーション番号
    private int m_animationIndex;
    // 観客ごとにUpdateタイミングをずらす値
    private int m_updateFrameOffset;
    // Animatorに存在するパラメーターだけを操作するための判定値
    private bool m_hasGoClap;
    private bool m_hasGoCool;
    private bool m_hasGoCall;
    private bool m_hasIsChangeable;
    // 前回Animatorへ送った値
    private bool m_lastGoClap;
    private bool m_lastGoCool;
    private bool m_lastGoCall;
    private bool m_lastIsChangeable;
    // 初回反映用
    private bool m_hasAppliedAnimatorParameters;
    // Animatorを取得して観客ごとの再生タイミングを初期化する関数
    private void Start()
    {
        m_visitorAnimator = GetComponent<Animator>();
        if (m_visitorAnimator == null)
        {
            enabled = false;
            return;
        }

        m_hasGoClap = HasBoolParameter(GoClapId);
        m_hasGoCool = HasBoolParameter(GoCoolId);
        m_hasGoCall = HasBoolParameter(GoCallId);
        m_hasIsChangeable = HasBoolParameter(IsChangeableId);
        m_animationIndex = Random.Range(0, 3);
        m_animationFrames = Random.Range(1, 10) * 100;
        m_waitFrames = Random.Range(0, 120);
        // 観客ごとに処理フレームを分散させる
        m_updateFrameOffset = Random.Range(0, Mathf.Max(1, m_updateFrameInterval));
        ApplyAnimatorParameters(true);
    }

    // 観客の現在状態に合わせて次のアニメーションへ切り替える関数
    private void Update()
    {
        // 毎フレーム全観客を更新しない
        if ((Time.frameCount + m_updateFrameOffset) % m_updateFrameInterval != 0)
        {
            return;
        }

        int frameStep = Mathf.Max(1, m_updateFrameInterval);
        if (m_waitFrames > 0)
        {
            m_waitFrames -= frameStep;
            if (m_waitFrames < 0)
            {
                m_waitFrames = 0;
            }

            return;
        }

        AnimatorStateInfo state = m_visitorAnimator.GetCurrentAnimatorStateInfo(0);
        bool isMainAnimation = state.IsTag("ClapMain") || state.IsTag("CoolMain") || state.IsTag("CallMain");
        if (!isMainAnimation && m_animationFrames == 0)
        {
            m_animationFrames = Random.Range(2, 10) * 50;
        }

        if (isMainAnimation && m_animationFrames > 0)
        {
            m_animationFrames -= frameStep;
            if (m_animationFrames <= 0)
            {
                m_animationFrames = 0;
                m_animationIndex = Random.Range(0, 3);
            }
        }

        ApplyAnimatorParameters(!isMainAnimation || m_animationFrames == 0);
    }

    // 現在選択中のアニメーションを存在するAnimatorパラメーターへ反映する関数
    private void ApplyAnimatorParameters(bool changeable)
    {
        bool goClap = m_animationIndex == 0;
        bool goCool = m_animationIndex == 1;
        bool goCall = m_animationIndex == 2;
        if (m_hasGoClap)
        {
            SetBoolIfChanged(GoClapId, goClap, ref m_lastGoClap);
        }

        if (m_hasGoCool)
        {
            SetBoolIfChanged(GoCoolId, goCool, ref m_lastGoCool);
        }

        if (m_hasGoCall)
        {
            SetBoolIfChanged(GoCallId, goCall, ref m_lastGoCall);
        }

        if (m_hasIsChangeable)
        {
            SetBoolIfChanged(IsChangeableId, changeable, ref m_lastIsChangeable);
        }

        m_hasAppliedAnimatorParameters = true;
    }

    // 前回と違う値の時だけAnimatorへ反映する
    private void SetBoolIfChanged(int parameterId, bool value, ref bool lastValue)
    {
        if (m_hasAppliedAnimatorParameters && lastValue == value)
        {
            return;
        }

        m_visitorAnimator.SetBool(parameterId, value);
        lastValue = value;
    }

    // 指定したBoolパラメーターがAnimator Controllerに存在するか確認する関数
    private bool HasBoolParameter(int parameterId)
    {
        AnimatorControllerParameter[] parameters = m_visitorAnimator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].nameHash == parameterId && parameters[i].type == AnimatorControllerParameterType.Bool)
            {
                return true;
            }
        }

        return false;
    }
}
