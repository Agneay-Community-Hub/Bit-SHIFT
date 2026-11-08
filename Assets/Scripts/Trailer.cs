// Scripted trailer recorder. Only active when the player is launched with:
//   BITSHIFT.exe -trailer <frames-dir> -screen-width 1280 -screen-height 720 -screen-fullscreen 0
// It plays solved levels automatically, overlays title cards, and writes one JPG per frame at 30 fps
// plus a WAV of the game's audio (via AudioRenderer). tools/make_trailer.sh stitches them with ffmpeg.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEngine;

namespace BitShift
{
    public class Trailer : MonoBehaviour
    {
        const int Fps = 30;
        string outDir;
        Game game;
        int frame;
        bool recording;
        readonly List<float> audio = new List<float>();
        int channels;

        // card overlay state
        string cardTitle, cardSub;
        float cardTime, cardAlpha;
        bool cardLogo;
        GUIStyle sTitle, sSub, sLogo;
        Texture2D black;

        static readonly Color Accent = new Color(0.22f, 1f, 0.69f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-trailer")
                {
                    var t = new GameObject("Trailer").AddComponent<Trailer>();
                    t.outDir = args[i + 1];
                }
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir, "*.jpg")) File.Delete(f);
            Time.captureFramerate = Fps;
            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
            black = new Texture2D(1, 1);
            black.SetPixel(0, 0, Color.black);
            black.Apply();
            yield return null;
            yield return null;
            game = FindAnyObjectByType<Game>();
            if (Sfx.I != null && !Sfx.I.musicOn) Sfx.I.ToggleMusic();
            if (Sfx.I != null && !Sfx.I.sfxOn) Sfx.I.ToggleSfx();

