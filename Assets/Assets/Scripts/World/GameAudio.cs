using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Small synthesized effects, generated once so the game needs no external sound pack.</summary>
[DefaultExecutionOrder(-100)]
public sealed class GameAudio : MonoBehaviour
{
    public enum Sound { Step, Swing, Hit, Hurt, Signal }
    [Serializable]
    private sealed class SoundSettings
    {
        [Tooltip("Optional replacement. Leave empty to use the built-in sound.")]
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
    }

    private static GameAudio instance;
    public static GameAudio Instance => instance;
    private const int SampleRate = 22050;
    [Header("Master")]
    [SerializeField, Range(0f, 1f)] private float masterVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float effectsVolume = 0.55f;
    [Header("Rain ambience")]
    [SerializeField, Range(0f, 1f)] private float rainVolume = 0.08f;
    [Tooltip("Optional loop; leave empty to use the built-in rain.")]
    [SerializeField] private AudioClip rainClip;
    [Header("Individual sounds")]
    [SerializeField] private SoundSettings footsteps = new SoundSettings();
    [SerializeField, Min(0.05f)] private float footstepInterval = 0.3f;
    [SerializeField] private SoundSettings swordSwing = new SoundSettings();
    [SerializeField] private SoundSettings swordHit = new SoundSettings();
    [SerializeField] private SoundSettings playerHurt = new SoundSettings();
    [SerializeField] private SoundSettings signalFire = new SoundSettings();
    private readonly AudioClip[] clips = new AudioClip[5];
    private SoundSettings[] settings;
    private AudioClip generatedRain;
    private AudioSource effects;
    private AudioSource rain;
    private IslandWeather weather;
    private float nextStep;
    private bool muted;

    private void Awake()
    {
        instance = this;
        settings = new[] { footsteps, swordSwing, swordHit, playerHurt, signalFire };
        muted = PlayerPrefs.GetInt("Sound.Muted", 0) == 1;
        effects = gameObject.AddComponent<AudioSource>();
        effects.playOnAwake = false;
        effects.spatialBlend = 0f;
        effects.ignoreListenerPause = true;
        effects.volume = effectsVolume * masterVolume;
        rain = gameObject.AddComponent<AudioSource>();
        rain.playOnAwake = false;
        rain.spatialBlend = 0f;
        rain.loop = true;
        rain.volume = 0f;
        weather = FindAnyObjectByType<IslandWeather>();
        for (int i = 0; i < clips.Length; i++) clips[i] = CreateEffect((Sound)i);
        generatedRain = CreateRain();
        rain.clip = rainClip != null ? rainClip : generatedRain;
        rain.Play();
    }

    private void Update()
    {
        if (effects != null) effects.volume = muted ? 0f : effectsVolume * masterVolume;
        if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame && !DevConsole.IsOpen)
        {
            muted = !muted;
            PlayerPrefs.SetInt("Sound.Muted", muted ? 1 : 0);
        }
        if (rain != null)
        {
            AudioClip desiredRain = rainClip != null ? rainClip : generatedRain;
            if (rain.clip != desiredRain)
            {
                rain.clip = desiredRain;
                rain.Play();
            }
            float target = !muted && !SurvivalIntro.IsOpen && !GameOverScreen.IsGameOver && weather != null && weather.IsRaining ? rainVolume * masterVolume : 0f;
            rain.volume = Mathf.MoveTowards(rain.volume, target, Time.unscaledDeltaTime * 0.08f);
        }
    }

    public static void Play(Sound sound)
    {
        if (instance == null || instance.muted || instance.effects == null) return;
        int index = (int)sound;
        SoundSettings setting = instance.settings[index];
        var clip = setting.clip != null ? setting.clip : instance.clips[index];
        if (clip != null)
        {
            instance.effects.volume = instance.masterVolume * instance.effectsVolume;
            instance.effects.PlayOneShot(clip, setting.volume);
        }
    }

    public static void Footstep()
    {
        if (instance == null || Time.time < instance.nextStep) return;
        instance.nextStep = Time.time + instance.footstepInterval;
        Play(Sound.Step);
    }

    private static AudioClip CreateEffect(Sound sound)
    {
        float duration, start, end, noise, tone, decay;
        switch (sound)
        {
            case Sound.Step:   duration=.09f; start=120; end=70; noise=.30f; tone=.23f; decay=25; break;
            case Sound.Swing:  duration=.18f; start=460; end=110; noise=.30f; tone=.08f; decay=10; break;
            case Sound.Hit:    duration=.13f; start=175; end=70; noise=.35f; tone=.42f; decay=23; break;
            case Sound.Hurt:   duration=.19f; start=240; end=105; noise=.18f; tone=.35f; decay=12; break;
            case Sound.Signal: duration=.55f; start=360; end=720; noise=.04f; tone=.30f; decay=4; break;
            default:           duration=.26f; start=360; end=580; noise=0; tone=.23f; decay=6; break;
        }
        int length = Mathf.CeilToInt(duration * SampleRate);
        var samples = new float[length];
        var random = new System.Random(700 + (int)sound);
        float phase = 0f, filtered = 0f;
        for (int i = 0; i < length; i++)
        {
            float t = i / (float)SampleRate;
            float progress = t / duration;
            phase += 2f * Mathf.PI * Mathf.Lerp(start, end, progress) / SampleRate;
            filtered = Mathf.Lerp(filtered, (float)(random.NextDouble() * 2 - 1), 0.18f);
            float attack = Mathf.Min(1f, t / .008f);
            float envelope = attack * Mathf.Exp(-decay * t) * Mathf.Clamp01((duration - t) / .025f);
            samples[i] = Mathf.Clamp((Mathf.Sin(phase) * tone + filtered * noise) * envelope, -1f, 1f);
        }
        var clip = AudioClip.Create("SFX " + sound, length, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private static AudioClip CreateRain()
    {
        int length = SampleRate * 3;
        var samples = new float[length];
        var random = new System.Random(381);
        float filtered = 0f;
        for (int i = 0; i < length; i++)
        {
            filtered = Mathf.Lerp(filtered, (float)(random.NextDouble() * 2 - 1), .22f);
            float edge = Mathf.Min(1f, Mathf.Min(i, length - 1 - i) / (SampleRate * .05f));
            samples[i] = filtered * edge * .65f;
        }
        var clip = AudioClip.Create("Rain ambience", length, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        foreach (var clip in clips) if (clip != null) Destroy(clip);
        if (generatedRain != null) Destroy(generatedRain);
    }
}
