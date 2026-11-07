// Pure game rules for BITSHIFT. Kept free of rendering so it mirrors tools/solver.py 1:1.
using System.Collections.Generic;
using UnityEngine;

namespace BitShift
{
    public enum Op { Shl, Shr, Rol, Ror }

    public enum Ev { None, Move, Turn, Bump, Push, Fill, Shift, Free, Segfault, NoTarget, Win }

    public class Block
    {
        public int id, x, y, value, bits;
        public bool vertical;
        public char letter;

        public Block Clone() => (Block)MemberwiseClone();
        public int Mask => (1 << bits) - 1;

        public List<Vector2Int> Cells(int v)
        {
            var list = new List<Vector2Int>(v);
            for (int i = 0; i < v; i++)
                list.Add(vertical ? new Vector2Int(x, y + i) : new Vector2Int(x + i, y));
            return list;
        }

        public List<Vector2Int> Cells() => Cells(value);
    }

    public class LevelDef
    {
        public string title = "", hint = "";
        public List<Op> ops = new List<Op>();
        public int par, w, h;
        public char[,] tiles;              // '#', '.', '_', '+', 'X'
        public List<HashSet<Vector2Int>> pads = new List<HashSet<Vector2Int>>();
        public List<HashSet<Vector2Int>> trenches = new List<HashSet<Vector2Int>>();
        public Vector2Int start, exit;
        public List<Block> blocks = new List<Block>();

        public char At(Vector2Int p) =>
            (p.x < 0 || p.y < 0 || p.x >= w || p.y >= h) ? '#' : tiles[p.x, p.y];
    }

    public class State
    {
        public Vector2Int player;
        public int facing = 2;
        public List<Block> blocks = new List<Block>();
        public Dictionary<Vector2Int, int> filled = new Dictionary<Vector2Int, int>(); // pit cell -> bits of block that filled it
        public int actions;

        public State Clone()
        {
            var s = new State { player = player, facing = facing, actions = actions };
            foreach (var b in blocks) s.blocks.Add(b.Clone());
            s.filled = new Dictionary<Vector2Int, int>(filled);
            return s;
        }
    }

    public struct StepInfo
    {
        public Ev ev;
        public int blockId;
        public Op op;
        public int oldV, newV, bits;
        public bool bitLost;
        public Vector2Int at;
        public List<Vector2Int> cells;
    }

    public static class Sim
    {
        public static readonly Vector2Int[] Dirs =
        {
            new Vector2Int(0, -1), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, 0)
        };

        public static string OpName(Op op) => op switch
        {
            Op.Shl => "SHL", Op.Shr => "SHR", Op.Rol => "ROL", _ => "ROR"
        };

        public static string OpSymbol(Op op) => op switch
        {
            Op.Shl => "<<", Op.Shr => ">>", Op.Rol => "<<<", _ => ">>>"
        };

        public static int Apply(Op op, int v, int bits)
        {
            int mask = (1 << bits) - 1;
            switch (op)
            {
                case Op.Shl: return (v << 1) & mask;
                case Op.Shr: return v >> 1;
                case Op.Rol: return ((v << 1) | (v >> (bits - 1))) & mask;
                default: return ((v >> 1) | ((v & 1) << (bits - 1))) & mask;
            }
        }

        public static bool LosesBit(Op op, int v, int bits) => op switch
        {
            Op.Shl => ((v >> (bits - 1)) & 1) == 1,
            Op.Shr => (v & 1) == 1,
            _ => false
        };

        public static string Bin(int v, int bits)
        {
            var c = new char[bits];
            for (int i = 0; i < bits; i++) c[i] = ((v >> (bits - 1 - i)) & 1) == 1 ? '1' : '0';
            return new string(c);
        }

