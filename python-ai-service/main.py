import asyncio
import os
import re
import socket
import struct
import subprocess
import tempfile
import time
import json as _json
import textwrap
import shutil
from pathlib import Path
from uuid import UUID, uuid4

import httpx
from fastapi import FastAPI
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel
from typing import Optional

app = FastAPI(title="AI Visual Learning - Python AI Service")

OLLAMA_URL = os.getenv("OLLAMA_URL", "http://localhost:11434")
OLLAMA_MODEL = os.getenv("OLLAMA_MODEL", "gemma3:4b")
PIPER_HOST = os.getenv("PIPER_HOST", "localhost")
PIPER_PORT = int(os.getenv("PIPER_PORT", "10200"))
OUTPUT_DIR = Path(os.getenv("OUTPUT_DIR", os.getenv("BLENDER_OUTPUT_DIR", "../output")))
OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
OUTPUT_DIR_HOST = str(OUTPUT_DIR.resolve())

# Serve generated files at /output/<filename>
app.mount("/output", StaticFiles(directory=str(OUTPUT_DIR)), name="output")


# ── Pydantic models ──────────────────────────────────────────────────────────

class GenerationSettings(BaseModel):
    narration: bool = False
    labels: bool = True
    difficulty: str = "beginner"
    detailLevel: str = "educational"
    renderingStyle: str = "schematic"


class GenerationContract(BaseModel):
    jobId: UUID
    sessionId: UUID
    visualizationType: str
    concept: str
    domain: str
    components: list[str]
    settings: GenerationSettings
    fallbackAttempt: int = 0


class GenerationMetadata(BaseModel):
    componentsCovered: list[str]
    generationDurationSeconds: float


class GenerationResult(BaseModel):
    jobId: UUID
    status: str
    outputType: Optional[str] = None
    outputUrl: Optional[str] = None
    outputContent: Optional[str] = None
    errorCode: Optional[str] = None
    retryable: bool = False
    metadata: Optional[GenerationMetadata] = None


# ── Ollama helper ────────────────────────────────────────────────────────────

async def call_ollama(prompt: str) -> str:
    payload = {"model": OLLAMA_MODEL, "prompt": prompt, "stream": False}
    async with httpx.AsyncClient(timeout=120) as client:
        resp = await client.post(f"{OLLAMA_URL}/api/generate", json=payload)
        resp.raise_for_status()
        return resp.json()["response"]


# ── Node ID helpers ──────────────────────────────────────────────────────────

def to_node_id(label: str) -> str:
    return re.sub(r"[^a-zA-Z0-9]", "_", label).strip("_")


def build_nodes_block(components: list[str]) -> str:
    lines = []
    for c in components:
        nid = to_node_id(c)
        lines.append(f'    {nid}["{c}"]')
    return "\n".join(lines)


# ── Mermaid sanitizer ────────────────────────────────────────────────────────

def strip_fences(raw: str) -> str:
    raw = re.sub(r"```(?:mermaid)?\s*", "", raw)
    raw = raw.replace("```", "")
    return raw.strip()


def find_diagram_start(raw: str) -> str:
    for kw in ["graph TD", "graph LR", "flowchart TD", "flowchart LR",
                "graph TB", "flowchart TB", "sequenceDiagram", "classDiagram"]:
        idx = raw.find(kw)
        if idx >= 0:
            return raw[idx:]
    return raw


def quote_bare_node_labels(mermaid: str) -> str:
    lines = mermaid.splitlines()
    fixed = []
    for line in lines:
        stripped = line.strip()
        if not stripped or stripped.startswith("%%") or stripped.startswith("graph") or stripped.startswith("flowchart"):
            fixed.append(line)
            continue

        line = re.sub(
            r'\[([^\]"\']+)\]',
            lambda m: f'["{m.group(1).strip()}"]' if ' ' in m.group(1) else m.group(0),
            line
        )
        line = re.sub(
            r'\(([^)"\']+)\)',
            lambda m: f'("{m.group(1).strip()}")' if ' ' in m.group(1) else m.group(0),
            line
        )
        line = fix_bare_node_refs(line)
        fixed.append(line)
    return "\n".join(fixed)


def fix_bare_node_refs(line: str) -> str:
    edge_pattern = re.compile(r'(--+>|==+>|--+\|[^|]*\|-->|--[^-].*?-->|\.\.\.|:::|\|)')
    parts = edge_pattern.split(line)
    result = []
    for part in parts:
        if edge_pattern.match(part):
            result.append(part)
        else:
            if re.search(r'[\[(]["\']', part):
                result.append(part)
            else:
                part = re.sub(
                    r'\b([A-Z][a-z]+(?:\s+[A-Z][a-z]+)+)\b',
                    lambda m: re.sub(r'\s+', '', m.group(0)),
                    part
                )
                result.append(part)
    return "".join(result)


VALID_MERMAID_STARTS = (
    "graph td", "graph lr", "graph tb", "graph rl",
    "flowchart td", "flowchart lr", "flowchart tb", "flowchart rl",
    "sequencediagram", "classdiagram", "erdiagram", "gantt", "pie",
)

def sanitize_mermaid(raw: str) -> str:
    raw = strip_fences(raw)
    raw = find_diagram_start(raw)
    raw = quote_bare_node_labels(raw)
    cleaned = raw.strip()
    if not any(cleaned.lower().startswith(kw) for kw in VALID_MERMAID_STARTS):
        return ""
    return cleaned


def build_safe_fallback_diagram(components: list[str], topic: str) -> str:
    lines = ["graph TD"]
    topic_id = to_node_id(topic) or "Topic"
    lines.append(f'    {topic_id}["{topic}"]')
    for c in components:
        nid = to_node_id(c) or f"N{len(lines)}"
        lines.append(f'    {topic_id} --> {nid}["{c}"]')
    return "\n".join(lines)


# ── Mermaid generation ───────────────────────────────────────────────────────

