"""BFS solver for BITSHIFT levels.

Mirrors the rules in Assets/Scripts/Sim.cs exactly. Used to check every level is
solvable and to compute the "par" (fewest actions). Run:

    python tools/solver.py            # solve all levels
    python tools/solver.py 5          # solve level 5 (1-based) and print the solution
    python tools/solver.py --write    # solve all and store par values in levels.txt
"""
import sys
from collections import deque
from pathlib import Path

LEVELS = Path(__file__).resolve().parent.parent / "Assets" / "Resources" / "levels.txt"
DIRS = {"U": (0, -1), "D": (0, 1), "L": (-1, 0), "R": (1, 0)}


def parse(text):
    levels, cur, mode = [], None, None
    for raw in text.splitlines():
        line = raw.rstrip()
        if line.startswith("==="):
            cur = {"title": line[3:].strip(), "map": [], "blocks": {}, "ops": [], "hint": "", "par": 0}
            levels.append(cur)
            mode = None
        elif cur is None:
            continue
        elif line.startswith("hint:"):
            cur["hint"] = line[5:].strip()
        elif line.startswith("ops:"):
            cur["ops"] = line[4:].split()
        elif line.startswith("par:"):
            cur["par"] = int(line[4:])
        elif line == "map:":
            mode = "map"
        elif line == "blocks:":
            mode = "blocks"
        elif mode == "map" and line:
            cur["map"].append(line)
        elif mode == "blocks" and line:
            k, o, v, b = line.split()
            cur["blocks"][k] = (o, int(v), int(b))
    return levels


def build(lv):
    walls, pits, pads_cells, exitp, player = set(), set(), set(), None, None
    letters = {}
    h = len(lv["map"])
    w = max(len(r) for r in lv["map"])
    for y, row in enumerate(lv["map"]):
        for x in range(w):
            c = row[x] if x < len(row) else "#"
            if c == "#":
                walls.add((x, y))
            elif c == "_":
                pits.add((x, y))
            elif c == "+":
                pads_cells.add((x, y))
            elif c == "X":
                exitp = (x, y)
            elif c == "P":
                player = (x, y)
            elif c.isalpha() and c.islower():
                letters.setdefault(c, []).append((x, y))
    # pads: connected components
    pads = components(pads_cells)
    trenches = components(pits)
    assert player and exitp, f"{lv['title']}: needs P and X"
    blocks = []
    for k in sorted(letters):
        o, v, b = lv["blocks"][k]
        cells = sorted(letters[k], key=lambda p: (p[1], p[0]))
        ax, ay = cells[0]
        assert len(cells) == v, f"{lv['title']}: block {k} has {len(cells)} cells but value {v}"
        assert cells == block_cells(ax, ay, o, v), f"{lv['title']}: block {k} shape mismatch"
        assert v < (1 << b), f"{lv['title']}: block {k} value overflows {b} bits"
        blocks.append((ax, ay, o, v, b))
    return dict(walls=walls, pits=frozenset(pits), pads=pads, trenches=trenches, exit=exitp, player=player,
                blocks=tuple(sorted(blocks)), w=w, h=h, ops=lv["ops"])


def components(cells):
    out, seen = [], set()
    for c in sorted(cells):
        if c in seen:
            continue
        comp, st = [], [c]
        seen.add(c)
        while st:
            p = st.pop()
            comp.append(p)
            for dx, dy in DIRS.values():
                q = (p[0] + dx, p[1] + dy)
                if q in cells and q not in seen:
                    seen.add(q)
                    st.append(q)
        out.append(frozenset(comp))
    return out


def block_cells(ax, ay, o, v):
    if o == "H":
        return [(ax + i, ay) for i in range(v)]
    return [(ax, ay + i) for i in range(v)]


def shift(op, v, bits):
    mask = (1 << bits) - 1
    if op == "shl":
        return (v << 1) & mask
    if op == "shr":
        return v >> 1
    if op == "rol":
        return ((v << 1) | (v >> (bits - 1))) & mask
    if op == "ror":
        return ((v >> 1) | ((v & 1) << (bits - 1))) & mask
    raise ValueError(op)


def exit_open(L, blocks):
    sets = [frozenset(block_cells(*b[:4])) for b in blocks]
    return all(p in sets for p in L["pads"])