        // ---------------------------------------------------------------- parsing
        public static List<LevelDef> Parse(string text)
        {
            var levels = new List<LevelDef>();
            LevelDef cur = null;
            List<string> map = null;
            var blockDefs = new Dictionary<char, string[]>();
            string mode = null;

            void Finish()
            {
                if (cur == null) return;
                Build(cur, map, blockDefs);
                levels.Add(cur);
            }

            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                var line = raw.TrimEnd();
                if (line.StartsWith("==="))
                {
                    Finish();
                    cur = new LevelDef { title = line.Substring(3).Trim() };
                    map = new List<string>();
                    blockDefs = new Dictionary<char, string[]>();
                    mode = null;
                }
                else if (cur == null) continue;
                else if (line.StartsWith("hint:")) cur.hint = line.Substring(5).Trim();
                else if (line.StartsWith("par:")) cur.par = int.Parse(line.Substring(4).Trim());
                else if (line.StartsWith("ops:"))
                {
                    foreach (var o in line.Substring(4).Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries))
                        cur.ops.Add(o == "shl" ? Op.Shl : o == "shr" ? Op.Shr : o == "rol" ? Op.Rol : Op.Ror);
                }
                else if (line == "map:") mode = "map";
                else if (line == "blocks:") mode = "blocks";
                else if (mode == "map" && line.Length > 0) map.Add(line);
                else if (mode == "blocks" && line.Length > 0)
                {
                    var parts = line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                    blockDefs[parts[0][0]] = parts;
                }
            }
            Finish();
            return levels;
        }

        static void Build(LevelDef L, List<string> map, Dictionary<char, string[]> defs)
        {
            L.h = map.Count;
            L.w = 0;
            foreach (var r in map) L.w = Mathf.Max(L.w, r.Length);
            L.tiles = new char[L.w, L.h];
            var letters = new SortedDictionary<char, List<Vector2Int>>();
            var padCells = new HashSet<Vector2Int>();
            var pitCells = new HashSet<Vector2Int>();
            for (int y = 0; y < L.h; y++)
                for (int x = 0; x < L.w; x++)
                {
                    char c = x < map[y].Length ? map[y][x] : '#';
                    var p = new Vector2Int(x, y);
                    char t = '.';
                    if (c == '#') t = '#';
                    else if (c == '_') { t = '_'; pitCells.Add(p); }
                    else if (c == '+') { t = '+'; padCells.Add(p); }
                    else if (c == 'X') { t = 'X'; L.exit = p; }
                    else if (c == 'P') L.start = p;
                    else if (char.IsLower(c))
                    {
                        if (!letters.TryGetValue(c, out var l)) letters[c] = l = new List<Vector2Int>();
                        l.Add(p);
                    }
                    L.tiles[x, y] = t;
                }
            L.pads = Components(padCells);
            L.trenches = Components(pitCells);
            int id = 1;
            foreach (var kv in letters)
            {
                var d = defs[kv.Key];
                kv.Value.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
                L.blocks.Add(new Block
                {
                    id = id++, letter = kv.Key, x = kv.Value[0].x, y = kv.Value[0].y,
                    vertical = d[1] == "V", value = int.Parse(d[2]), bits = int.Parse(d[3])
                });
            }
        }

        static List<HashSet<Vector2Int>> Components(HashSet<Vector2Int> cells)
        {
            var result = new List<HashSet<Vector2Int>>();
            var seen = new HashSet<Vector2Int>();
            foreach (var c in cells)
            {
                if (seen.Contains(c)) continue;
                var comp = new HashSet<Vector2Int>();
                var stack = new Stack<Vector2Int>();
                stack.Push(c);
                seen.Add(c);
                while (stack.Count > 0)
                {
                    var p = stack.Pop();
                    comp.Add(p);
                    foreach (var d in Dirs)
                    {
                        var q = p + d;
                        if (cells.Contains(q) && seen.Add(q)) stack.Push(q);
                    }
                }
                result.Add(comp);
            }
            return result;
        }

        // ---------------------------------------------------------------- rules
        public static State Initial(LevelDef L)
        {
            var s = new State { player = L.start, facing = 2 };
            foreach (var b in L.blocks) s.blocks.Add(b.Clone());
            return s;
        }

        public static bool IsPit(LevelDef L, State s, Vector2Int p) => L.At(p) == '_' && !s.filled.ContainsKey(p);

        public static Block BlockAt(State s, Vector2Int p)
        {
            foreach (var b in s.blocks)
            {
                if (b.vertical ? (p.x == b.x && p.y >= b.y && p.y < b.y + b.value)
                               : (p.y == b.y && p.x >= b.x && p.x < b.x + b.value)) return b;
            }
            return null;
        }

        public static bool PadSatisfied(State s, HashSet<Vector2Int> pad)
        {
            foreach (var b in s.blocks)
            {
                if (b.value != pad.Count) continue;
                bool all = true;
                foreach (var c in b.Cells()) if (!pad.Contains(c)) { all = false; break; }
                if (all) return true;
            }
            return false;
        }

        public static bool ExitOpen(LevelDef L, State s)
        {
            foreach (var pad in L.pads) if (!PadSatisfied(s, pad)) return false;
            return true;
        }

        public static Block Target(State s) => BlockAt(s, s.player + Dirs[s.facing]);

        /// Result of applying op to the faced block without committing it. ok=false means SEGFAULT.
        public static bool Preview(LevelDef L, State s, Block b, Op op, out int nv, out Vector2Int hit)
        {
            nv = Apply(op, b.value, b.bits);
            hit = default;
            if (nv == 0 || nv <= b.value) return true;
            foreach (var c in b.Cells(nv))
            {
                var other = BlockAt(s, c);
                if (L.At(c) == '#' || IsPit(L, s, c) || L.At(c) == 'X' || c == s.player || (other != null && other != b))
                {
                    hit = c;
                    return false;
                }
            }
            return true;
        }

        /// Mutates s. action 0..3 = move direction, 4+ = (Op)(action-4)
        public static StepInfo Step(LevelDef L, State s, int action)
        {
            var info = new StepInfo { ev = Ev.None };
            if (action < 4)
            {
                var d = Dirs[action];
                var t = s.player + d;
                bool turned = s.facing != action;
                s.facing = action;
                info.at = t;
                var blockedEv = turned ? Ev.Turn : Ev.Bump;
                if (L.At(t) == '#' || IsPit(L, s, t) || (t == L.exit && !ExitOpen(L, s)))
                {
                    info.ev = blockedEv;
                    if (turned) s.actions++;
                    return info;
                }
                var b = BlockAt(s, t);
                if (b != null)
                {
                    var moved = new List<Vector2Int>();
                    bool ok = true;
                    int inPit = 0;
                    foreach (var c in b.Cells())
                    {
                        var n = c + d;
                        moved.Add(n);
                        var other = BlockAt(s, n);
                        if (L.At(n) == '#' || n == L.exit || (other != null && other != b)) { ok = false; break; }
                        if (IsPit(L, s, n)) inPit++;
                    }
                    if (ok && inPit > 0)
                    {
                        ok = false;
                        foreach (var tr in L.trenches)
                            if (tr.Count == moved.Count && tr.SetEquals(moved)) { ok = true; break; }
                    }
                    if (!ok)
                    {
                        info.ev = blockedEv;
                        info.blockId = b.id;
                        if (turned) s.actions++;
                        return info;
                    }
                    s.player = t;
                    s.actions++;
                    info.blockId = b.id;
                    info.bits = b.bits;
                    if (inPit > 0)
                    {
                        foreach (var c in moved) s.filled[c] = b.bits;
                        s.blocks.Remove(b);
                        info.ev = Ev.Fill;
                        info.cells = moved;
                    }
                    else
                    {
                        b.x += d.x;
                        b.y += d.y;
                        info.ev = Ev.Push;
                    }
                    return info;
                }
                s.player = t;
                s.actions++;
                info.ev = t == L.exit ? Ev.Win : Ev.Move;
                return info;
            }

            var op = (Op)(action - 4);
            info.op = op;
            var tb = Target(s);
            info.at = s.player + Dirs[s.facing];
            if (tb == null) { info.ev = Ev.NoTarget; return info; }
            info.blockId = tb.id;
            info.oldV = tb.value;
            info.bits = tb.bits;
            if (!Preview(L, s, tb, op, out int nv, out var hit))
            {
                info.ev = Ev.Segfault;
                info.newV = nv;
                info.at = hit;
                return info;
            }
            info.newV = nv;
            info.bitLost = LosesBit(op, tb.value, tb.bits);
            if (nv == tb.value) { info.ev = Ev.NoTarget; return info; }
            s.actions++;
            if (nv == 0)
            {
                info.cells = tb.Cells();
                s.blocks.Remove(tb);
                info.ev = Ev.Free;
                return info;
            }
            tb.value = nv;
            info.ev = Ev.Shift;
            return info;
        }
    }
}