            channels = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;
            AudioRenderer.Start();
            recording = true;
            StartCoroutine(Capture());
            yield return Script();
            recording = false;
            yield return null;
            AudioRenderer.Stop();
            WriteWav(Path.Combine(outDir, "audio.wav"), audio, channels, AudioSettings.outputSampleRate);
            Debug.Log($"[Trailer] wrote {frame} frames and {audio.Count / channels} audio samples to {outDir}");
            Application.Quit();
        }

        IEnumerator Capture()
        {
            var eof = new WaitForEndOfFrame();
            while (recording)
            {
                yield return eof;
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(outDir, $"f{frame:00000}.jpg"), tex.EncodeToJPG(92));
                Destroy(tex);
                frame++;

                int n = AudioRenderer.GetSampleCountForCaptureFrame();
                if (n > 0)
                {
                    var buf = new NativeArray<float>(n * channels, Allocator.Temp);
                    AudioRenderer.Render(buf);
                    audio.AddRange(buf);
                    buf.Dispose();
                }
            }
        }

        // ------------------------------------------------------------------ the storyboard
        IEnumerator Script()
        {
            var sols = new Dictionary<string, string[]>();
            foreach (var line in Resources.Load<TextAsset>("solutions").text.Split('\n'))
            {
                int c = line.IndexOf(':');
                if (c > 0) sols[line.Substring(0, c).Trim()] = line.Substring(c + 1).Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            }
            var titles = new List<string>();
            foreach (var line in Resources.Load<TextAsset>("levels").text.Split('\n'))
                if (line.StartsWith("===")) titles.Add(line.Substring(3).Trim());

            yield return Card("every block is a number.", "and its value is its size.", 2.6f);
            game.ShowTitle();
            yield return new WaitForSeconds(5.0f);

            yield return Card("<<  doubles it.", "", 1.8f);
            yield return Play(titles, sols, "HELLO_WORLD", 0.12f, 0.55f, 1.4f);

            yield return Card(">>  halves it.", "", 1.8f);
            yield return Play(titles, sols, "HALF_PRECISION", 0.09f, 0.45f, 1.2f);

            yield return Card("fill the gaps.", "a block that fits a pit exactly becomes floor.", 2.2f);
            yield return Play(titles, sols, "BRIDGE_BUILDER", 0.09f, 0.45f, 1.2f);

            yield return Card("but registers are small...", "0b101 << 1  =  0b010  in 3 bits", 2.4f);
            yield return Play(titles, sols, "TRUNCATE", 0.13f, 0.7f, 1.4f);

            yield return Card("run out of bits", "and you run out of block.", 2.2f);
            yield return Play(titles, sols, "NULL_POINTER", 0.16f, 0.8f, 1.4f);

            yield return Card("rotate.  wrap.  survive.", "", 1.8f);
            yield return Play(titles, sols, "ROTATE_LEFT", 0.08f, 0.45f, 1.2f);

            yield return Card("11 programs.  4 instructions.  1 theme.", "", 2.2f);
            yield return Play(titles, sols, "SHIFT_HAPPENS", 0.06f, 0.35f, 1.6f);

            yield return Card("BITSHIFT", "a programming puzzler about running out of bits", 5.0f, logo: true);
        }

        IEnumerator Card(string title, string sub, float hold, bool logo = false)
        {
            cardTitle = title;
            cardSub = sub;
            cardLogo = logo;
            cardTime = 0f;
            float fade = 0.25f;
            for (float t = 0; t < hold; t += Time.deltaTime)
            {
                cardTime = t;
                cardAlpha = Mathf.Min(1f, t / fade, logo ? 1f : (hold - t) / fade);
                if (logo && Mathf.Abs(t - 0.05f) < 0.02f && Sfx.I != null) Sfx.I.Play("unlock");
                yield return null;
            }
            if (!logo) cardTitle = null;
        }

        IEnumerator Play(List<string> titles, Dictionary<string, string[]> sols, string title, float step,
                         float shiftPause = 0.55f, float after = 1.8f)
        {
            int idx = titles.IndexOf(title);
            game.LoadLevel(idx);
            yield return new WaitForSeconds(0.5f);
            foreach (var m in sols[title])
            {
                int a = m switch
                {
                    "U" => 0, "R" => 1, "D" => 2, "L" => 3,
                    "shl" => 4 + (int)Op.Shl, "shr" => 4 + (int)Op.Shr, "rol" => 4 + (int)Op.Rol, _ => 4 + (int)Op.Ror
                };
                bool shift = a >= 4;
                if (shift) yield return new WaitForSeconds(Mathf.Min(0.25f, shiftPause * 0.4f));
                game.DoAction(a);
                yield return new WaitForSeconds(shift ? shiftPause : step);
            }
            yield return new WaitForSeconds(after);
        }

        // ------------------------------------------------------------------ overlay
        void OnGUI()
        {
            if (string.IsNullOrEmpty(cardTitle)) return;
            GUI.depth = -100;
            if (sTitle == null)
            {
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                sTitle = new GUIStyle { font = font, fontSize = 64, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = true };
                sTitle.normal.textColor = Color.white;
                sSub = new GUIStyle(sTitle) { fontSize = 28, fontStyle = FontStyle.Normal };
                sSub.normal.textColor = new Color(0.62f, 0.7f, 0.8f);
                sLogo = new GUIStyle(sTitle) { fontSize = 150 };
            }
            float k = Screen.height / 720f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1));
            float vw = Screen.width / k, vh = 720f;
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, cardAlpha);
            GUI.DrawTexture(new Rect(-10, -10, vw + 20, vh + 20), black);

            if (cardLogo)
            {
                // the logo assembles itself by shifting in from the right, one letter at a time
                string word = "BITSHIFT";
                int shown = Mathf.Clamp((int)(cardTime / 0.09f), 0, word.Length);
                string acc = ColorUtility.ToHtmlStringRGB(Accent);
                string txt = $"<color=#{acc}>{word.Substring(0, Mathf.Min(shown, 3))}</color>{(shown > 3 ? word.Substring(3, shown - 3) : "")}";
                GUI.Label(new Rect(0, 170, vw, 170), txt, sLogo);
                if (cardTime > 0.9f)
                {
                    GUI.Label(new Rect(0, 345, vw, 40), cardSub, sSub);
                    var small = new GUIStyle(sSub) { fontSize = 22 };
                    GUI.Label(new Rect(0, 440, vw, 30), $"<color=#{acc}>GitHub Game Off 2026</color>   //   theme: SHIFT", small);
                    GUI.Label(new Rect(0, 480, vw, 30), "play free in your browser on itch.io", small);
                }
                if (cardTime > 1.6f)
                {
                    var keys = new GUIStyle(sSub) { fontSize = 20 };
                    keys.normal.textColor = new Color(0.45f, 0.52f, 0.62f);
                    GUI.Label(new Rect(0, 600, vw, 30), "Q  <<      E  >>      F  rotate      Z  undo", keys);
                }
            }
            else
            {
                int chars = Mathf.Clamp((int)(cardTime * 42f), 0, cardTitle.Length);
                bool cursor = Mathf.Repeat(cardTime, 0.5f) < 0.3f;
                // shrink long lines so they always fit the frame
                var fit = new GUIStyle(sTitle);
                float w = sTitle.CalcSize(new GUIContent(cardTitle + "_")).x;
                if (w > vw - 120) fit.fontSize = Mathf.FloorToInt(sTitle.fontSize * (vw - 120) / w);
                GUI.Label(new Rect(0, 280, vw, 90), cardTitle.Substring(0, chars) + (cursor ? "<color=#39ffb0>_</color>" : " "), fit);
                if (!string.IsNullOrEmpty(cardSub) && cardTime > 0.5f)
                    GUI.Label(new Rect(0, 375, vw, 40), cardSub, sSub);
            }
            GUI.color = old;
            GUI.matrix = Matrix4x4.identity;
        }

        static void WriteWav(string path, List<float> samples, int ch, int rate)
        {
            using var bw = new BinaryWriter(File.Create(path));
            int bytes = samples.Count * 2;
            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + bytes);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)ch);
            bw.Write(rate);
            bw.Write(rate * ch * 2);
            bw.Write((short)(ch * 2));
            bw.Write((short)16);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            bw.Write(bytes);
            foreach (var s in samples) bw.Write((short)(Mathf.Clamp(s, -1f, 1f) * 32767f));
        }
    }
}
