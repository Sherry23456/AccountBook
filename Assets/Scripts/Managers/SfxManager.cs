using UnityEngine;

/// <summary>
/// 轻量程序合成音效（步骤08）：按钮点击"嗒" + 记账成功"叮咚"。
/// AudioClip 全由代码生成，无外部音频资产，不受导入管线影响；
/// AfterSceneLoad 自建常驻物体，实例缺失（异常场景）时静态入口静默跳过不报错。
/// </summary>
public class SfxManager : MonoBehaviour
{
    private const int SampleRate = 44100;
    private const float ClickVolume = 0.8f;
    private const float SuccessVolume = 0.9f;
    private const string SoundPrefKey = "SfxEnabled";

    private static SfxManager instance;

    private AudioSource audioSource;
    private AudioClip clickClip;
    private AudioClip successClip;

    /// <summary>
    /// 全局音效开关（PlayerPrefs 持久化，缺省开）；顶栏 SfxToggleUI 负责 UI 切换
    /// </summary>
    public static bool SoundEnabled => PlayerPrefs.GetInt(SoundPrefKey, 1) == 1;

    /// <summary>
    /// 切换并立即落盘；已在播的短音效不截断，自然结束
    /// </summary>
    public static void SetSoundEnabled(bool enabled)
    {
        PlayerPrefs.SetInt(SoundPrefKey, enabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance == null)
        {
            new GameObject("SfxManager").AddComponent<SfxManager>();
        }
    }

    /// <summary>
    /// 按钮点击音：各面板 RegisterButton 统一挂接，先于业务回调注册保证反馈必响
    /// </summary>
    public static void PlayClick()
    {
        if (SoundEnabled == false || instance == null || instance.clickClip == null)
        {
            return;
        }

        instance.audioSource.PlayOneShot(instance.clickClip, ClickVolume);
    }

    /// <summary>
    /// 成功音：记账新增/编辑落库成功后调用
    /// </summary>
    public static void PlaySuccess()
    {
        if (SoundEnabled == false || instance == null || instance.successClip == null)
        {
            return;
        }

        instance.audioSource.PlayOneShot(instance.successClip, SuccessVolume);
    }

    private void Awake()
    {
        instance = this;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        clickClip = BuildClickClip();
        successClip = BuildSuccessClip();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    /// <summary>
    /// 短促"嗒"：1.6kHz 主音 + 倍频点缀，指数衰减 60ms，2ms 起音防爆音
    /// </summary>
    private static AudioClip BuildClickClip()
    {
        const float Duration = 0.06f;
        int samples = Mathf.CeilToInt(Duration * SampleRate);
        float[] data = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / SampleRate;
            float envelope = Mathf.Exp(-t * 70f) * Mathf.Min(1f, t / 0.002f);
            data[i] = (Mathf.Sin(2f * Mathf.PI * 1600f * t) * 0.75f
                       + Mathf.Sin(2f * Mathf.PI * 3200f * t) * 0.25f) * envelope * 0.6f;
        }

        return BuildClip("sfx_click", data);
    }

    /// <summary>
    /// "叮咚"双音：A5(880Hz)→E6(1318.5Hz) 纯五度，第二音延迟 120ms 进入，铃音泛音 + 指数衰减
    /// </summary>
    private static AudioClip BuildSuccessClip()
    {
        const float Duration = 0.7f;
        int samples = Mathf.CeilToInt(Duration * SampleRate);
        float[] data = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / SampleRate;
            data[i] = (Chime(t, 880f) + Chime(t - 0.12f, 1318.5f)) * 0.35f;
        }

        return BuildClip("sfx_success", data);
    }

    /// <summary>
    /// 单个铃音：基频 + 二/三次泛音，起音 4ms、衰减系数 7/s
    /// </summary>
    private static float Chime(float t, float freq)
    {
        if (t < 0f)
        {
            return 0f;
        }

        float envelope = Mathf.Exp(-t * 7f) * Mathf.Min(1f, t / 0.004f);
        return (Mathf.Sin(2f * Mathf.PI * freq * t)
                + Mathf.Sin(2f * Mathf.PI * freq * 2f * t) * 0.4f
                + Mathf.Sin(2f * Mathf.PI * freq * 3f * t) * 0.15f) * envelope * 0.5f;
    }

    private static AudioClip BuildClip(string clipName, float[] data)
    {
        AudioClip clip = AudioClip.Create(clipName, data.Length, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
