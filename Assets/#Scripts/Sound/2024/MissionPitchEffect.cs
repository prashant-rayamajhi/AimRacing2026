using System;

// 既存バンクのミッション音だけへ音程の揺れを加えるクラス
public sealed class MissionPitchEffect : IDisposable
{
    // 他の音を誤って加工しないため照合する既存音源名
    const string m_sourceName = "transmission_stereo";
    // 音程倍率をセントから求めるための一オクターブの単位数
    const float m_centsPerOctave = 1200f;
    // 循環や深すぎる音声経路を探索し続けないための上限
    const int m_maximumDepth = 16;
    // ミッション音にだけ接続した補正器を解放するための参照
    FMOD.DSP m_effect;
    // 補正器を取り外すための対象チャンネル
    FMOD.Channel m_channel;
    // 参照を取得できない期間に毎フレーム探索しないための待ち時間
    float m_retryRemaining;
    // 減衰振動の経過時間と連続した位相
    float m_elapsed = float.PositiveInfinity;
    double m_phase;
    // 検証ログで音程補正の接続状態を確認するための値
    public bool Connected { get; private set; }
    // 音程補正の実測確認に使う直近の倍率
    public float LastRatio { get; private set; } = 1f;

    // 変速や踏み直しから始まる減衰サイン波をミッション音だけへ渡す関数
    public void Tick(FMOD.Studio.EventInstance _sound, bool _trigger, bool _allowed, float _rpmRatio, float _deltaTime, float _duration, float _cents, float _lowHz, float _highHz, float _baseSemitones = 0f)
    {
        // 無効化した場合は既存音へ戻し補正器も解放する
        if (!_allowed)
        {
            Dispose();
            return;
        }

        float dt = Math.Max(0f, _deltaTime);
        if (_trigger)
        {
            m_elapsed = 0f;
            m_phase = 0d;
        }

        // 音源の遅延読み込みやチャンネル再生成後も安全に接続し直す
        if (Connected && !IsMissionChannel(m_channel))
        {
            Dispose();
        }

        if (!Connected)
        {
            m_retryRemaining -= dt;
            if (m_retryRemaining <= 0f)
            {
                m_retryRemaining = 0.5f;
                Connect(_sound);
            }
        }

        // 演出終了時は元の速度とギアによる音程をそのまま通す
        float ratio = 1f;
        if (m_elapsed < Math.Max(0.01f, _duration))
        {
            float rpm = Math.Max(0f, Math.Min(1f, _rpmRatio));
            float hz = Math.Max(0f, _lowHz + (_highHz - _lowHz) * rpm);
            m_phase += 2d * Math.PI * hz * dt;
            m_elapsed += dt;
            float envelope = Math.Max(0f, 1f - m_elapsed / Math.Max(0.01f, _duration));
            double offset = Math.Sin(m_phase) * envelope * envelope * Math.Max(0f, Math.Min(100f, _cents));
            ratio = (float)Math.Pow(2d, offset / m_centsPerOctave);
        }

        // 速度とギアの目標音程へ短い揺れを重ねFMOD補正器の有効範囲へ収める
        ratio *= (float)Math.Pow(2d, _baseSemitones / 12d);
        ratio = Math.Max(0.5f, Math.Min(2f, ratio));
        LastRatio = ratio;
        // API失敗時は音声全体へ代替適用せずこの補正だけを外す
        if (Connected && m_effect.setParameterFloat((int)FMOD.DSP_PITCHSHIFT.PITCH, ratio) != FMOD.RESULT.OK)
        {
            Dispose();
        }
    }

    // 再生チャンネルが別音源へ再利用された場合に補正を続けないための関数
    static bool IsMissionChannel(FMOD.Channel _candidate)
    {
        if (_candidate.isPlaying(out bool playing) != FMOD.RESULT.OK || !playing)
        {
            return false;
        }

        if (_candidate.getCurrentSound(out var source) != FMOD.RESULT.OK)
        {
            return false;
        }

        return source.getName(out string name, 256) == FMOD.RESULT.OK && name == m_sourceName;
    }

    // 名前が一致する単独音源へ補正器を接続する関数
    void Connect(FMOD.Studio.EventInstance _sound)
    {
        if (!_sound.isValid() || _sound.getChannelGroup(out var root) != FMOD.RESULT.OK)
        {
            return;
        }

        if (!Find(root, 0, out m_channel))
        {
            return;
        }

        if (m_channel.getSystemObject(out var core) != FMOD.RESULT.OK)
        {
            return;
        }

        if (core.createDSPByType(FMOD.DSP_TYPE.PITCHSHIFT, out m_effect) != FMOD.RESULT.OK)
        {
            return;
        }

        // 一倍から開始し接続した瞬間の音程飛びを防ぐ
        if (m_effect.setParameterFloat((int)FMOD.DSP_PITCHSHIFT.PITCH, 1f) != FMOD.RESULT.OK || m_channel.addDSP(FMOD.CHANNELCONTROL_DSP_INDEX.TAIL, m_effect) != FMOD.RESULT.OK)
        {
            Dispose();
            return;
        }

        Connected = true;
    }

    // イベント内だけを探索してミッション音のチャンネルを取得する関数
    static bool Find(FMOD.ChannelGroup _group, int _depth, out FMOD.Channel _result)
    {
        _result = default;
        if (_depth > m_maximumDepth || _group.getNumChannels(out int count) != FMOD.RESULT.OK)
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            if (_group.getChannel(i, out var candidate) != FMOD.RESULT.OK)
            {
                continue;
            }

            if (candidate.getCurrentSound(out var source) != FMOD.RESULT.OK)
            {
                continue;
            }

            if (source.getName(out string name, 256) != FMOD.RESULT.OK || name != m_sourceName)
            {
                continue;
            }

            _result = candidate;
            return true;
        }

        if (_group.getNumGroups(out int children) != FMOD.RESULT.OK)
        {
            return false;
        }

        for (int i = 0; i < children; i++)
        {
            if (_group.getGroup(i, out var child) == FMOD.RESULT.OK && Find(child, _depth + 1, out _result))
            {
                return true;
            }
        }

        return false;
    }

    // 無効化やシーン終了時に追加した補正器だけを解放する関数
    public void Dispose()
    {
        if (m_effect.hasHandle())
        {
            m_channel.removeDSP(m_effect);
            m_effect.release();
            m_effect.clearHandle();
        }

        Connected = false;
        LastRatio = 1f;
        m_elapsed = float.PositiveInfinity;
        m_phase = 0d;
    }
}