def step(L, state, action):
    """Returns new state or None if the action has no effect. state=(player, facing, blocks, pits)"""
    (px, py), facing, blocks, pits = state
    walls = L["walls"]
    occ = {}
    for i, b in enumerate(blocks):
        for c in block_cells(*b[:4]):
            occ[c] = i
    if action in DIRS:
        dx, dy = DIRS[action]
        t = (px + dx, py + dy)
        if t in walls or t in pits:
            return ((px, py), action, blocks, pits) if facing != action else None
        if t == L["exit"] and not exit_open(L, blocks):
            return ((px, py), action, blocks, pits) if facing != action else None
        if t in occ:
            i = occ[t]
            ax, ay, o, v, bb = blocks[i]
            old = set(block_cells(ax, ay, o, v))
            new = block_cells(ax + dx, ay + dy, o, v)
            ok, inpit = True, 0
            for c in new:
                if c in walls or c == L["exit"] or (c in occ and occ[c] != i):
                    ok = False
                    break
                if c in pits:
                    inpit += 1
            # a block may only enter a pit if it covers that whole trench exactly
            if ok and inpit and frozenset(new) not in L["trenches"]:
                ok = False
            if not ok:
                return ((px, py), action, blocks, pits) if facing != action else None
            nb = list(blocks)
            if ok and inpit:
                del nb[i]
                pits = pits - frozenset(new)
            else:
                nb[i] = (ax + dx, ay + dy, o, v, bb)
            return (t, action, tuple(sorted(nb)), pits)
        return (t, action, blocks, pits)
    # shift op
    dx, dy = DIRS[facing]
    t = (px + dx, py + dy)
    if t not in occ:
        return None
    i = occ[t]
    ax, ay, o, v, bb = blocks[i]
    nv = shift(action, v, bb)
    if nv == v:
        return None
    nb = list(blocks)
    if nv == 0:
        del nb[i]
        return ((px, py), facing, tuple(sorted(nb)), pits)
    for c in block_cells(ax, ay, o, nv):
        if c in walls or c in pits or c == L["exit"] or c == (px, py) or (c in occ and occ[c] != i):
            return None
    nb[i] = (ax, ay, o, nv, bb)
    return ((px, py), facing, tuple(sorted(nb)), pits)


def solve(L, limit=6_000_000):
    start = (L["player"], "D", L["blocks"], L["pits"])
    actions = list(DIRS) + L["ops"]
    prev = {start: None}
    q = deque([start])
    while q:
        s = q.popleft()
        if s[0] == L["exit"]:
            path = []
            while prev[s]:
                s, a = prev[s]
                path.append(a)
            return path[::-1], len(prev)
        for a in actions:
            n = step(L, s, a)
            if n is not None and n not in prev:
                prev[n] = (s, a)
                q.append(n)
                if len(prev) > limit:
                    return None, len(prev)
    return None, len(prev)


def main():
    levels = parse(LEVELS.read_text())
    write = "--write" in sys.argv
    args = [a for a in sys.argv[1:] if a != "--write"]
    only = int(args[0]) if args else None
    pars, sols = {}, {}
    ok = True
    for n, lv in enumerate(levels, 1):
        if only and n != only:
            continue
        L = build(lv)
        path, explored = solve(L)
        if path is None:
            ok = False
            print(f"{n:2} {lv['title']:<22} UNSOLVABLE (explored {explored})")
            continue
        shifts = sum(1 for a in path if a not in DIRS)
        pars[lv["title"]] = len(path)
        sols[lv["title"]] = " ".join(path)
        flag = "" if lv["par"] == len(path) else f"  <-- par in file is {lv['par']}"
        print(f"{n:2} {lv['title']:<22} par {len(path):3}  shifts {shifts:2}  explored {explored}{flag}")
        if only:
            print(" ".join(path))
    if write and pars:
        out, title = [], None
        for line in LEVELS.read_text().splitlines():
            if line.startswith("==="):
                title = line[3:].strip()
            if line.startswith("par:") and title in pars:
                line = f"par: {pars[title]}"
            out.append(line)
        LEVELS.write_text("\n".join(out) + "\n")
        print("wrote par values to", LEVELS)
        if not only:
            sol_file = Path(__file__).resolve().parent / "solutions.txt"
            sol_file.write_text("".join(f"{t}: {p}\n" for t, p in sols.items()))
            print("wrote solutions to", sol_file)
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
