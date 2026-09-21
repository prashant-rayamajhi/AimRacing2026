using System;

// 既存バンクのミッション音だけへ音程の揺れを加えるクラス
public sealed class MissionPitchEffect : IDisposable
{
    // 他の音を誤って加工しないため照合する既存音源名
    const string SourceName = "transmission_stereo";
    // 音程倍率をセントから求めるための一オクターブの単位数
    const float CentsPerOctave = 1200f;
    // 循環や深すぎる音声経路を探索し続けないための上限
    const int MaximumDepth = 16;
    // ミッション音にだけ接続した補正器を解放するための参照
    FMOD.DSP effect;
    // 補正器を取り外すための対象チャンネル
    FMOD.Channel channel;
    // 参照を取得できない期間に毎フレーム探索しないための待ち時間
    float retryRemaining;
    // 減衰振動の経過時間と連続した位相
    float elapsed = float.PositiveInfinity;
    double phase;
    // 検証ログで音程補正の接続状態を確認するための値
    public bool Connected { get; private set; }
    // 音程補正の実測確認に使う直近の倍率
    public float LastRatio { get; private set; } = 1f;

    // 変速や踏み直しから始まる減衰サイン波をミッション音だけへ渡す関数
    public void Tick(FMOD.Studio.EventInstance sound, bool trigger, bool allowed, float rpmRatio, float deltaTime, float duration, float cents, float lowHz, float highHz, float baseSemitones = 0f)
    {
        // 無効化した場合は既存音へ戻し補正器も解放する
        if (!allowed)
        {
            Dispose();
            return;
        }

        float dt = Math.Max(0f, deltaTime);
        if (trigger)
        {
            elapsed = 0f;
            phase = 0d;
        }

        // 音源の遅延読み込みやチャンネル再生成後も安全に接続し直す
        if (Connected && !IsMissionChannel(channel))
        {
            Dispose();
        }

        if (!Connected)
        {
            retryRemaining -= dt;
            if (retryRemaining <= 0f)
            {
                retryRemaining = 0.5f;
                Connect(sound);
            }
        }

        // 演出終了時は元の速度とギアによる音程をそのまま通す
        float ratio = 1f;
        if (elapsed < Math.Max(0.01f, duration))
        {
            float rpm = Math.Max(0f, Math.Min(1f, rpmRatio));
            float hz = Math.Max(0f, lowHz + (highHz - lowHz) * rpm);
            phase += 2d * Math.PI * hz * dt;
            elapsed += dt;
            float envelope = Math.Max(0f, 1f - elapsed / Math.Max(0.01f, duration));
            double offset = Math.Sin(phase) * envelope * envelope * Math.Max(0f, Math.Min(100f, cents));
            ratio = (float)Math.Pow(2d, offset / CentsPerOctave);
        }

        // 速度とギアの目標音程へ短い揺れを重ねFMOD補正器の有効範囲へ収める
        ratio *= (float)Math.Pow(2d, baseSemitones / 12d);
        ratio = Math.Max(0.5f, Math.Min(2f, ratio));
        LastRatio = ratio;
        // API失敗時は音声全体へ代替適用せずこの補正だけを外す
        if (Connected && effect.setParameterFloat((int)FMOD.DSP_PITCHSHIFT.PITCH, ratio) != FMOD.RESULT.OK)
        {
            Dispose();
        }
    }

    // 再生チャンネルが別音源へ再利用された場合に補正を続けないための関数
    static bool IsMissionChannel(FMOD.Channel candidate)
    {
        if (candidate.isPlaying(out bool playing) != FMOD.RESULT.OK || !playing)
        {
            return false;
        }

        if (candidate.getCurrentSound(out var source) != FMOD.RESULT.OK)
        {
            return false;
        }

        return source.getName(out string name, 256) == FMOD.RESULT.OK && name == SourceName;
    }

    // 名前が一致する単独音源へ補正器を接続する関数
    void Connect(FMOD.Studio.EventInstance sound)
    {
        if (!sound.isValid() || sound.getChannelGroup(out var root) != FMOD.RESULT.OK)
        {
            return;
        }

        if (!Find(root, 0, out channel))
        {
            return;
        }

        if (channel.getSystemObject(out var core) != FMOD.RESULT.OK)
        {
            return;
        }

        if (core.createDSPByType(FMOD.DSP_TYPE.PITCHSHIFT, out effect) != FMOD.RESULT.OK)
        {
            return;
        }

        // 一倍から開始し接続した瞬間の音程飛びを防ぐ
        if (effect.setParameterFloat((int)FMOD.DSP_PITCHSHIFT.PITCH, 1f) != FMOD.RESULT.OK || channel.addDSP(FMOD.CHANNELCONTROL_DSP_INDEX.TAIL, effect) != FMOD.RESULT.OK)
        {
            Dispose();
            return;
        }

        Connected = true;
    }

    // イベント内だけを探索してミッション音のチャンネルを取得する関数
    static bool Find(FMOD.ChannelGroup group, int depth, out FMOD.Channel result)
    {
        result = default;
        if (depth > MaximumDepth || group.getNumChannels(out int count) != FMOD.RESULT.OK)
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            if (group.getChannel(i, out var candidate) != FMOD.RESULT.OK)
            {
                continue;
            }

            if (candidate.getCurrentSound(out var source) != FMOD.RESULT.OK)
            {
                continue;
            }

            if (source.getName(out string name, 256) != FMOD.RESULT.OK || name != SourceName)
            {
                continue;
            }

            result = candidate;
            return true;
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

    // 無効化やシーン終了時に追加した補正器だけを解放する関数
    public void Dispose()
    {
        if (effect.hasHandle())
        {
            channel.removeDSP(effect);
            effect.release();
            effect.clearHandle();
        }

        Connected = false;
        LastRatio = 1f;
        elapsed = float.PositiveInfinity;
        phase = 0d;
    }
}
