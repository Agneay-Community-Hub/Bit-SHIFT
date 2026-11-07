// Every sound in BITSHIFT is synthesized at startup; there are no audio files.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BitShift
{
    public class Sfx : MonoBehaviour
    {
        const int Rate = 22050;
        static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        AudioSource fx, music;
        public bool musicOn = true, sfxOn = true;

        public static Sfx I;

        void Awake()
        {
            I = this;
            fx = gameObject.AddComponent<AudioSource>();
            fx.playOnAwake = false;
            music = gameObject.AddComponent<AudioSource>();
            music.loop = true;
            music.volume = 0.32f;
            Generate();
            music.clip = clips["music"];
            musicOn = PlayerPrefs.GetInt("music", 1) == 1;
            sfxOn = PlayerPrefs.GetInt("sfx", 1) == 1;
            if (musicOn) music.Play();
        }

        public void ToggleMusic()
        {
            musicOn = !musicOn;
            PlayerPrefs.SetInt("music", musicOn ? 1 : 0);
            if (musicOn) music.Play(); else music.Stop();
        }

        public void ToggleSfx()
        {
            sfxOn = !sfxOn;
            PlayerPrefs.SetInt("sfx", sfxOn ? 1 : 0);
        }

        public void Play(string name, float vol = 1f, float pitch = 1f)
        {
            if (!sfxOn || !clips.TryGetValue(name, out var c)) return;
            fx.pitch = pitch;
            fx.PlayOneShot(c, vol);
        }

        // ------------------------------------------------------------ synthesis
        static float Sq(float f, float t, float duty = 0.5f) => Mathf.Repeat(f * t, 1f) < duty ? 1f : -1f;
        static float Tri(float f, float t) => 1f - 4f * Mathf.Abs(Mathf.Repeat(f * t + 0.25f, 1f) - 0.5f);
        static float Saw(float f, float t) => 2f * Mathf.Repeat(f * t, 1f) - 1f;
        static float Note(int semisFromA4) => 440f * Mathf.Pow(2f, semisFromA4 / 12f);

        static void Make(string name, float dur, Func<float, float> f)
        {
            int n = Mathf.CeilToInt(dur * Rate);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float fade = Mathf.Clamp01((dur - t) * 60f) * Mathf.Clamp01(t * 400f);
                d[i] = Mathf.Clamp(f(t) * fade, -1f, 1f);
            }
            var c = AudioClip.Create(name, n, 1, Rate, false);
            c.SetData(d, 0);
            clips[name] = c;
        }

        static readonly int[] Arp = { 0, 4, 7, 12 }, Mel = { 0, 4, 7, 12, 16, 19 };

        static void Generate()
        {
            if (clips.Count > 0) return;
            var rng = new System.Random(7);
            float[] noise = new float[Rate * 2];
            for (int i = 0; i < noise.Length; i++) noise[i] = (float)(rng.NextDouble() * 2 - 1);
            float Noise(float t) => noise[(int)(t * Rate) % noise.Length];

            Make("step", 0.05f, t => Sq(180f, t, 0.25f) * 0.12f * Mathf.Exp(-t * 60f));
            Make("bump", 0.08f, t => Sq(70f, t) * 0.18f * Mathf.Exp(-t * 40f));
            Make("push", 0.12f, t => (Sq(110f - t * 300f, t) * 0.2f + Noise(t) * 0.15f) * Mathf.Exp(-t * 25f));
            Make("shl", 0.22f, t => Sq(300f + t * 2400f, t, 0.3f) * 0.22f * Mathf.Exp(-t * 9f));
            Make("shr", 0.22f, t => Sq(820f - t * 2400f, t, 0.3f) * 0.22f * Mathf.Exp(-t * 9f));
            Make("rol", 0.3f, t => Sq(500f + Mathf.Sin(t * 70f) * 200f, t, 0.4f) * 0.2f * Mathf.Exp(-t * 7f));
            Make("err", 0.28f, t => (Saw(62f, t) * 0.3f + Sq(93f, t) * 0.15f) * Mathf.Exp(-t * 6f));
            Make("none", 0.08f, t => Sq(240f, t, 0.125f) * 0.1f * Mathf.Exp(-t * 40f));
            Make("free", 0.55f, t =>
            {
                int step = Mathf.Min(4, (int)(t / 0.09f));
                float f = Note(12 - step * 5);
                return (Sq(f, t, 0.25f) * 0.18f + Noise(t) * 0.12f * Mathf.Exp(-t * 6f)) * Mathf.Exp(-t * 4f);
            });
            Make("lost", 0.18f, t => Tri(1400f - t * 5000f, t) * 0.25f * Mathf.Exp(-t * 14f));
            Make("fill", 0.4f, t => (Sq(55f - t * 40f, t) * 0.25f + Noise(t) * 0.25f * Mathf.Exp(-t * 15f)) * Mathf.Exp(-t * 7f));
            Make("unlock", 0.45f, t =>
            {
                int step = Mathf.Min(3, (int)(t / 0.08f));
                return Sq(Note(Arp[step] + 3), t, 0.25f) * 0.16f * Mathf.Exp(-(t - step * 0.08f) * 8f);
            });
            Make("win", 1.1f, t =>
            {
                int step = Mathf.Min(5, (int)(t / 0.1f));
                float lt = t - step * 0.1f;
                return (Sq(Note(Mel[step] + 3), t, 0.25f) * 0.16f + Tri(Note(Mel[step] - 9), t) * 0.2f)
                       * Mathf.Exp(-lt * (step == 5 ? 2.5f : 10f));
            });
            Make("select", 0.06f, t => Sq(660f, t, 0.25f) * 0.12f * Mathf.Exp(-t * 50f));
            Make("music", MusicLength, t => Music(t, Noise));
        }

        // A tiny 8-bar sequencer: square bass, triangle arpeggio, noise hats.
        const float Bpm = 104f;
        static float StepDur => 60f / Bpm / 4f;
        static float MusicLength => StepDur * 16 * 8;

        static readonly int[] roots = { -12, -16, -9, -14, -12, -16, -7, -10 }; // A, F, C, G ... (semitones from A4)
        static readonly int[][] chord = { new[] { 0, 3, 7, 12 }, new[] { 0, 4, 7, 12 }, new[] { 0, 4, 7, 12 }, new[] { 0, 4, 7, 12 } };
        static readonly int[] patA = { 0, 1, 2, 3, 2, 1, 2, 3 }, patB = { 3, 2, 1, 0, 1, 2, 3, 2 };

        static float Music(float t, Func<float, float> noise)
        {
            int step = (int)(t / StepDur);
            float lt = t - step * StepDur;
            int bar = (step / 16) % 8;
            int s16 = step % 16;
            int root = roots[bar];
            var ch = chord[bar % 4];

            float v = 0f;
            // bass: eighth notes, octave bounce
            int s8 = s16 / 2;
            float blt = t - (step - (s16 % 2)) * StepDur;
            float bf = Note(root - 12 + ((s8 % 2 == 1) ? 12 : 0));
            v += Sq(bf, t, 0.5f) * 0.16f * Mathf.Exp(-blt * 5f);
            // arp
            int[] pattern = bar < 4 ? patA : patB;
            int tone = ch[pattern[s16 % 8]];
            v += Tri(Note(root + 12 + tone), t) * 0.13f * Mathf.Exp(-lt * 9f);
            // lead blips on some steps in the second half
            if (bar >= 4 && (s16 == 0 || s16 == 6 || s16 == 10))
                v += Sq(Note(root + 24 + ch[(s16 / 4) % 4]), t, 0.125f) * 0.05f * Mathf.Exp(-lt * 6f);
            // hats on offbeats, kick on beats
            if (s16 % 4 == 2) v += noise(t) * 0.06f * Mathf.Exp(-lt * 45f);
            if (s16 % 4 == 0) v += Mathf.Sin(2 * Mathf.PI * (50f + 120f * Mathf.Exp(-lt * 30f)) * lt) * 0.22f * Mathf.Exp(-lt * 12f);
            return v;
        }
    }
}