async def generate_mermaid(contract: GenerationContract) -> str:
    viz = contract.visualizationType
    difficulty = contract.settings.difficulty

    diagram_type = "flowchart TD" if viz == "flowchart" else "graph TD"
    node_ids = {c: to_node_id(c) for c in contract.components}
    node_list = "\n".join(f'  - {nid} (represents: {label})' for label, nid in node_ids.items())

    prompt = (
        f"Generate a valid Mermaid {diagram_type} diagram for the topic '{contract.concept}'.\n\n"
        f"RULES — follow exactly:\n"
        f"1. Start with exactly: {diagram_type}\n"
        f"2. Use ONLY these node IDs (no spaces allowed in IDs):\n{node_list}\n"
        f"3. Connect every node with at least one edge using --> or -- label -->\n"
        f"4. Edge labels must be quoted: A -- \"relates to\" --> B\n"
        f"5. Do NOT use node labels with brackets unless quoted: NodeId[\"Label text\"]\n"
        f"6. Output ONLY the diagram — no explanation, no markdown fences, no ```\n"
        f"7. Difficulty level: {difficulty}\n\n"
        f"Output the {diagram_type} diagram now:"
    )

    raw = await call_ollama(prompt)
    return sanitize_mermaid(raw)


def validate_mermaid(mermaid: str, required_components: list[str]) -> Optional[str]:
    if not mermaid or len(mermaid) < 20:
        return "EMPTY_DIAGRAM"
    mermaid_lower = mermaid.lower()
    hits = sum(
        1 for c in required_components
        if any(word.lower() in mermaid_lower for word in c.split() if len(word) > 3)
    )
    if hits < max(1, len(required_components) // 2):
        return "MISSING_COMPONENTS"
    return None


# ── Video generation — educational pipeline via matplotlib + ffmpeg ───────────

# Palette
_BG       = "#0a0a14"
_PANEL    = "#0d0d1f"
_BORDER   = "#1e1e2e"
_ACTIVE   = "#4f46e5"   # highlighted node fill
_VISITED  = "#1e3a5f"   # already-processed node
_INACTIVE = "#1a1a2e"   # not yet reached
_ARROW    = "#6366f1"
_TEXT_HI  = "#ffffff"
_TEXT_LO  = "#94a3b8"
_TEXT_EXP = "#cbd5e1"
_ACCENT   = "#818cf8"

DOMAIN_ICONS = {
    "mechanical": "⚙", "medical": "🩺", "electrical": "⚡",
    "computer_science": "💻", "chemistry": "🔬", "biology": "🧬",
    "defence": "🛡", "robotics": "🤖", "default": "📚",
}

BLENDER_CONTAINER = os.getenv("BLENDER_CONTAINER", "visuallearning-blender")
BLENDER_CONTAINER_OUTPUT = "/output"
FFMPEG_CONTAINER = os.getenv("FFMPEG_CONTAINER", "visuallearning-ffmpeg")
FFMPEG_CONTAINER_OUTPUT = "/out"


def _node_positions(n: int) -> list[tuple[float, float]]:
    """Return (x, y) positions for n nodes. Ring layout for <=8, grid for more."""
    import math
    if n == 1:
        return [(0.5, 0.50)]
    if n <= 8:
        positions = []
        for i in range(n):
            angle = math.pi / 2 - 2 * math.pi * i / n
            # Wider ring so shapes don't overlap: rx=0.36, ry=0.30
            x = 0.5 + 0.36 * math.cos(angle)
            y = 0.50 + 0.30 * math.sin(angle)
            positions.append((x, y))
        return positions
    # Grid layout for > 8 nodes
    cols = math.ceil(math.sqrt(n))
    rows = math.ceil(n / cols)
    positions = []
    for i in range(n):
        col = i % cols
        row = i // cols
        x = 0.12 + col * (0.76 / max(cols - 1, 1))
        y = 0.88 - row * (0.76 / max(rows - 1, 1))
        positions.append((x, y))
    return positions


# ── Stage 1: Storyboard generation ──────────────────────────────────────────

async def _generate_storyboard(contract: GenerationContract) -> dict:
    """Call Ollama to produce a structured JSON storyboard. Falls back gracefully."""
    components = contract.components
    domain = contract.domain
    concept = contract.concept
    difficulty = contract.settings.difficulty

    schema_example = {
        "title": concept,
        "domain": domain,
        "difficulty": difficulty,
        "total_duration_seconds": len(components) * 6,
        "scenes": [
            {
                "scene_id": 1,
                "title": f"Introduction to {components[0] if components else concept}",
                "duration_seconds": 6,
                "narration": f"Let us explore {components[0] if components else concept} in {concept}.",
                "visual_description": f"Show {components[0] if components else concept} as the central component.",
                "objects": [
                    {"id": "obj1", "label": components[0] if components else concept,
                     "type": "component", "role": "primary", "connects_to": []}
                ],
                "active_object": "obj1",
                "transition": "fade",
            }
        ],
    }

    prompt = (
        f"You are an educational video script writer.\n"
        f"Create a detailed storyboard for a video about '{concept}' at {difficulty} level.\n"
        f"Domain: {domain}\n"
        f"Components to cover (in order): {', '.join(components)}\n\n"
        f"Return ONLY a single JSON object matching this exact schema (no prose, no markdown):\n"
        f"{_json.dumps(schema_example, indent=2)}\n\n"
        f"Rules:\n"
        f"- Create exactly {len(components)} scenes, one per component\n"
        f"- Each scene duration_seconds: between 4 and 8\n"
        f"- narration: 1-2 clear educational sentences suitable for TTS\n"
        f"- objects: list all components as nodes; use connects_to to show relationships\n"
        f"- active_object: the id of the component being explained in this scene\n"
        f"- transition: one of 'fade', 'slide', 'cut'\n"
        f"- Output ONLY the JSON object, starting with {{ and ending with }}"
    )

    try:
        raw = await asyncio.wait_for(call_ollama(prompt), timeout=90)
        match = re.search(r'\{[\s\S]*\}', raw)
        if match:
            parsed = _json.loads(match.group(0))
            if isinstance(parsed, dict) and "scenes" in parsed and len(parsed["scenes"]) > 0:
                for i, scene in enumerate(parsed["scenes"]):
                    scene.setdefault("scene_id", i + 1)
                    scene.setdefault("duration_seconds", 6)
                    scene["duration_seconds"] = max(4, min(8, int(scene["duration_seconds"])))
                    scene.setdefault("narration", f"Now let us look at {concept}.")
                    scene.setdefault("visual_description", "")
                    scene.setdefault("objects", [])
                    scene.setdefault("active_object", "")
                    scene.setdefault("transition", "fade")
                return parsed
    except Exception as e:
        print(f"[storyboard] LLM parse failed: {e}", flush=True)

    # Fallback: build minimal storyboard from components
    print("[storyboard] Using fallback storyboard", flush=True)
    scenes = []
    for i, comp in enumerate(components):
        obj_id = f"obj{i+1}"
        objects = []
        for j, c2 in enumerate(components):
            objects.append({
                "id": f"obj{j+1}", "label": c2,
                "type": "component", "role": "primary" if j == i else "secondary",
                "connects_to": [f"obj{j+2}"] if j < len(components) - 1 else [],
            })
        scenes.append({
            "scene_id": i + 1,
            "title": comp,
            "duration_seconds": 6,
            "narration": f"In {concept}, {comp} plays a crucial role. "
                         f"It connects the system components together.",
            "visual_description": f"Highlight {comp} in the process diagram.",
            "objects": objects,
            "active_object": obj_id,
            "transition": "fade",
        })
    return {
        "title": concept,
        "domain": domain,
        "difficulty": difficulty,
        "total_duration_seconds": len(components) * 6,
        "scenes": scenes,
    }


# ── Stage 2: Scene graph rendering ───────────────────────────────────────────

# Component shape catalogue — each entry is (shape_fn, symbol, color_hint)
# shape_fn(ax, cx, cy, size, fill, ec, lw) draws the shape onto ax
def _shape_piston(ax, cx, cy, sz, fill, ec, lw):
    import matplotlib.patches as mp
    # Body: tall rectangle
    body = mp.FancyBboxPatch((cx - sz * 0.5, cy - sz * 0.55), sz, sz * 1.1,
                              boxstyle="round,pad=0.01", facecolor=fill, edgecolor=ec, lw=lw, zorder=3)
    ax.add_patch(body)
    # Crown: wider top bar
    crown = mp.FancyBboxPatch((cx - sz * 0.55, cy + sz * 0.52), sz * 1.1, sz * 0.18,
                               boxstyle="round,pad=0.01", facecolor=ec, edgecolor=ec, lw=lw * 0.5, zorder=4)
    ax.add_patch(crown)
    # Rod: thin line downward
    ax.plot([cx, cx], [cy - sz * 0.55, cy - sz * 0.85], color=ec, lw=lw * 1.2, zorder=4)


def _shape_cylinder(ax, cx, cy, sz, fill, ec, lw):
    import matplotlib.patches as mp
    # Outer wall left
    left = mp.FancyBboxPatch((cx - sz * 0.65, cy - sz * 0.9), sz * 0.15, sz * 1.8,
                              boxstyle="square,pad=0", facecolor=ec, edgecolor=ec, lw=lw, zorder=3)
    # Outer wall right
    right = mp.FancyBboxPatch((cx + sz * 0.50, cy - sz * 0.9), sz * 0.15, sz * 1.8,
                               boxstyle="square,pad=0", facecolor=ec, edgecolor=ec, lw=lw, zorder=3)
    # Inner bore
    bore = mp.FancyBboxPatch((cx - sz * 0.50, cy - sz * 0.9), sz, sz * 1.8,
                              boxstyle="square,pad=0", facecolor=fill, edgecolor="none", lw=0, zorder=2)
    ax.add_patch(bore)
    ax.add_patch(left)
    ax.add_patch(right)


def _shape_spark_plug(ax, cx, cy, sz, fill, ec, lw):
    import matplotlib.patches as mp
    import math
    # Hex body
    hex_pts_x = [cx + sz * 0.28 * math.cos(math.pi / 2 + i * math.pi / 3) for i in range(6)]
    hex_pts_y = [cy + sz * 0.28 * math.sin(math.pi / 2 + i * math.pi / 3) for i in range(6)]
    hex_pts_x.append(hex_pts_x[0])
    hex_pts_y.append(hex_pts_y[0])
    ax.fill(hex_pts_x, hex_pts_y, color=fill, zorder=3)
    ax.plot(hex_pts_x, hex_pts_y, color=ec, lw=lw, zorder=4)
    # Electrode tip
    ax.plot([cx, cx], [cy - sz * 0.28, cy - sz * 0.65], color=ec, lw=lw * 1.5, zorder=5)
    # Spark arc
    arc = mp.Arc((cx, cy - sz * 0.65), sz * 0.18, sz * 0.14,
                  angle=0, theta1=0, theta2=180, color="#facc15", lw=lw * 1.5, zorder=6)
    ax.add_patch(arc)


def _shape_valve(ax, cx, cy, sz, fill, ec, lw):
    import matplotlib.patches as mp
    # Stem: thin rectangle
    stem = mp.FancyBboxPatch((cx - sz * 0.07, cy - sz * 0.85), sz * 0.14, sz * 1.2,
                              boxstyle="square,pad=0", facecolor=fill, edgecolor=ec, lw=lw, zorder=3)
    ax.add_patch(stem)
    # Head: wide disc at bottom
    head = mp.Ellipse((cx, cy - sz * 0.85), sz * 0.6, sz * 0.18,
                       facecolor=ec, edgecolor=ec, lw=lw, zorder=4)
    ax.add_patch(head)


def _shape_crankshaft(ax, cx, cy, sz, fill, ec, lw):
    import matplotlib.patches as mp
    # Main journal
    journal = mp.Circle((cx, cy), sz * 0.14, facecolor=fill, edgecolor=ec, lw=lw, zorder=3)
    ax.add_patch(journal)
    # Crank pin offset
    pin = mp.Circle((cx + sz * 0.32, cy + sz * 0.25), sz * 0.10,
                     facecolor=ec, edgecolor=ec, lw=lw * 0.5, zorder=3)
    ax.add_patch(pin)
    # Crank arm
    ax.plot([cx, cx + sz * 0.32], [cy, cy + sz * 0.25], color=ec, lw=lw * 2.5, zorder=2)
    # Counter-weight
    cw_pts_x = [cx - sz * 0.32, cx - sz * 0.14, cx - sz * 0.14, cx - sz * 0.32]
    cw_pts_y = [cy - sz * 0.15, cy - sz * 0.15, cy + sz * 0.15, cy + sz * 0.15]
    ax.fill(cw_pts_x, cw_pts_y, color=fill, zorder=2)
    ax.plot(cw_pts_x + [cw_pts_x[0]], cw_pts_y + [cw_pts_y[0]], color=ec, lw=lw, zorder=3)


def _shape_camshaft(ax, cx, cy, sz, fill, ec, lw):
    import matplotlib.patches as mp
    # Shaft: horizontal bar
    ax.plot([cx - sz * 0.55, cx + sz * 0.55], [cy, cy], color=ec, lw=lw * 3, zorder=2)
    # Cams (lobes)
    for dx in [-0.28, 0, 0.28]:
        lobe = mp.Ellipse((cx + dx * sz, cy + sz * 0.16), sz * 0.18, sz * 0.28,
                           facecolor=fill, edgecolor=ec, lw=lw, zorder=3)
        ax.add_patch(lobe)


def _shape_fuel_injector(ax, cx, cy, sz, fill, ec, lw):
    import matplotlib.patches as mp
    # Body: tall narrow rect
    body = mp.FancyBboxPatch((cx - sz * 0.12, cy - sz * 0.2), sz * 0.24, sz * 0.7,
                              boxstyle="round,pad=0.01", facecolor=fill, edgecolor=ec, lw=lw, zorder=3)
    ax.add_patch(body)
    # Nozzle
    noz_x = [cx - sz * 0.08, cx + sz * 0.08, cx, cx - sz * 0.08]
    noz_y = [cy - sz * 0.20, cy - sz * 0.20, cy - sz * 0.50, cy - sz * 0.20]
    ax.fill(noz_x, noz_y, color=ec, zorder=4)
    # Spray dots
    for dy in [0.55, 0.65, 0.75]:
        ax.plot(cx, cy - sz * dy, 'o', color="#60a5fa", ms=sz * 50 * 0.03, zorder=5)


def _shape_default(ax, cx, cy, sz, fill, ec, lw):
    import matplotlib.patches as mp
    box = mp.FancyBboxPatch((cx - sz * 0.42, cy - sz * 0.32), sz * 0.84, sz * 0.64,
                             boxstyle="round,pad=0.04", facecolor=fill, edgecolor=ec, lw=lw, zorder=3)
    ax.add_patch(box)


def _pick_shape_fn(label: str):
    """Return shape-drawing function based on component label keyword."""
    l = label.lower()
    if any(k in l for k in ["piston", "plunger"]):
        return _shape_piston
    if any(k in l for k in ["cylinder", "bore", "chamber", "combustion"]):
        return _shape_cylinder
    if any(k in l for k in ["spark", "ignit"]):
        return _shape_spark_plug
    if any(k in l for k in ["valve", "intake", "exhaust"]):
        return _shape_valve
    if any(k in l for k in ["crank", "crankshaft"]):
        return _shape_crankshaft
    if any(k in l for k in ["cam", "camshaft"]):
        return _shape_camshaft
    if any(k in l for k in ["injector", "fuel", "carburet"]):
        return _shape_fuel_injector
    return _shape_default


def _render_scene_frame(
    scene: dict,
    scene_index: int,
    total_scenes: int,
    domain: str,
    w: int = 1280, h: int = 720,
) -> bytes:
    """Render a scene as PNG bytes using domain-specific component shapes."""
    import math
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    import matplotlib.patches as mpatches
    import io

    objects = scene.get("objects", [])
    active_id = scene.get("active_object", "")
    visited_ids = {obj["id"] for obj in objects if obj.get("role") == "visited"}

    n = len(objects)
    positions = _node_positions(n)
    obj_pos = {obj["id"]: positions[i] for i, obj in enumerate(objects)}

    fig = plt.figure(figsize=(w / 100, h / 100), dpi=100, facecolor=_BG)

    # ── Left panel — visual diagram (65%) ───────────────────────────────────
    ax_diag = fig.add_axes([0.01, 0.08, 0.63, 0.86], facecolor=_PANEL)
    ax_diag.set_xlim(0, 1)
    ax_diag.set_ylim(0, 1)
    ax_diag.axis("off")

    # Draw edges first (behind nodes)
    for obj in objects:
        if obj["id"] not in obj_pos:
            continue
        x0, y0 = obj_pos[obj["id"]]
        for target_id in obj.get("connects_to", []):
            if target_id in obj_pos:
                x1, y1 = obj_pos[target_id]
                is_active_edge = (obj["id"] == active_id or target_id == active_id)
                color = _ACTIVE if is_active_edge else _ARROW
                alpha = 0.95 if is_active_edge else 0.35
                ax_diag.annotate(
                    "", xy=(x1, y1), xytext=(x0, y0),
                    arrowprops=dict(
                        arrowstyle="-|>", color=color, lw=2.0, alpha=alpha,
                        connectionstyle="arc3,rad=0.10",
                        shrinkA=22, shrinkB=22,
                    ),
                )

    # Draw domain-specific shapes
    sz = 0.12 if n <= 6 else 0.09 if n <= 10 else 0.07
    for obj in objects:
        if obj["id"] not in obj_pos:
            continue
        cx, cy = obj_pos[obj["id"]]
        is_active = obj["id"] == active_id
        is_visited = obj["id"] in visited_ids

        if is_active:
            fill, ec, tc, lw = _ACTIVE, "#a5b4fc", _TEXT_HI, 2.5
        elif is_visited:
            fill, ec, tc, lw = _VISITED, "#6366f1", _TEXT_LO, 1.8
        else:
            fill, ec, tc, lw = _INACTIVE, _BORDER, _TEXT_LO, 1.0

        shape_fn = _pick_shape_fn(obj.get("label", ""))
        try:
            shape_fn(ax_diag, cx, cy, sz, fill, ec, lw)
        except Exception:
            _shape_default(ax_diag, cx, cy, sz, fill, ec, lw)

        # Glow ring on active node
        if is_active:
            glow = plt.Circle((cx, cy), sz * 0.75, color=_ACTIVE, alpha=0.15, zorder=1)
            ax_diag.add_patch(glow)
            # Pulse ring
            ring = plt.Circle((cx, cy), sz * 0.85, color="#a5b4fc", fill=False, lw=1.5, alpha=0.6, zorder=1)
            ax_diag.add_patch(ring)

        label = "\n".join(textwrap.wrap(obj.get("label", ""), width=11))
        label_y = cy - sz * 1.05
        ax_diag.text(cx, label_y, label, ha="center", va="top",
                     fontsize=max(6, 8 - n // 4), color=tc, fontweight="bold" if is_active else "normal",
                     multialignment="center", zorder=6,
                     bbox=dict(facecolor=_BG, edgecolor="none", alpha=0.7, pad=0.5))

    # Scene title overlay at top of diagram
    ax_diag.text(0.5, 0.99, scene.get("title", ""), ha="center", va="top",
                 fontsize=11, color=_ACCENT, fontweight="bold",
                 transform=ax_diag.transAxes,
                 bbox=dict(facecolor=_BG, edgecolor="none", alpha=0.6, pad=2))

    # ── Right panel — narration + active component callout (33%) ─────────────
    ax_exp = fig.add_axes([0.66, 0.08, 0.32, 0.86], facecolor=_PANEL)
    ax_exp.axis("off")

    # Active component name
    active_label = next((obj.get("label", "") for obj in objects if obj["id"] == active_id), "")
    if active_label:
        ax_exp.text(0.5, 0.95, active_label, ha="center", va="top",
                    fontsize=14, color=_TEXT_HI, fontweight="bold",
                    multialignment="center", transform=ax_exp.transAxes,
                    bbox=dict(boxstyle="round,pad=0.4", facecolor=_ACTIVE, alpha=0.85))

    ax_exp.plot([0.05, 0.95], [0.84, 0.84], color=_ACTIVE,
                linewidth=1.5, transform=ax_exp.transAxes, clip_on=False)

    # Narration text — main body
    narration = scene.get("narration", "")
    narration_wrapped = "\n".join(textwrap.wrap(narration, width=34))
    ax_exp.text(0.5, 0.80, narration_wrapped, ha="center", va="top",
                fontsize=9, color=_TEXT_EXP, linespacing=1.6,
                multialignment="center", transform=ax_exp.transAxes)

    # Component list — all components, highlight active
    ax_exp.plot([0.05, 0.95], [0.35, 0.35], color=_BORDER,
                linewidth=1.0, transform=ax_exp.transAxes, clip_on=False)
    ax_exp.text(0.5, 0.32, "Components", ha="center", va="top",
                fontsize=8, color=_TEXT_LO, transform=ax_exp.transAxes)
    for i, obj in enumerate(objects):
        is_act = obj["id"] == active_id
        color = _TEXT_HI if is_act else _TEXT_LO
        prefix = "▶ " if is_act else "  "
        y = 0.28 - i * 0.062
        if y < 0.02:
            break
        ax_exp.text(0.08, y, f"{prefix}{obj.get('label','')}", ha="left", va="top",
                    fontsize=8, color=color, fontweight="bold" if is_act else "normal",
                    transform=ax_exp.transAxes)

    # ── Bottom progress bar ───────────────────────────────────────────────────
    ax_prog = fig.add_axes([0.01, 0.01, 0.98, 0.05], facecolor=_BORDER)
    ax_prog.set_xlim(0, 1)
    ax_prog.set_ylim(0, 1)
    ax_prog.axis("off")
    progress = (scene_index + 1) / max(total_scenes, 1)
    bar = mpatches.FancyBboxPatch(
        (0.005, 0.1), progress * 0.99, 0.8,
        boxstyle="round,pad=0.01", facecolor=_ACTIVE, edgecolor="none"
    )
    ax_prog.add_patch(bar)
    ax_prog.text(0.5, 0.5,
                 f"Scene {scene_index + 1} of {total_scenes}  —  {scene.get('title', '')}",
                 ha="center", va="center", fontsize=8, color=_TEXT_LO)

    fig.canvas.draw()
    buf = io.BytesIO()
    fig.savefig(buf, format="png", dpi=100, facecolor=_BG)
    plt.close(fig)
    buf.seek(0)
    return buf.read()


def _render_title_card(title: str, domain: str, w: int = 1280, h: int = 720) -> bytes:
    """Render a title card PNG."""
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    import io

    domain_icon = DOMAIN_ICONS.get(domain, DOMAIN_ICONS["default"])
    fig = plt.figure(figsize=(w / 100, h / 100), dpi=100, facecolor=_BG)
    ax = fig.add_axes([0, 0, 1, 1], facecolor=_BG)
    ax.axis("off")
    ax.set_xlim(0, 1)
    ax.set_ylim(0, 1)

    try:
        ax.text(0.5, 0.65, domain_icon, ha="center", va="center",
                fontsize=60, transform=ax.transAxes)
    except Exception:
        pass
    title_wrapped = "\n".join(textwrap.wrap(title, width=30))
    ax.text(0.5, 0.45, title_wrapped, ha="center", va="center",
            fontsize=28, color=_TEXT_HI, fontweight="bold",
            multialignment="center", transform=ax.transAxes)
    ax.text(0.5, 0.25, "Educational Video", ha="center", va="center",
            fontsize=14, color=_ACCENT, transform=ax.transAxes)

    fig.canvas.draw()
    buf = io.BytesIO()
    fig.savefig(buf, format="png", dpi=100, facecolor=_BG)
    plt.close(fig)
    buf.seek(0)
    return buf.read()


def _render_summary_card(title: str, components: list[str], w: int = 1280, h: int = 720) -> bytes:
    """Render a summary card PNG."""
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    import io

    fig = plt.figure(figsize=(w / 100, h / 100), dpi=100, facecolor=_BG)
    ax = fig.add_axes([0, 0, 1, 1], facecolor=_BG)
    ax.axis("off")
    ax.set_xlim(0, 1)
    ax.set_ylim(0, 1)

    ax.text(0.5, 0.88, "Summary", ha="center", va="center",
            fontsize=26, color=_TEXT_HI, fontweight="bold", transform=ax.transAxes)
    ax.text(0.5, 0.78, f"Key components of {title}:", ha="center", va="center",
            fontsize=14, color=_ACCENT, transform=ax.transAxes)
    for j, comp in enumerate(components[:8]):
        ax.text(0.2, 0.68 - j * 0.08, f"checkmark  {comp}", ha="left", va="center",
                fontsize=11, color="#4ade80", transform=ax.transAxes)

    fig.canvas.draw()
    buf = io.BytesIO()
    fig.savefig(buf, format="png", dpi=100, facecolor=_BG)
    plt.close(fig)
    buf.seek(0)
    return buf.read()


# ── Stage 3 + 4: TTS per scene + FFmpeg assembly ────────────────────────────

async def _synthesize_scene_audio(narration: str, scene_wav_path: Path) -> bool:
    """Synthesize TTS for a scene narration. Returns True on success."""
    try:
        wav_data = await asyncio.wait_for(_wyoming_synthesize(narration), timeout=30)
        if wav_data:
            scene_wav_path.write_bytes(wav_data)
            return True
    except Exception as e:
        print(f"[tts] scene audio failed: {e}", flush=True)
    return False


async def _run_ffmpeg_docker(*args, timeout: int = 120) -> tuple[int, str]:
    """Run ffmpeg inside the ffmpeg Docker container, return (returncode, stderr_tail)."""
    proc = await asyncio.create_subprocess_exec(
        "docker", "exec", FFMPEG_CONTAINER,
        "ffmpeg", "-y", *args,
        stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.PIPE,
    )
    _, stderr = await asyncio.wait_for(proc.communicate(), timeout=timeout)
    return proc.returncode, stderr.decode()[-800:]


async def _docker_cp_to(local: Path, container_path: str, timeout: int = 30) -> None:
    """Copy a local file into the ffmpeg container."""
    proc = await asyncio.create_subprocess_exec(
        "docker", "cp", str(local), f"{FFMPEG_CONTAINER}:{container_path}",
        stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.PIPE,
    )
    _, err = await asyncio.wait_for(proc.communicate(), timeout=timeout)
    if proc.returncode != 0:
        raise RuntimeError(f"docker cp to container failed: {err.decode()[-200:]}")


async def _docker_cp_from(container_path: str, local: Path, timeout: int = 30) -> None:
    """Copy a file from the ffmpeg container to local."""
    proc = await asyncio.create_subprocess_exec(
        "docker", "cp", f"{FFMPEG_CONTAINER}:{container_path}", str(local),
        stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.PIPE,
    )
    _, err = await asyncio.wait_for(proc.communicate(), timeout=timeout)
    if proc.returncode != 0:
        raise RuntimeError(f"docker cp from container failed: {err.decode()[-200:]}")


async def _make_silent_audio_docker(duration_seconds: int, container_out: str) -> None:
    """Generate a silent WAV file inside the ffmpeg container."""
    rc, err = await _run_ffmpeg_docker(
        "-f", "lavfi", "-i", "anullsrc=r=22050:cl=mono",
        "-t", str(duration_seconds),
        "-c:a", "pcm_s16le",
        container_out,
        timeout=30
    )
    if rc != 0:
        print(f"[silent_audio] ffmpeg in container failed: {err[-200:]}", flush=True)


async def generate_video(contract: GenerationContract) -> tuple[str, str]:
    """
    Educational video generation pipeline:
    Stage 1: LLM storyboard via Ollama
    Stage 2: Scene PNG rendering via matplotlib
    Stage 3: Per-scene TTS via Wyoming Piper
    Stage 4: Assembly via ffmpeg inside Docker container
    """
    job_id = str(contract.jobId)
    output_filename = f"video_{job_id}.mp4"
    local_output_path = OUTPUT_DIR / output_filename
    work_dir = OUTPUT_DIR / f"frames_{job_id}"
    work_dir.mkdir(parents=True, exist_ok=True)

    # Container working directory for this job
    container_job_dir = f"{FFMPEG_CONTAINER_OUTPUT}/job_{job_id}"

    try:
        # ── Stage 1: Storyboard ──────────────────────────────────────────────
        print(f"[video:{job_id[:8]}] Stage 1: generating storyboard", flush=True)
        storyboard = await _generate_storyboard(contract)
        scenes = storyboard.get("scenes", [])
        domain = storyboard.get("domain", contract.domain)
        title = storyboard.get("title", contract.concept)
        print(f"[video:{job_id[:8]}] Storyboard: {len(scenes)} scenes", flush=True)

        # Create job dir in container
        proc = await asyncio.create_subprocess_exec(
            "docker", "exec", FFMPEG_CONTAINER, "mkdir", "-p", container_job_dir,
            stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.PIPE,
        )
        await asyncio.wait_for(proc.communicate(), timeout=10)

        loop = asyncio.get_event_loop()
        scene_clips_container: list[str] = []  # container paths of assembled clips

        # ── Title card ────────────────────────────────────────────────────────
        title_png = work_dir / "title_card.png"
        title_wav_container = f"{container_job_dir}/title_silence.wav"
        title_clip_container = f"{container_job_dir}/clip_title.mp4"
        title_png_container = f"{container_job_dir}/title_card.png"

        def render_title():
            return _render_title_card(title, domain)
        title_png_data = await loop.run_in_executor(None, render_title)
        title_png.write_bytes(title_png_data)

        await _docker_cp_to(title_png, title_png_container)
        await _make_silent_audio_docker(3, title_wav_container)
        rc, err = await _run_ffmpeg_docker(
            "-loop", "1", "-i", title_png_container,
            "-i", title_wav_container,
            "-c:v", "libx264", "-preset", "fast", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-t", "3", "-shortest",
            title_clip_container, timeout=60
        )
        if rc == 0:
            scene_clips_container.append(title_clip_container)
        else:
            print(f"[video] title clip failed: {err[-200:]}", flush=True)

        # ── Scene clips ───────────────────────────────────────────────────────
        for s_idx, scene in enumerate(scenes):
            duration = scene.get("duration_seconds", 6)
            scene_png_local = work_dir / f"scene_{s_idx:02d}.png"
            scene_wav_local = work_dir / f"scene_{s_idx:02d}.wav"
            scene_png_container = f"{container_job_dir}/scene_{s_idx:02d}.png"
            scene_wav_container = f"{container_job_dir}/scene_{s_idx:02d}.wav"
            scene_clip_container = f"{container_job_dir}/clip_{s_idx:02d}.mp4"

            # Render PNG
            try:
                def render_s(sc=scene, si=s_idx, ts=len(scenes)):
                    return _render_scene_frame(sc, si, ts, domain)
                png_data = await loop.run_in_executor(None, render_s)
                scene_png_local.write_bytes(png_data)
                await _docker_cp_to(scene_png_local, scene_png_container)
            except Exception as e:
                print(f"[video] scene {s_idx} render failed: {e}", flush=True)
                continue

            # TTS narration
            narration = scene.get("narration", "")
            audio_ok = False
            if narration:
                audio_ok = await _synthesize_scene_audio(narration, scene_wav_local)
            if audio_ok:
                await _docker_cp_to(scene_wav_local, scene_wav_container)
            else:
                await _make_silent_audio_docker(duration, scene_wav_container)

            # Build scene clip
            rc, err = await _run_ffmpeg_docker(
                "-loop", "1", "-i", scene_png_container,
                "-i", scene_wav_container,
                "-c:v", "libx264", "-preset", "fast", "-pix_fmt", "yuv420p",
                "-c:a", "aac", "-t", str(duration), "-shortest",
                scene_clip_container, timeout=60
            )
            if rc == 0:
                scene_clips_container.append(scene_clip_container)
                print(f"[video:{job_id[:8]}] Scene {s_idx+1}/{len(scenes)} ok, audio={'tts' if audio_ok else 'silent'}", flush=True)
            else:
                print(f"[video] scene {s_idx} clip failed: {err[-200:]}", flush=True)

        # ── Summary card ──────────────────────────────────────────────────────
        summary_png_local = work_dir / "summary_card.png"
        summary_png_container = f"{container_job_dir}/summary_card.png"
        summary_wav_container = f"{container_job_dir}/summary_silence.wav"
        summary_clip_container = f"{container_job_dir}/clip_summary.mp4"

        def render_summary():
            return _render_summary_card(title, contract.components)
        summary_png_data = await loop.run_in_executor(None, render_summary)
        summary_png_local.write_bytes(summary_png_data)
        await _docker_cp_to(summary_png_local, summary_png_container)
        await _make_silent_audio_docker(3, summary_wav_container)
        rc, err = await _run_ffmpeg_docker(
            "-loop", "1", "-i", summary_png_container,
            "-i", summary_wav_container,
            "-c:v", "libx264", "-preset", "fast", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-t", "3", "-shortest",
            summary_clip_container, timeout=60
        )
        if rc == 0:
            scene_clips_container.append(summary_clip_container)

        if not scene_clips_container:
            raise RuntimeError("No scene clips were produced")

        # ── Stage 4: Concatenate all clips ────────────────────────────────────
        print(f"[video:{job_id[:8]}] Stage 4: concatenating {len(scene_clips_container)} clips", flush=True)
        concat_txt_local = work_dir / "concat.txt"
        concat_txt_container = f"{container_job_dir}/concat.txt"
        with open(concat_txt_local, "w") as fh:
            for clip in scene_clips_container:
                fh.write(f"file '{clip}'\n")

        await _docker_cp_to(concat_txt_local, concat_txt_container)

        container_output = f"{FFMPEG_CONTAINER_OUTPUT}/{output_filename}"
        rc, err = await _run_ffmpeg_docker(
            "-f", "concat", "-safe", "0",
            "-i", concat_txt_container,
            "-c", "copy",
            container_output,
            timeout=300
        )
        if rc != 0:
            raise RuntimeError(f"ffmpeg concat failed: {err[-500:]}")

        # Copy MP4 back from container
        await _docker_cp_from(container_output, local_output_path)

        size_kb = local_output_path.stat().st_size // 1024
        print(f"[video:{job_id[:8]}] Done: {local_output_path} ({size_kb} KB)", flush=True)

        # Cleanup container job dir
        await asyncio.create_subprocess_exec(
            "docker", "exec", FFMPEG_CONTAINER, "rm", "-rf", container_job_dir,
            stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.PIPE,
        )

        return f"/output/{output_filename}", str(local_output_path)

    finally:
        shutil.rmtree(work_dir, ignore_errors=True)


def _esc(text: str) -> str:
    return text.replace("'", "\\'").replace(":", "\\:").replace("\\", "\\\\")


# ── Narration (Piper Wyoming protocol) ───────────────────────────────────────

async def generate_narration(contract: GenerationContract) -> tuple[str, str]:
    """
    Sends text to wyoming-piper over TCP Wyoming protocol, receives WAV audio,
    converts to MP3 with ffmpeg. Returns (output_url, output_path).
    """
    narration_text = (
        f"Welcome to the lesson on {contract.concept}. "
        f"This is a {contract.settings.difficulty} level explanation. "
        + " ".join(
            f"Component {i+1}: {comp}." for i, comp in enumerate(contract.components)
        )
    )

    wav_data = await _wyoming_synthesize(narration_text)

    output_filename = f"narration_{contract.jobId}.mp3"
    output_path = OUTPUT_DIR / output_filename

    proc = await asyncio.create_subprocess_exec(
        "ffmpeg", "-y",
        "-f", "wav", "-i", "pipe:0",
        "-codec:a", "libmp3lame", "-q:a", "4",
        str(output_path),
        stdin=asyncio.subprocess.PIPE,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
    )
    _, stderr = await asyncio.wait_for(
        proc.communicate(input=wav_data), timeout=60
    )

    if proc.returncode != 0:
        raise RuntimeError(f"ffmpeg WAV->MP3 failed: {stderr.decode()[-300:]}")

    return f"/output/{output_filename}", str(output_path)


async def _wyoming_synthesize(text: str) -> bytes:
    """
    Wyoming protocol (v1): send Synthesize event, receive AudioStart + Audio chunks + AudioStop.
    """
    loop = asyncio.get_event_loop()

    def _sync_wyoming(text: str) -> bytes:
        import json

        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(30)
        sock.connect((PIPER_HOST, PIPER_PORT))

        synthesize_event = {
            "type": "synthesize",
            "data": {
                "text": text,
                "voice": {"name": "en_US-lessac-medium", "language": "en_US"},
            },
            "data_length": 0,
        }
        event_json = _json.dumps(synthesize_event) + "\n"
        sock.sendall(event_json.encode())

        wav_chunks: list[bytes] = []
        buf = b""
        while True:
            chunk = sock.recv(65536)
            if not chunk:
                break
            buf += chunk

            while b"\n" in buf:
                line, buf = buf.split(b"\n", 1)
                try:
                    event = _json.loads(line.decode())
                except Exception:
                    continue

                data_length = event.get("data_length", 0)
                payload = b""
                while len(payload) < data_length:
                    needed = data_length - len(payload)
                    part = sock.recv(min(needed, 65536))
                    if not part:
                        break
                    payload += part
                buf = buf[len(buf):]

                if event["type"] == "audio":
                    wav_chunks.append(payload)
                elif event["type"] == "audio-stop":
                    sock.close()
                    return b"".join(wav_chunks)
                elif event["type"] == "error":
                    raise RuntimeError(f"Piper error: {event.get('data', {}).get('text', 'unknown')}")

        sock.close()
        return b"".join(wav_chunks)

    return await loop.run_in_executor(None, _sync_wyoming, text)


# ── 3D generation (Blender via docker exec) ──────────────────────────────────

async def generate_3d(contract: GenerationContract) -> tuple[str, str]:
    """
    Writes a Blender Python script, copies it into the running Blender container,
    runs it headlessly via docker exec, then copies the GLB back to OUTPUT_DIR.
    """
    output_filename = f"scene_{contract.jobId}.glb"
    local_output_path = OUTPUT_DIR / output_filename
    container_glb_path = f"{BLENDER_CONTAINER_OUTPUT}/{output_filename}"
    container_script_path = f"/tmp/blender_script_{contract.jobId}.py"

    script_content = _build_blender_script(contract, container_glb_path)

    with tempfile.NamedTemporaryFile(mode="w", suffix=".py", delete=False, encoding="utf-8") as f:
        f.write(script_content)
        local_script_path = f.name

    try:
        cp_in = await asyncio.create_subprocess_exec(
            "docker", "cp", local_script_path, f"{BLENDER_CONTAINER}:{container_script_path}",
            stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.PIPE,
        )
        _, cp_err = await asyncio.wait_for(cp_in.communicate(), timeout=15)
        if cp_in.returncode != 0:
            raise RuntimeError(f"docker cp script failed: {cp_err.decode()[-300:]}")

        exec_proc = await asyncio.create_subprocess_exec(
            "docker", "exec", BLENDER_CONTAINER,
            "blender", "--background", "--python", container_script_path,
            stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.PIPE,
        )
        stdout, stderr = await asyncio.wait_for(exec_proc.communicate(), timeout=120)
        if exec_proc.returncode != 0:
            raise RuntimeError(f"Blender exec failed: {stderr.decode()[-500:]}")

        cp_out = await asyncio.create_subprocess_exec(
            "docker", "cp", f"{BLENDER_CONTAINER}:{container_glb_path}", str(local_output_path),
            stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.PIPE,
        )
        _, cp_err2 = await asyncio.wait_for(cp_out.communicate(), timeout=30)
        if cp_out.returncode != 0:
            raise RuntimeError(f"docker cp GLB failed: {cp_err2.decode()[-300:]}")

        if not local_output_path.exists() or local_output_path.stat().st_size < 1024:
            raise RuntimeError("Blender produced no output or file is too small")

        await asyncio.create_subprocess_exec(
            "docker", "exec", BLENDER_CONTAINER, "rm", "-f", container_script_path
        )

        return f"/output/{output_filename}", str(local_output_path)
    finally:
        Path(local_script_path).unlink(missing_ok=True)


def _build_blender_script(contract: GenerationContract, output_path: str) -> str:
    components = contract.components

    cols = max(1, int(len(components) ** 0.5))
    component_lines = []
    for i, comp in enumerate(components):
        x = (i % cols) * 3.0 - (cols - 1) * 1.5
        y = (i // cols) * 3.0
        label = comp.replace('"', '\\"').replace("'", "\\'")
        r = round(0.2 + (i * 0.15) % 0.8, 2)
        g = round(0.4 + (i * 0.1) % 0.5, 2)
        b = round(0.6 + (i * 0.05) % 0.4, 2)
        component_lines.append(f'''\
bpy.ops.mesh.primitive_cube_add(size=1.5, location=({x}, {y}, 0))
cube = bpy.context.active_object
cube.name = "{label}"
mat = bpy.data.materials.new(name="mat_{i}")
mat.use_nodes = True
bsdf = mat.node_tree.nodes.get("Principled BSDF")
if bsdf:
    bsdf.inputs["Base Color"].default_value = ({r}, {g}, {b}, 1)
cube.data.materials.append(mat)''')

    component_code = "\n".join(component_lines)

    return f'''\
import bpy

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete()

{component_code}

bpy.ops.object.camera_add(location=(0, -10, 5))
cam = bpy.context.active_object
cam.rotation_euler = (1.1, 0, 0)
bpy.context.scene.camera = cam

bpy.ops.object.light_add(type="SUN", location=(5, -5, 10))

bpy.ops.export_scene.gltf(
    filepath="{output_path}",
    export_format="GLB",
    export_selected=False,
)
print("Blender export complete: {output_path}")
'''


# ── Routes ───────────────────────────────────────────────────────────────────

@app.get("/health")
async def health():
    return {"status": "healthy", "service": "python-ai-service"}


@app.post("/generate", response_model=GenerationResult)
async def generate(contract: GenerationContract):
    start = time.time()

    try:
        if contract.visualizationType in ("diagram", "flowchart"):
            mermaid = await generate_mermaid(contract)
            if not mermaid or validate_mermaid(mermaid, contract.components):
                mermaid = build_safe_fallback_diagram(contract.components, contract.concept)
            duration = time.time() - start
            return GenerationResult(
                jobId=contract.jobId, status="Completed",
                outputType="mermaid", outputContent=mermaid,
                metadata=GenerationMetadata(componentsCovered=contract.components, generationDurationSeconds=round(duration, 2)),
            )

        if contract.visualizationType == "video":
            url, _ = await generate_video(contract)
            duration = time.time() - start
            return GenerationResult(
                jobId=contract.jobId, status="Completed",
                outputType="video", outputUrl=url,
                metadata=GenerationMetadata(componentsCovered=contract.components, generationDurationSeconds=round(duration, 2)),
            )

        if contract.visualizationType == "narration":
            url, _ = await generate_narration(contract)
            duration = time.time() - start
            return GenerationResult(
                jobId=contract.jobId, status="Completed",
                outputType="audio", outputUrl=url,
                metadata=GenerationMetadata(componentsCovered=contract.components, generationDurationSeconds=round(duration, 2)),
            )

        if contract.visualizationType in ("3d", "3d_animation"):
            url, _ = await generate_3d(contract)
            duration = time.time() - start
            return GenerationResult(
                jobId=contract.jobId, status="Completed",
                outputType="glb", outputUrl=url,
                metadata=GenerationMetadata(componentsCovered=contract.components, generationDurationSeconds=round(duration, 2)),
            )

        if contract.visualizationType in ("auto", "interactive", "static_diagram", "2d_animation"):
            mermaid = await generate_mermaid(contract)
            if not mermaid or validate_mermaid(mermaid, contract.components):
                mermaid = build_safe_fallback_diagram(contract.components, contract.concept)
            duration = time.time() - start
            return GenerationResult(
                jobId=contract.jobId, status="Completed",
                outputType="mermaid", outputContent=mermaid,
                metadata=GenerationMetadata(componentsCovered=contract.components, generationDurationSeconds=round(duration, 2)),
            )

        return GenerationResult(jobId=contract.jobId, status="Failed", errorCode="UNSUPPORTED_TYPE", retryable=False)

    except Exception as exc:
        import traceback
        print(f"[generate] error for job {contract.jobId}: {exc}", flush=True)
        traceback.print_exc()
        return GenerationResult(jobId=contract.jobId, status="Failed", errorCode="PYTHON_EXCEPTION", retryable=True)
