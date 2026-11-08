// BITSHIFT: a puzzle about bitwise shifts. Everything (sprites, sound, UI) is generated in code.
using System.Collections.Generic;
using UnityEngine;

namespace BitShift
{
    public class Game : MonoBehaviour
    {
        enum Mode { Title, Select, Play, Done, End }

        // ------------------------------------------------------------ palette
        static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }
        static readonly Color BgCol = Hex("070a10"), FloorA = Hex("111925"), FloorB = Hex("142030"),
            WallCol = Hex("22324a"), WallEdge = Hex("3d5878"), PitCol = Hex("000000"), PitRim = Hex("ff4d6d"),
            PadCol = Hex("39ffb0"), ExitLocked = Hex("ff4d6d"), ExitOpen = Hex("39ff88"), PlayerCol = Hex("eafff4"),
            Accent = Hex("39ffb0"), ErrCol = Hex("ff4d6d"), WarnCol = Hex("ffd166"), Dim = Hex("9fb2cc");

        static Color BitsColor(int bits) => bits switch
        {
            1 => Hex("a3adbd"), 2 => Hex("ff9f43"), 3 => Hex("2ec5ff"), _ => Hex("ff5ccd")
        };

        static string Html(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        // ------------------------------------------------------------ state
        List<LevelDef> levels;
        int levelIndex, selectIndex;
        LevelDef L;
        State st;
        readonly Stack<State> undo = new Stack<State>();
        Mode mode = Mode.Title;
        float modeTime, doneTimer = -1f;
        bool showHint = true, exitWasOpen;
        Camera cam;

        // ------------------------------------------------------------ visuals
        Sprite sq, round, ring, tri, glyph0, glyph1, digits;
        Texture2D scanTex, vignetteTex, white;
        Transform levelRoot, bgRoot;
        SpriteRenderer bgDigits;

        class BlockView
        {
            public int id;
            public Transform root;
            public SpriteRenderer body, inner, cap;
            public List<SpriteRenderer> glyphs = new List<SpriteRenderer>();
            public Vector2 anchor;
            public float len, flash;
            public Block b;
        }

        readonly Dictionary<int, BlockView> views = new Dictionary<int, BlockView>();
        Transform player;
        SpriteRenderer playerBody, playerEye;
        Vector2 playerPos, playerNudge;
        float playerSquash;
        readonly List<(HashSet<Vector2Int> cells, SpriteRenderer ring, SpriteRenderer fill)> padViews = new List<(HashSet<Vector2Int>, SpriteRenderer, SpriteRenderer)>();
        readonly Dictionary<Vector2Int, SpriteRenderer> pitViews = new Dictionary<Vector2Int, SpriteRenderer>();
        SpriteRenderer exitBody, exitRing;
        SpriteRenderer beam;
        float beamT;
        Color beamCol;

        class Particle { public SpriteRenderer sr; public Vector2 v; public float life, max, spin; }
        readonly List<Particle> particles = new List<Particle>();
        readonly Stack<SpriteRenderer> particlePool = new Stack<SpriteRenderer>();

        class FloatText { public string text; public Vector2 world; public float t, life; public Color col; public int size; }
        readonly List<FloatText> floats = new List<FloatText>();

        float shake;
        Vector3 camBase;

        // input repeat
        int heldDir = -1;
        float repeatTimer;
        float undoRepeat;

        // ============================================================ boot
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<Game>() == null) new GameObject("BitShift").AddComponent<Game>();
        }

        void Awake()
        {
            Application.targetFrameRate = 60;
            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = BgCol;
            cam.transform.position = new Vector3(0, 0, -10);
            if (GetComponent<Sfx>() == null) gameObject.AddComponent<Sfx>();

            MakeSprites();
            var ta = Resources.Load<TextAsset>("levels");
            levels = Sim.Parse(ta.text);

            bgRoot = new GameObject("Background").transform;
            bgDigits = NewSprite(bgRoot, digits, new Color(1, 1, 1, 1), -100);
            bgDigits.drawMode = SpriteDrawMode.Tiled;
            bgDigits.size = new Vector2(80, 60);

            beam = NewSprite(null, sq, Color.clear, 25);
            beam.name = "Beam";
            selectIndex = Mathf.Min(FirstUnfinished(), levels.Count - 1);
        }

        // ============================================================ sprites
        static Texture2D Tex(int w, int h, System.Func<int, int, Color> f, FilterMode fm, TextureWrapMode wrap = TextureWrapMode.Clamp)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = fm, wrapMode = wrap };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = f(x, y);
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        static Texture2D RoundTex(int s, float r, float thick)
        {
            return Tex(s, s, (x, y) =>
            {
                float px = Mathf.Abs(x + 0.5f - s / 2f) - (s / 2f - r);
                float py = Mathf.Abs(y + 0.5f - s / 2f) - (s / 2f - r);
                float outside = new Vector2(Mathf.Max(px, 0), Mathf.Max(py, 0)).magnitude;
                float d = outside + Mathf.Min(Mathf.Max(px, py), 0) - r;
                float a = Mathf.Clamp01(0.5f - d);
                if (thick > 0) a *= Mathf.Clamp01(d + thick + 0.5f);
                return new Color(1, 1, 1, a);
            }, FilterMode.Bilinear);
        }

        static readonly string[] Glyph0 = { "111", "101", "101", "101", "111" };
        static readonly string[] Glyph1 = { "010", "110", "010", "010", "111" };

        static Texture2D GlyphTex(string[] g) =>
            Tex(3, 5, (x, y) => g[4 - y][x] == '1' ? Color.white : Color.clear, FilterMode.Point);

        void MakeSprites()
        {
            var half = new Vector2(0.5f, 0.5f);
            white = Tex(2, 2, (x, y) => Color.white, FilterMode.Point);
            sq = Sprite.Create(Tex(4, 4, (x, y) => Color.white, FilterMode.Point), new Rect(0, 0, 4, 4), half, 4);
            round = Sprite.Create(RoundTex(32, 9, 0), new Rect(0, 0, 32, 32), half, 32, 0, SpriteMeshType.FullRect, new Vector4(10, 10, 10, 10));
            ring = Sprite.Create(RoundTex(32, 9, 2.5f), new Rect(0, 0, 32, 32), half, 32, 0, SpriteMeshType.FullRect, new Vector4(10, 10, 10, 10));
            tri = Sprite.Create(Tex(32, 32, (x, y) =>
            {
                float fx = x / 31f, fy = Mathf.Abs(y - 15.5f) / 15.5f;
                return new Color(1, 1, 1, fy <= 1f - fx ? 1f : 0f);
            }, FilterMode.Bilinear), new Rect(0, 0, 32, 32), half, 32);
            glyph0 = Sprite.Create(GlyphTex(Glyph0), new Rect(0, 0, 3, 5), half, 5f / 0.26f);
            glyph1 = Sprite.Create(GlyphTex(Glyph1), new Rect(0, 0, 3, 5), half, 5f / 0.26f);

            // background: a field of faint 0s and 1s
            var rng = new System.Random(42);
            var cells = new int[16, 10];
            var alpha = new float[16, 10];
            for (int i = 0; i < 16; i++)
                for (int j = 0; j < 10; j++)
                {
                    cells[i, j] = rng.Next(2);
                    alpha[i, j] = rng.NextDouble() < 0.35 ? 0f : (float)(0.015 + rng.NextDouble() * 0.035);
                }
            var dt = Tex(64, 60, (x, y) =>
            {
                int cx = x / 4, cy = y / 6, gx = x % 4, gy = y % 6;
                if (gx >= 3 || gy >= 5) return Color.clear;
                var g = cells[cx, cy] == 1 ? Glyph1 : Glyph0;
                return g[4 - gy][gx] == '1' ? new Color(0.22f, 1f, 0.69f, alpha[cx, cy]) : Color.clear;
            }, FilterMode.Point);
            digits = Sprite.Create(dt, new Rect(0, 0, 64, 60), half, 12, 0, SpriteMeshType.FullRect);

            scanTex = Tex(1, 3, (x, y) => y == 0 ? new Color(0, 0, 0, 0.22f) : Color.clear, FilterMode.Point, TextureWrapMode.Repeat);
            vignetteTex = Tex(128, 128, (x, y) =>
            {
                float dx = (x - 63.5f) / 64f, dy = (y - 63.5f) / 64f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                return new Color(0, 0, 0, Mathf.SmoothStep(0f, 0.75f, (d - 0.55f) / 0.6f));
            }, FilterMode.Bilinear);
        }

        SpriteRenderer NewSprite(Transform parent, Sprite s, Color c, int order)
        {
            var go = new GameObject("s");
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s;
            sr.color = c;
            sr.sortingOrder = order;
            return sr;
        }

        SpriteRenderer NewSliced(Transform parent, Sprite s, Color c, int order, Vector2 size)
        {
            var sr = NewSprite(parent, s, c, order);
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = size;
            return sr;
        }

        static Vector2 W(Vector2Int p) => new Vector2(p.x, -p.y);
        static Vector2 W(Vector2 p) => new Vector2(p.x, -p.y);

        // ============================================================ progress
        int Best(int i) => PlayerPrefs.GetInt("best_" + levels[i].title, 0);
        bool Unlocked(int i) => unlockAll || i == 0 || Best(i - 1) > 0 || Best(i) > 0;

        int FirstUnfinished()
        {
            for (int i = 0; i < levels.Count; i++) if (Best(i) == 0) return i;
            return 0;
        }

        // ============================================================ level setup
        public void LoadLevel(int i)
        {
            levelIndex = i;
            L = levels[i];
            st = Sim.Initial(L);
            undo.Clear();
            BuildLevelVisuals();
            exitWasOpen = Sim.ExitOpen(L, st);
            playerPos = st.player;
            SetMode(Mode.Play);
            doneTimer = -1f;
            floats.Clear();
            showHint = true;
        }

        void ClearLevel()
        {
            if (levelRoot != null) Destroy(levelRoot.gameObject);
            levelRoot = null;
            views.Clear();
            padViews.Clear();
            pitViews.Clear();
            foreach (var p in particles) { p.sr.gameObject.SetActive(false); particlePool.Push(p.sr); }
            particles.Clear();
        }

        bool IsWall(int x, int y) => L.At(new Vector2Int(x, y)) == '#';

        void BuildLevelVisuals()
        {
            ClearLevel();
            levelRoot = new GameObject("Level").transform;
            for (int y = 0; y < L.h; y++)
                for (int x = 0; x < L.w; x++)
                {
                    var p = new Vector2Int(x, y);
                    char t = L.At(p);
                    var wp = W(p);
                    if (t == '#')
                    {
                        bool nearFloor = false;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                var q = new Vector2Int(x + dx, y + dy);
                                if (q.x >= 0 && q.y >= 0 && q.x < L.w && q.y < L.h && L.At(q) != '#') nearFloor = true;
                            }
                        if (!nearFloor) continue;
                        var w = NewSprite(levelRoot, sq, WallCol, 0);
                        w.transform.localPosition = wp;
                        // a lighter lip on edges that face open floor below
                        if (y + 1 < L.h && !IsWall(x, y + 1))
                        {
                            var lip = NewSprite(levelRoot, sq, WallEdge, 1);
                            lip.transform.localPosition = wp + new Vector2(0, -0.44f);
                            lip.transform.localScale = new Vector3(1f, 0.12f, 1);
                        }
                        continue;
                    }
                    var f = NewSprite(levelRoot, sq, ((x + y) & 1) == 0 ? FloorA : FloorB, 0);
                    f.transform.localPosition = wp;
                    if (t == '_')
                    {
                        var pit = NewSprite(levelRoot, sq, PitCol, 1);
                        pit.transform.localPosition = wp;
                        pit.transform.localScale = new Vector3(0.94f, 0.94f, 1);
                        pitViews[p] = pit;
                    }
                }

            foreach (var tr in L.trenches)
            {
                Bounds(tr, out var min, out var max);
                var center = (W(min) + W(max)) / 2f;
                var r = NewSliced(levelRoot, ring, new Color(PitRim.r, PitRim.g, PitRim.b, 0.55f), 2,
                    new Vector2(max.x - min.x + 1, max.y - min.y + 1));
                r.transform.localPosition = center;
                r.name = "trench";
            }

            foreach (var pad in L.pads)
            {
                Bounds(pad, out var min, out var max);
                var center = (W(min) + W(max)) / 2f;
                var size = new Vector2(max.x - min.x + 1, max.y - min.y + 1);
                var fill = NewSliced(levelRoot, round, new Color(PadCol.r, PadCol.g, PadCol.b, 0.08f), 2, size - new Vector2(0.1f, 0.1f));
                fill.transform.localPosition = center;
                var r = NewSliced(levelRoot, ring, new Color(PadCol.r, PadCol.g, PadCol.b, 0.6f), 3, size - new Vector2(0.06f, 0.06f));
                r.transform.localPosition = center;
                padViews.Add((pad, r, fill));
            }

            exitBody = NewSliced(levelRoot, round, ExitLocked, 3, new Vector2(0.8f, 0.8f));
            exitBody.transform.localPosition = W(L.exit);
            exitRing = NewSliced(levelRoot, ring, ExitLocked, 4, new Vector2(0.96f, 0.96f));
            exitRing.transform.localPosition = W(L.exit);

            var pg = new GameObject("Player").transform;
            pg.SetParent(levelRoot, false);
            player = pg;
            playerBody = NewSliced(pg, round, PlayerCol, 20, new Vector2(0.62f, 0.62f));
            playerEye = NewSprite(pg, tri, BgCol, 21);
            playerEye.transform.localScale = new Vector3(0.28f, 0.28f, 1);

            SyncViews(true);
        }

        static void Bounds(IEnumerable<Vector2Int> cells, out Vector2Int min, out Vector2Int max)
        {
            min = new Vector2Int(int.MaxValue, int.MaxValue);
            max = new Vector2Int(int.MinValue, int.MinValue);
            foreach (var c in cells) { min = Vector2Int.Min(min, c); max = Vector2Int.Max(max, c); }
        }

        BlockView MakeView(Block b)
        {
            var v = new BlockView { id = b.id, b = b, anchor = new Vector2(b.x, b.y), len = b.value };
            v.root = new GameObject("Block " + b.letter).transform;
            v.root.SetParent(levelRoot, false);
            var c = BitsColor(b.bits);
            v.body = NewSliced(v.root, round, c, 10, Vector2.one);
            v.inner = NewSliced(v.root, round, Color.Lerp(c, BgCol, 0.55f), 11, Vector2.one);
            v.cap = NewSprite(v.root, sq, Color.white, 12);
            for (int i = 0; i < b.bits; i++) v.glyphs.Add(NewSprite(v.root, glyph0, Color.white, 13));
            views[b.id] = v;
            return v;
        }

        void SyncViews(bool snap)
        {
            var alive = new HashSet<int>();
            foreach (var b in st.blocks)
            {
                alive.Add(b.id);
                if (!views.TryGetValue(b.id, out var v)) { v = MakeView(b); v.flash = 1f; }
                v.b = b;
                if (snap) { v.anchor = new Vector2(b.x, b.y); v.len = b.value; }
            }
            var dead = new List<int>();
            foreach (var kv in views) if (!alive.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (var id in dead) { Destroy(views[id].root.gameObject); views.Remove(id); }

            foreach (var kv in pitViews)
            {
                bool filled = st.filled.TryGetValue(kv.Key, out int bits);
                kv.Value.color = filled ? Color.Lerp(BitsColor(bits), FloorA, 0.7f) : PitCol;
            }
        }

        // ============================================================ actions
        public void DoAction(int a)
        {
            if (mode != Mode.Play || doneTimer >= 0f) return;
            if (a >= 4 && !L.ops.Contains((Op)(a - 4))) return;
            var before = st.Clone();
            var info = Sim.Step(L, st, a);
            bool changed = info.ev != Ev.None && info.ev != Ev.Bump && info.ev != Ev.NoTarget && info.ev != Ev.Segfault;
            if (changed) undo.Push(before);
            React(info);
            SyncViews(false);
            bool open = Sim.ExitOpen(L, st);
            if (open && !exitWasOpen)
            {
                Sfx.I.Play("unlock", 0.9f);
                Float("EXIT UNLOCKED", W(L.exit) + new Vector2(0, 0.6f), ExitOpen, 18);
            }
            exitWasOpen = open;
        }

        void React(StepInfo info)
        {
            var dir = Sim.Dirs[st.facing];
            switch (info.ev)
            {
                case Ev.Move:
                    Sfx.I.Play("step", 0.8f, Random.Range(0.95f, 1.05f));
                    playerSquash = 1f;
                    break;
                case Ev.Turn:
                    Sfx.I.Play("step", 0.4f, 1.4f);
                    break;
                case Ev.Bump:
                    Sfx.I.Play("bump", 0.7f);
                    playerNudge = new Vector2(dir.x, dir.y) * 0.12f;
                    break;
                case Ev.Push:
                    Sfx.I.Play("push", 0.8f, Random.Range(0.95f, 1.05f));
                    playerSquash = 1f;
                    break;
                case Ev.Fill:
                    Sfx.I.Play("fill");
                    shake = Mathf.Max(shake, 0.18f);
                    foreach (var c in info.cells) Burst(W(c), BitsColor(info.bits), 8, 3f);
                    Bounds(info.cells, out var mn, out var mx);
                    Float("STORE", (W(mn) + W(mx)) / 2f + new Vector2(0, 0.5f), BitsColor(info.bits), 16);
                    break;
                case Ev.Shift:
                {
                    Sfx.I.Play(info.op == Op.Shl ? "shl" : info.op == Op.Shr ? "shr" : "rol");
                    var v = views[info.blockId];
                    v.flash = 1f;
                    Beam(info.at, BitsColor(info.bits));
                    string msg = $"{Sim.Bin(info.oldV, info.bits)} {Sim.OpSymbol(info.op)} 1 = {Sim.Bin(info.newV, info.bits)}";
                    Float(msg, W(info.at) + new Vector2(0, 0.6f), BitsColor(info.bits), 15);
                    if (info.bitLost)
                    {
                        Sfx.I.Play("lost", 0.8f);
                        var end = info.op == Op.Shl ? EndCell(v.b, info.oldV) : EndCell(v.b, 1);
                        Float(info.op == Op.Shl ? "OVERFLOW! a bit fell off the top" : "a bit fell off the bottom",
                            W(end) + new Vector2(0, 1.0f), WarnCol, 15);
                        Burst(W(end), WarnCol, 6, 4f);
                        shake = Mathf.Max(shake, 0.1f);
                    }
                    else if (info.op == Op.Rol && info.oldV >> (info.bits - 1) == 1)
                        Float("bit wrapped around", W(info.at) + new Vector2(0, 1.0f), WarnCol, 14);
                    break;
                }
                case Ev.Free:
                    Sfx.I.Play("free");
                    shake = Mathf.Max(shake, 0.3f);
                    Beam(info.at, ErrCol);
                    foreach (var c in info.cells) Burst(W(c), BitsColor(info.bits), 14, 5f, true);
                    Bounds(info.cells, out var a0, out var a1);
                    Float(info.bitLost ? "OUT OF BITS -> 0 -> free()" : "0 -> free()", (W(a0) + W(a1)) / 2f + new Vector2(0, 0.7f), ErrCol, 17);
                    break;
                case Ev.Segfault:
                    Sfx.I.Play("err");
                    shake = Mathf.Max(shake, 0.12f);
                    Burst(W(info.at), ErrCol, 6, 2.5f);
                    Float("SEGFAULT: no room to grow", W(info.at) + new Vector2(0, 0.6f), ErrCol, 15);
                    break;
                case Ev.NoTarget:
                    Sfx.I.Play("none");
                    Float("no register in front of you", W(playerPos) + new Vector2(0, 0.7f), Dim, 13);
                    break;
                case Ev.Win:
                    Sfx.I.Play("win");
                    Burst(W(L.exit), ExitOpen, 30, 6f, true);
                    doneTimer = 0.7f;
                    int best = Best(levelIndex);
                    if (best == 0 || st.actions < best)
                    {
                        PlayerPrefs.SetInt("best_" + L.title, st.actions);
                        PlayerPrefs.Save();
                    }
                    break;
            }
        }

        static Vector2Int EndCell(Block b, int len) =>
            b.vertical ? new Vector2Int(b.x, b.y + len - 1) : new Vector2Int(b.x + len - 1, b.y);

        void Undo()
        {
            if (mode != Mode.Play || undo.Count == 0) return;
            st = undo.Pop();
            doneTimer = -1f;
            Sfx.I.Play("shr", 0.35f, 1.6f);
            SyncViews(false);
            exitWasOpen = Sim.ExitOpen(L, st);
        }

        void Restart()
        {
            if (mode != Mode.Play && mode != Mode.Done) return;
            if (mode == Mode.Done) { LoadLevel(levelIndex); return; }
            undo.Push(st);
            st = Sim.Initial(L);
            doneTimer = -1f;
            Sfx.I.Play("rol", 0.5f, 0.7f);
            SyncViews(false);
            exitWasOpen = Sim.ExitOpen(L, st);
        }

        public void ShowTitle() => SetMode(Mode.Title);

        void SetMode(Mode m)
        {
            mode = m;
            modeTime = 0f;
            if (m == Mode.Title || m == Mode.Select || m == Mode.End) ClearLevel();
        }

        // ============================================================ effects
        void Burst(Vector2 at, Color c, int n, float speed, bool glyphs = false)
        {
            for (int i = 0; i < n; i++)
            {
                var sr = particlePool.Count > 0 ? particlePool.Pop() : NewSprite(null, sq, c, 30);
                sr.gameObject.SetActive(true);
                sr.sprite = glyphs ? (Random.value < 0.5f ? glyph0 : glyph1) : sq;
                sr.color = c;
                sr.sortingOrder = 30;
                sr.transform.position = at + Random.insideUnitCircle * 0.3f;
                float s = glyphs ? Random.Range(0.8f, 1.3f) : Random.Range(0.06f, 0.14f);
                sr.transform.localScale = new Vector3(s, s, 1);
                sr.transform.rotation = Quaternion.identity;
                particles.Add(new Particle
                {
                    sr = sr, v = Random.insideUnitCircle.normalized * Random.Range(0.3f, 1f) * speed,
                    life = 0, max = Random.Range(0.4f, 0.9f), spin = glyphs ? 0 : Random.Range(-400f, 400f)
                });
            }
        }

        void Float(string text, Vector2 world, Color c, int size)
        {
            floats.Add(new FloatText { text = text, world = world, col = c, life = 1.6f, size = size });
            if (floats.Count > 6) floats.RemoveAt(0);
        }

        void Beam(Vector2Int target, Color c)
        {
            beamT = 1f;
            beamCol = c;
            var a = W(st.player);
            var b = W(target);
            beam.transform.position = (a + b) / 2f;
            bool horiz = Mathf.Abs(a.x - b.x) > 0.1f;
            beam.transform.localScale = horiz ? new Vector3(Mathf.Abs(a.x - b.x), 0.12f, 1) : new Vector3(0.12f, Mathf.Abs(a.y - b.y), 1);
        }

        // ============================================================ update
        void Update()
        {
            float dt = Time.deltaTime;
            modeTime += dt;
            if (Input.GetKeyDown(KeyCode.M)) Sfx.I.ToggleMusic();
            if (Input.GetKeyDown(KeyCode.N)) Sfx.I.ToggleSfx();

            switch (mode)
            {
                case Mode.Title:
                    if (modeTime > 0.2f && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.KeypadEnter)))
                    { Sfx.I.Play("select"); SetMode(Mode.Select); }
                    break;
                case Mode.Select: UpdateSelect(); break;
                case Mode.Play: UpdatePlay(dt); break;
                case Mode.Done:
                    if (modeTime > 0.25f)
                    {
                        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.KeypadEnter)) Next();
                        else if (Input.GetKeyDown(KeyCode.R)) Restart();
                        else if (Input.GetKeyDown(KeyCode.Z)) { mode = Mode.Play; Undo(); }
                        else if (Input.GetKeyDown(KeyCode.Escape)) ToSelect();
                    }
                    break;
                case Mode.End:
                    if (modeTime > 0.5f && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Space))) ToSelect();
                    break;
            }

            AnimateWorld(dt);
            FrameCamera(dt);
        }

        void ToSelect()
        {
            Sfx.I.Play("select");
            selectIndex = mode == Mode.End ? 0 : levelIndex;
            SetMode(Mode.Select);
        }

        void Next()
        {
            Sfx.I.Play("select");
            if (levelIndex + 1 < levels.Count) LoadLevel(levelIndex + 1);
            else SetMode(Mode.End);
        }

        int SelectCols => vw > 0 && vw < 1100 ? 3 : 4;

        void UpdateSelect()
        {
            int cols = SelectCols;
            int prev = selectIndex;
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) selectIndex++;
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) selectIndex--;
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) selectIndex += cols;
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) selectIndex -= cols;
            selectIndex = Mathf.Clamp(selectIndex, 0, levels.Count - 1);
            if (prev != selectIndex) Sfx.I.Play("select", 0.6f);
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (Unlocked(selectIndex)) { Sfx.I.Play("select"); LoadLevel(selectIndex); }
                else Sfx.I.Play("err", 0.6f);
            }
            if (Input.GetKeyDown(KeyCode.Escape)) SetMode(Mode.Title);
            // F9: unlock every level (for testers / judges who want to jump ahead)
            if (Input.GetKeyDown(KeyCode.F9)) { unlockAll = true; Sfx.I.Play("unlock"); }
        }

        bool unlockAll;

        static int KeyDir()
        {
            if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W)) return 0;
            if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) return 1;
            if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S)) return 2;
            if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) return 3;
            return -1;
        }

        static int KeyDirDown()
        {
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) return 0;
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) return 1;
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) return 2;
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) return 3;
            return -1;
        }

        void UpdatePlay(float dt)
        {
            if (doneTimer >= 0f)
            {
                doneTimer -= dt;
                if (doneTimer < 0f) SetMode(Mode.Done);
                if (Input.GetKeyDown(KeyCode.Z)) Undo();
                return;
            }
            int down = KeyDirDown();
            if (down >= 0) { DoAction(down); heldDir = down; repeatTimer = 0.2f; }
            else
            {
                int held = KeyDir();
                if (held != heldDir) heldDir = -1;
                if (heldDir >= 0)
                {
                    repeatTimer -= dt;
                    if (repeatTimer <= 0f) { DoAction(heldDir); repeatTimer = 0.11f; }
                }
            }
            if (Input.GetKeyDown(KeyCode.Q)) DoAction(4 + (int)Op.Shl);
            if (Input.GetKeyDown(KeyCode.E)) DoAction(4 + (int)Op.Shr);
            if (Input.GetKeyDown(KeyCode.F)) DoAction(4 + (int)Op.Rol);
            if (Input.GetKeyDown(KeyCode.G)) DoAction(4 + (int)Op.Ror);
            if (Input.GetKeyDown(KeyCode.Z) || Input.GetKeyDown(KeyCode.Backspace)) { Undo(); undoRepeat = 0.3f; }
            else if (Input.GetKey(KeyCode.Z) || Input.GetKey(KeyCode.Backspace))
            {
                undoRepeat -= dt;
                if (undoRepeat <= 0f) { Undo(); undoRepeat = 0.08f; }
            }
            if (Input.GetKeyDown(KeyCode.R)) Restart();
            if (Input.GetKeyDown(KeyCode.H)) showHint = !showHint;
            if (Input.GetKeyDown(KeyCode.Escape)) ToSelect();
        }

        void AnimateWorld(float dt)
        {
            float t = Time.time;
            if (bgDigits != null)
            {
                var cp = cam.transform.position;
                float tw = 64f / 12f, th = 5f;
                bgDigits.transform.position = new Vector3(cp.x - Mathf.Repeat(cp.x, tw) + Mathf.Repeat(t * 0.15f, tw),
                    cp.y - Mathf.Repeat(cp.y, th) - Mathf.Repeat(t * 0.3f, th), 0);
            }

            // particles
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                var p = particles[i];
                p.life += dt;
                if (p.life >= p.max)
                {
                    p.sr.gameObject.SetActive(false);
                    particlePool.Push(p.sr);
                    particles.RemoveAt(i);
                    continue;
                }
                p.v *= Mathf.Exp(-dt * 3f);
                p.sr.transform.position += (Vector3)(p.v * dt);
                p.sr.transform.Rotate(0, 0, p.spin * dt);
                var c = p.sr.color;
                c.a = 1f - p.life / p.max;
                p.sr.color = c;
            }
            for (int i = floats.Count - 1; i >= 0; i--)
            {
                floats[i].t += dt;
                if (floats[i].t > floats[i].life) floats.RemoveAt(i);
            }

            beamT = Mathf.Max(0, beamT - dt * 5f);
            beam.color = new Color(beamCol.r, beamCol.g, beamCol.b, beamT * 0.9f);

            if (levelRoot == null || st == null) return;

            // player
            playerPos = Vector2.Lerp(playerPos, st.player, 1f - Mathf.Exp(-dt * 22f));
            playerNudge = Vector2.Lerp(playerNudge, Vector2.zero, 1f - Mathf.Exp(-dt * 18f));
            playerSquash = Mathf.Max(0, playerSquash - dt * 7f);
            player.localPosition = W(playerPos + playerNudge);
            float sq_ = 1f + Mathf.Sin(playerSquash * Mathf.PI) * 0.12f;
            var fdir = Sim.Dirs[st.facing];
            bool horizFacing = fdir.x != 0;
            playerBody.transform.localScale = horizFacing ? new Vector3(sq_, 2f - sq_, 1) : new Vector3(2f - sq_, sq_, 1);
            float ang = st.facing switch { 0 => 90f, 1 => 0f, 2 => -90f, _ => 180f };
            playerEye.transform.localRotation = Quaternion.Euler(0, 0, ang);
            playerEye.transform.localPosition = new Vector3(fdir.x, -fdir.y, 0) * 0.14f;
            float blink = 0.85f + 0.15f * Mathf.Sin(t * 6f);
            playerBody.color = PlayerCol * blink + new Color(0, 0, 0, 1 - blink);

            // blocks
            var target = Sim.Target(st);
            foreach (var v in views.Values)
            {
                var b = v.b;
                float k = 1f - Mathf.Exp(-dt * 20f);
                v.anchor = Vector2.Lerp(v.anchor, new Vector2(b.x, b.y), k);
                v.len = Mathf.Lerp(v.len, b.value, 1f - Mathf.Exp(-dt * 14f));
                v.flash = Mathf.Max(0, v.flash - dt * 3f);
                var axis = b.vertical ? new Vector2(0, 1) : new Vector2(1, 0);
                var center = v.anchor + axis * (v.len - 1f) / 2f;
                v.root.localPosition = W(center);
                var size = b.vertical ? new Vector2(0.86f, v.len - 0.14f) : new Vector2(v.len - 0.14f, 0.86f);
                v.body.size = size;
                v.inner.size = size - new Vector2(0.2f, 0.2f);
                var bc = BitsColor(b.bits);
                bool targeted = target == b;
                v.body.color = Color.Lerp(bc, Color.white, v.flash * 0.8f + (targeted ? 0.12f + 0.08f * Mathf.Sin(t * 8f) : 0f));
                v.inner.color = Color.Lerp(bc, BgCol, 0.6f);
                // anchor cap marks the fixed end; the block grows away from it
                var anchorLocal = W(v.anchor) - W(center);
                if (b.vertical)
                {
                    v.cap.transform.localPosition = anchorLocal + new Vector2(0, 0.36f);
                    v.cap.transform.localScale = new Vector3(0.6f, 0.07f, 1);
                }
                else
                {
                    v.cap.transform.localPosition = anchorLocal + new Vector2(-0.36f, 0);
                    v.cap.transform.localScale = new Vector3(0.07f, 0.6f, 1);
                }
                v.cap.color = new Color(1, 1, 1, 0.85f);
                // register digits, MSB first, drawn on the anchor cell
                float spacing = Mathf.Min(0.2f, 0.62f / b.bits);
                float startX = -(b.bits - 1) * spacing / 2f;
                float gscale = b.bits >= 4 ? 0.75f : 1f;
                for (int i = 0; i < v.glyphs.Count; i++)
                {
                    bool one = ((b.value >> (b.bits - 1 - i)) & 1) == 1;
                    var g = v.glyphs[i];
                    g.sprite = one ? glyph1 : glyph0;
                    g.color = one ? Color.white : new Color(1, 1, 1, 0.28f);
                    g.transform.localPosition = anchorLocal + new Vector2(startX + i * spacing + 0.04f, 0);
                    g.transform.localScale = new Vector3(gscale, gscale, 1);
                }
            }

            // pads
            foreach (var pv in padViews)
            {
                bool ok = Sim.PadSatisfied(st, pv.cells);
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 3f);
                pv.ring.color = new Color(PadCol.r, PadCol.g, PadCol.b, ok ? 1f : 0.35f + 0.25f * pulse);
                pv.fill.color = new Color(PadCol.r, PadCol.g, PadCol.b, ok ? 0.3f : 0.06f);
            }

            bool open = Sim.ExitOpen(L, st);
            var ec = open ? ExitOpen : ExitLocked;
            float ep = 0.5f + 0.5f * Mathf.Sin(t * (open ? 6f : 2f));
            exitBody.color = new Color(ec.r, ec.g, ec.b, open ? 0.35f + 0.25f * ep : 0.18f);
            exitRing.color = new Color(ec.r, ec.g, ec.b, open ? 0.7f + 0.3f * ep : 0.6f);
            exitRing.size = Vector2.one * (open ? 0.9f + 0.08f * ep : 0.9f);
        }

        void FrameCamera(float dt)
        {
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            if (levelRoot != null && L != null)
            {
                // leave room for the HUD: ~70px top, ~120px bottom of a 720px-tall virtual screen
                float usable = (720f - 190f) / 720f;
                float size = Mathf.Max((L.h + 0.4f) / (2f * usable), (L.w + 0.8f) / (2f * aspect * 0.98f));
                cam.orthographicSize = size;
                float cx = (L.w - 1) / 2f, cy = -(L.h - 1) / 2f;
                float shiftPx = (120f - 70f) / 2f;
                camBase = new Vector3(cx, cy - shiftPx / 360f * size, -10);
            }
            else
            {
                cam.orthographicSize = 6f;
                camBase = new Vector3(0, 0, -10);
            }
            shake = Mathf.Max(0, shake - dt * 1.2f);
            cam.transform.position = camBase + (Vector3)(Random.insideUnitCircle * shake * 0.5f);
        }

        // ============================================================ GUI
        GUIStyle sText, sCenter, sBig, sTitle, sBtn, sPanel, sFloat, sSmall;
        Texture2D accentTex, shadeTex;
        Font font;
        float vw, vh, gk;

        Texture2D Solid(Color c) => Tex(2, 2, (x, y) => c, FilterMode.Point);

        // 8x8 texture with a 2px frame, used 9-sliced by GUIStyle.border
        Texture2D Framed(Color fill, Color frame) =>
            Tex(8, 8, (x, y) => (x < 2 || y < 2 || x > 5 || y > 5) ? frame : fill, FilterMode.Point);

        GUIStyle sBar;

        // "[Q] SHL" style label: key in the accent colour, the rest in white
        string KeyLabel(string key, string text) => $"<color={Html(Accent)}>[{key}]</color> {text}";

        void InitStyles()
        {
            if (sText != null) return;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            accentTex = Solid(Accent);
            shadeTex = Solid(new Color(0, 0, 0, 0.55f));
            sText = new GUIStyle { font = font, fontSize = 18, richText = true, wordWrap = false };
            sText.normal.textColor = Hex("f2fff8");
            sSmall = new GUIStyle(sText) { fontSize = 14 };
            sSmall.normal.textColor = Dim;
            sCenter = new GUIStyle(sText) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            sBig = new GUIStyle(sCenter) { fontSize = 34, fontStyle = FontStyle.Bold };
            sTitle = new GUIStyle(sCenter) { fontSize = 96, fontStyle = FontStyle.Bold, wordWrap = false };
            sFloat = new GUIStyle(sCenter) { fontStyle = FontStyle.Bold, wordWrap = false };
            sPanel = new GUIStyle { padding = new RectOffset(12, 12, 8, 8) };
            sPanel.normal.background = Solid(new Color(0.03f, 0.05f, 0.08f, 0.94f));
            sBar = new GUIStyle { border = new RectOffset(3, 3, 3, 3) };
            sBar.normal.background = Framed(new Color(0.035f, 0.055f, 0.085f, 0.97f), new Color(0.22f, 0.75f, 0.55f, 0.9f));
            // buttons: opaque fill + bright 2px frame so they read against any background
            sBtn = new GUIStyle(sCenter) { fontSize = 16, fontStyle = FontStyle.Bold, wordWrap = false, richText = true };
            sBtn.border = new RectOffset(3, 3, 3, 3);
            sBtn.normal.background = Framed(Hex("172a3f"), Hex("5f8fb8"));
            sBtn.hover.background = Framed(Hex("1f4a52"), Accent);
            sBtn.active.background = Framed(Hex("2a8a6a"), Color.white);
            sBtn.normal.textColor = Color.white;
            sBtn.hover.textColor = Color.white;
            sBtn.active.textColor = Color.white;
        }

        Vector2 ToGui(Vector2 world)
        {
            var sp = cam.WorldToScreenPoint(new Vector3(world.x, world.y, 0));
            return new Vector2(sp.x / gk, (Screen.height - sp.y) / gk);
        }

        void Label(Rect r, string s, GUIStyle st_, Color? shadow = null)
        {
            var sh = new GUIStyle(st_);
            sh.normal.textColor = shadow ?? new Color(0, 0, 0, 0.8f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), StripColor(s), sh);
            GUI.Label(r, s, st_);
        }

        static string StripColor(string s) => System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", "");

        bool Button(Rect r, string s) => GUI.Button(r, s, sBtn);

        void OnGUI()
        {
            InitStyles();
            gk = Screen.height / 720f;
            vh = 720f;
            vw = Screen.width / gk;
            // CRT finish first, so it sits under the HUD instead of dimming it
            GUI.matrix = Matrix4x4.identity;
            GUI.DrawTextureWithTexCoords(new Rect(0, 0, Screen.width, Screen.height), scanTex, new Rect(0, 0, 1, Screen.height / 3f));
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), vignetteTex, ScaleMode.StretchToFill);
            GUI.matrix = Matrix4x4.Scale(new Vector3(gk, gk, 1));

            switch (mode)
            {
                case Mode.Title: DrawTitle(); break;
                case Mode.Select: DrawSelect(); break;
                case Mode.Play: DrawPlay(); break;
                case Mode.Done: DrawPlay(); DrawDone(); break;
                case Mode.End: DrawEnd(); break;
            }
        }

        void DrawTitle()
        {
            float t = modeTime;
            Label(new Rect(0, 120, vw, 120), $"<color={Html(Accent)}>BIT</color>SHIFT", sTitle);
            Label(new Rect(0, 225, vw, 30), "a programming puzzler about running out of bits", sCenter);

            // an 8-bit register that keeps shifting left until it runs out of bits
            int phase = (int)(t / 0.45f) % 11;
            int v = phase < 8 ? 1 << phase : 0;
            string bin = Sim.Bin(v, 8);
            var big = new GUIStyle(sBig) { fontSize = 54 };
            string pretty = "";
            foreach (char c in bin) pretty += c == '1' ? $"<color={Html(Accent)}>1</color> " : $"<color=#2a3b4f>0</color> ";
            Label(new Rect(0, 300, vw, 70), pretty, big);
            string caption = phase < 7 ? $"x = {v}    x <<= 1" : phase == 7 ? "x = 128   x <<= 1  ...uh oh" : $"<color={Html(ErrCol)}>OVERFLOW  -  x = 0</color>";
            Label(new Rect(0, 370, vw, 30), caption, sCenter);

            float blink = Mathf.Repeat(t, 1f) < 0.6f ? 1f : 0.25f;
            var press = new GUIStyle(sBig) { fontSize = 26 };
            press.normal.textColor = new Color(1, 1, 1, blink);
            GUI.Label(new Rect(0, 450, vw, 40), "PRESS ENTER", press);
            if (Button(new Rect(vw / 2 - 100, 500, 200, 44), "START")) { Sfx.I.Play("select"); SetMode(Mode.Select); }

            Label(new Rect(0, 600, vw, 26), "WASD / arrows move     Q  <<     E  >>     F  rotate     Z undo     R reset     M music", sCenter);
            Label(new Rect(0, 660, vw, 26), $"<color={Html(Dim)}>made for GitHub Game Off 2026  //  theme: SHIFT</color>", sCenter);
        }

        void DrawSelect()
        {
            Label(new Rect(0, 40, vw, 50), "SELECT PROGRAM", sBig);
            int cols = SelectCols;
            float cw = 250, ch = 92, gap = 16;
            float total = cols * cw + (cols - 1) * gap;
            float x0 = (vw - total) / 2f, y0 = 130;
            for (int i = 0; i < levels.Count; i++)
            {
                int cx = i % cols, cy = i / cols;
                var r = new Rect(x0 + cx * (cw + gap), y0 + cy * (ch + gap), cw, ch);
                bool unlocked = Unlocked(i);
                int best = Best(i);
                string status = !unlocked ? $"<color={Html(Dim)}>LOCKED</color>"
                    : best == 0 ? $"<color={Html(WarnCol)}>NEW</color>"
                    : best <= levels[i].par ? $"<color={Html(Accent)}>OPTIMAL  {best}/{levels[i].par}</color>"
                    : $"<color={Html(Accent)}>DONE</color>  <color={Html(Dim)}>{best} (par {levels[i].par})</color>";
                string txt = $"<size=13><color={Html(Dim)}>{i + 1:00}</color></size>\n{(unlocked ? levels[i].title : "????????")}\n<size=13>{status}</size>";
                if (i == selectIndex)
                {
                    GUI.DrawTexture(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), accentTex);
                }
                var old = GUI.enabled;
                GUI.enabled = unlocked;
                var bs = new GUIStyle(sBtn) { richText = true, fontSize = 18 };
                if (GUI.Button(r, txt, bs)) { Sfx.I.Play("select"); LoadLevel(i); }
                GUI.enabled = old;
            }
            Label(new Rect(0, vh - 60, vw, 26), "arrows choose  -  ENTER run  -  ESC back", sCenter);
            if (Button(new Rect(20, 20, 110, 36), "< BACK")) SetMode(Mode.Title);
            DrawAudioButtons();
        }

        void DrawAudioButtons()
        {
            if (Button(new Rect(vw - 250, 20, 115, 32), Sfx.I.musicOn ? "MUSIC ON" : "MUSIC OFF")) Sfx.I.ToggleMusic();
            if (Button(new Rect(vw - 128, 20, 108, 32), Sfx.I.sfxOn ? "SFX ON" : "SFX OFF")) Sfx.I.ToggleSfx();
        }

        static string KeyFor(Op op) => op switch { Op.Shl => "Q", Op.Shr => "E", Op.Rol => "F", _ => "G" };

        void DrawPlay()
        {
            if (L == null || st == null) return;

            // world-anchored labels: pad and trench widths, exit
            foreach (var pad in L.pads)
            {
                Bounds(pad, out var mn, out var mx);
                var p = ToGui(W(new Vector2Int(mn.x, mx.y)) + new Vector2(-0.45f, -0.5f));
                var s = new GUIStyle(sSmall) { fontSize = 13 };
                s.normal.textColor = PadCol;
                GUI.Label(new Rect(p.x, p.y, 80, 18), $"=={pad.Count}", s);
            }
            foreach (var tr in L.trenches)
            {
                bool filled = true;
                foreach (var c in tr) if (!st.filled.ContainsKey(c)) filled = false;
                if (filled) continue;
                Bounds(tr, out var mn, out var mx);
                var p = ToGui(W(new Vector2Int(mn.x, mx.y)) + new Vector2(-0.45f, -0.5f));
                var s = new GUIStyle(sSmall) { fontSize = 13 };
                s.normal.textColor = PitRim;
                GUI.Label(new Rect(p.x, p.y, 80, 18), $"[{tr.Count}]", s);
            }
            {
                var p = ToGui(W(L.exit));
                var s = new GUIStyle(sFloat) { fontSize = 12 };
                bool open = Sim.ExitOpen(L, st);
                s.normal.textColor = open ? ExitOpen : ExitLocked;
                GUI.Label(new Rect(p.x - 40, p.y - 9, 80, 18), open ? "EXIT" : "LOCK", s);
            }
            foreach (var f in floats)
            {
                var p = ToGui(f.world + new Vector2(0, f.t * 0.5f));
                var s = new GUIStyle(sFloat) { fontSize = f.size };
                float a = Mathf.Clamp01((f.life - f.t) * 2f);
                var c = f.col;
                c.a = a;
                s.normal.textColor = c;
                Label(new Rect(p.x - 300, p.y - 15, 600, 30), f.text, s, new Color(0, 0, 0, a * 0.8f));
            }

            // top bar
            GUI.Box(new Rect(-4, -4, vw + 8, 56), GUIContent.none, sBar);
            Label(new Rect(20, 14, 600, 30), $"<color={Html(Dim)}>{levelIndex + 1:00}/{levels.Count:00}</color>  <b>{L.title}</b>", sText);
            var right = new GUIStyle(sText) { alignment = TextAnchor.UpperRight };
            string actCol = st.actions <= L.par ? Html(Accent) : Html(WarnCol);
            Label(new Rect(vw - 520, 14, 500, 30), $"ACTIONS <color={actCol}>{st.actions}</color>   <color={Html(Dim)}>PAR {L.par}</color>", right);

            // register inspector
            var tb = Sim.Target(st);
            float ix = vw - 300, iy = 62;
            var ir = new Rect(ix, iy, 286, 34 + 24 * L.ops.Count);
            GUI.Box(ir, GUIContent.none, sPanel);
            if (tb != null)
            {
                var col = Html(BitsColor(tb.bits));
                Label(new Rect(ix + 12, iy + 6, 280, 24), $"<color={col}>u{tb.bits}</color> = <color={col}><b>{Sim.Bin(tb.value, tb.bits)}</b></color>  ({tb.value})", sText);
                int row = 0;
                foreach (var op in L.ops)
                {
                    bool ok = Sim.Preview(L, st, tb, op, out int nv, out _);
                    string res = !ok ? $"<color={Html(ErrCol)}>SEGFAULT</color>"
                        : nv == 0 ? $"<color={Html(ErrCol)}>0  free()</color>"
                        : $"{Sim.Bin(nv, tb.bits)}  ({nv})";
                    bool lost = ok && Sim.LosesBit(op, tb.value, tb.bits);
                    Label(new Rect(ix + 12, iy + 32 + row * 24, 280, 24),
                        $"<color={Html(Dim)}>[{KeyFor(op)}]</color> {Sim.OpSymbol(op),-3}  {res}{(lost ? $" <color={Html(WarnCol)}>bit lost</color>" : "")}", sSmall);
                    row++;
                }
            }
            else
            {
                Label(new Rect(ix + 12, iy + 6, 280, 24), $"<color={Html(Dim)}>face a register to inspect it</color>", sText);
                int row = 0;
                foreach (var op in L.ops)
                {
                    Label(new Rect(ix + 12, iy + 32 + row * 24, 280, 24), $"<color={Html(Dim)}>[{KeyFor(op)}] {Sim.OpSymbol(op)}  {Sim.OpName(op)}</color>", sSmall);
                    row++;
                }
            }

            // hint
            if (showHint && !string.IsNullOrEmpty(L.hint))
            {
                var hs = new GUIStyle(sCenter) { fontSize = 17 };
                float hw = Mathf.Min(920f, vw - 32f);
                var hr = new Rect(vw / 2 - hw / 2, vh - 118, hw, 54);
                GUI.Box(hr, GUIContent.none, sBar);
                Label(new Rect(hr.x + 10, hr.y + 4, hr.width - 20, hr.height - 8), $"<color={Html(Accent)}>//</color> {L.hint}", hs);
            }

            // bottom bar + buttons
            GUI.Box(new Rect(-4, vh - 58, vw + 8, 62), GUIContent.none, sBar);
            float bx = 14, by = vh - 50;
            foreach (var op in L.ops)
            {
                if (Button(new Rect(bx, by, 150, 40), KeyLabel(KeyFor(op), $"{Sim.OpName(op)} {Sim.OpSymbol(op)}"))) DoAction(4 + (int)op);
                bx += 158;
            }
            float rx = vw - 14;
            rx -= 132; if (Button(new Rect(rx, by, 132, 40), KeyLabel("ESC", "MENU"))) ToSelect();
            rx -= 140; if (Button(new Rect(rx, by, 132, 40), KeyLabel("R", "RESET"))) Restart();
            rx -= 140; if (Button(new Rect(rx, by, 132, 40), KeyLabel("Z", "UNDO"))) Undo();
            rx -= 140; if (Button(new Rect(rx, by, 132, 40), KeyLabel("H", showHint ? "HIDE HINT" : "HINT"))) showHint = !showHint;
        }

        void DrawDone()
        {
            float a = Mathf.Clamp01(modeTime * 4f);
            GUI.color = new Color(1, 1, 1, a);
            var r = new Rect(vw / 2 - 280, vh / 2 - 150, 560, 290);
            GUI.DrawTexture(new Rect(0, 0, vw, vh), shadeTex);
            GUI.Box(r, GUIContent.none, sPanel);
            Label(new Rect(r.x, r.y + 24, r.width, 40), $"<color={Html(Accent)}>PROCESS EXITED (0)</color>", sBig);
            Label(new Rect(r.x, r.y + 74, r.width, 30), L.title, sCenter);
            bool optimal = st.actions <= L.par;
            Label(new Rect(r.x, r.y + 112, r.width, 30), $"actions {st.actions}   par {L.par}", sCenter);
            Label(new Rect(r.x, r.y + 144, r.width, 30), optimal ? $"<color={Html(Accent)}>* OPTIMAL SOLUTION *</color>" : $"<color={Html(Dim)}>par is {L.par}. can you shave some cycles?</color>", sCenter);
            string nextLabel = levelIndex + 1 < levels.Count ? "NEXT [ENTER]" : "FINISH [ENTER]";
            if (Button(new Rect(r.x + 30, r.y + 206, 160, 44), "RETRY [R]")) Restart();
            if (Button(new Rect(r.x + 200, r.y + 206, 160, 44), "MENU [ESC]")) ToSelect();
            if (Button(new Rect(r.x + 370, r.y + 206, 160, 44), nextLabel)) Next();
            GUI.color = Color.white;
        }

        void DrawEnd()
        {
            int optimal = 0, total = 0;
            for (int i = 0; i < levels.Count; i++)
            {
                if (Best(i) > 0) total++;
                if (Best(i) > 0 && Best(i) <= levels[i].par) optimal++;
            }
            Label(new Rect(0, 150, vw, 60), $"<color={Html(Accent)}>ALL PROCESSES TERMINATED</color>", sBig);
            Label(new Rect(0, 230, vw, 30), $"{total}/{levels.Count} programs complete    {optimal}/{levels.Count} optimal", sCenter);
            Label(new Rect(0, 290, vw, 90), "You learned that << doubles, >> halves,\nand that every register runs out of bits eventually.", sCenter);
            Label(new Rect(0, 400, vw, 30), "Thanks for playing BITSHIFT!", sCenter);
            if (Button(new Rect(vw / 2 - 110, 470, 220, 44), "BACK TO MENU")) ToSelect();
            Label(new Rect(0, 660, vw, 26), $"<color={Html(Dim)}>made for GitHub Game Off 2026  //  theme: SHIFT</color>", sCenter);
        }
    }
}
