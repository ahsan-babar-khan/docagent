"""Renders docs/demo.gif as a terminal session from REAL captured API responses (demo.json + agent_runs.jsonl).
Waits are shortened in the animation; the real elapsed time of each call is printed after it."""
import json, sys, textwrap, pathlib
from PIL import Image, ImageDraw, ImageFont

SP = pathlib.Path(sys.argv[1]); OUT = pathlib.Path(sys.argv[2])
steps = json.loads((SP / "demo.json").read_text(encoding="utf-8"))
agent_runs = [json.loads(l) for l in (SP / "agent_runs.jsonl").read_text(encoding="utf-8").splitlines() if l.strip()]
agent = next(r for r in agent_runs if r["q"].startswith("Who is") and any("read_document" in t for t in r["response"]["trace"]))

W, H, PAD, LH, COLS = 960, 560, 18, 21, 86
font = ImageFont.truetype("C:/Windows/Fonts/consola.ttf", 16)
bold = ImageFont.truetype("C:/Windows/Fonts/consolab.ttf", 16)
BG, FG, DIM, GREEN, CYAN, YEL, RED, MAG = "#0d1117", "#e6edf3", "#7d8590", "#3fb950", "#58a6ff", "#d29922", "#f85149", "#bc8cff"
ROWS = (H - 2 * PAD - 30) // LH

lines: list[tuple[str, str, bool]] = []   # (text, colour, bold)
frames, durations = [], []

def snap(ms):
    img = Image.new("RGB", (W, H), BG); d = ImageDraw.Draw(img)
    d.rectangle([0, 0, W, 28], fill="#161b22")
    for i, c in enumerate(["#ff5f56", "#ffbd2e", "#27c93f"]): d.ellipse([12 + i * 20, 9, 24 + i * 20, 21], fill=c)
    d.text((W // 2 - 190, 6), "DocAgent  ·  local models via Ollama (qwen2.5:3b)", font=font, fill=DIM)
    for i, (t, c, b) in enumerate(lines[-ROWS:]):
        d.text((PAD, 28 + PAD + i * LH), t, font=bold if b else font, fill=c)
    frames.append(img); durations.append(ms)

def out(text, colour=FG, b=False, ms=0, width=COLS):
    for part in (textwrap.wrap(text, width, subsequent_indent="  ") or [""]):
        lines.append((part, colour, b))
    if ms: snap(ms)

def type_cmd(cmd):
    start = len(lines)
    for i in range(0, len(cmd) + 6, 6):
        del lines[start:]
        for j, part in enumerate(textwrap.wrap("$ " + cmd[:i], COLS, subsequent_indent="    ") or ["$ "]):
            lines.append((part, GREEN, True))
        snap(35)
    snap(250)

def wait(secs):
    for i in range(4):
        lines.append((f"  … waiting for the model{'.' * i}", DIM, False)); snap(180); lines.pop()
    out(f"  ({secs} s)", DIM)

def comment(t, ms=1400): out("# " + t, YEL, ms=ms)

# 1. upload
comment("1. Upload documents with metadata (chunked, embedded, indexed)", 900)
for s in steps:
    if s["kind"] != "upload": continue
    b = s["body"]
    type_cmd(f'curl -X POST :5000/documents -d \'{{"fileName":"{b["fileName"]}","project":"{b["project"]}",...}}\'')
    r = s["response"]; out(json.dumps(r, ensure_ascii=False), CYAN, ms=500)
snap(1200)

# 2. ask with citation
asks = [s for s in steps if s["kind"] == "ask"]
lines.clear(); comment("2. Ask: the answer cites its source file", 900)
type_cmd(f'curl -X POST :5000/ask -d \'{{"question":"{asks[0]["question"]}"}}\'')
wait(asks[0]["secs"]); out(json.dumps(asks[0]["response"], ensure_ascii=False), CYAN, ms=2600)

# 3. refusal
out("")
comment("3. Off-topic question: refused BEFORE the LLM is called (no tokens, no guessing)", 900)
type_cmd(f'curl -X POST :5000/ask -d \'{{"question":"{asks[1]["question"]}"}}\'')
out(f'  ({asks[1]["secs"]} s)', DIM); out(json.dumps(asks[1]["response"], ensure_ascii=False), CYAN, ms=3000)

# 4. agent
lines.clear(); comment("4. Agent: the model picks the tools; access is checked in code", 900)
type_cmd(f'curl -X POST :5000/agent -H "X-Allowed-Projects: Apollo" -d \'{{"question":"{agent["q"]}"}}\'')
wait(agent["secs"])
out("trace:", MAG, True)
for t in agent["response"]["trace"]: out("  → " + t, MAG, ms=700)
out("answer:", CYAN, True); out(agent["response"]["answer"], CYAN, ms=3200)
out("")
comment("The tools only ever return documents from the caller's allowed projects.", 4000)

frames[0].save(OUT, save_all=True, append_images=frames[1:], duration=durations, loop=0, optimize=True)
print(len(frames), "frames,", round(sum(durations) / 1000, 1), "s,", OUT.stat().st_size // 1024, "KB")
