import time
import re
import httpx
from uuid import UUID
from fastapi import FastAPI
from pydantic import BaseModel
from typing import Optional

app = FastAPI(title="AI Visual Learning - Python AI Service")

OLLAMA_URL = "http://localhost:11434"
OLLAMA_MODEL = "gemma3:4b"


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
    """Convert a component label to a valid Mermaid node ID (no spaces, alphanumeric+underscore)."""
    return re.sub(r"[^a-zA-Z0-9]", "_", label).strip("_")


def build_nodes_block(components: list[str]) -> str:
    """
    Pre-build node definitions with quoted labels so the LLM just needs to wire them.
    e.g.  SwappingElements["Swapping Elements"]
    """
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
    """
    Fix unquoted multi-word node labels like:
        A --> Swapping Elements
        Swapping Elements --> B
        A -- label --> Swapping Elements
    Mermaid requires spaces in node IDs/labels to be quoted: ["Swapping Elements"]
    Strategy: replace bare multi-word identifiers on edge lines with single-word IDs.
    """
    lines = mermaid.splitlines()
    fixed = []
    for line in lines:
        # Skip comment and directive lines
        stripped = line.strip()
        if not stripped or stripped.startswith("%%") or stripped.startswith("graph") or stripped.startswith("flowchart"):
            fixed.append(line)
            continue

        # Fix node definitions with unquoted labels: NodeId[unquoted label]  or  NodeId(unquoted)
        # Already quoted ones are left alone
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

        # Fix bare multi-word node references on edge lines (A --> Some Thing --> B)
        # Replace unquoted multi-word tokens that appear as node references
        # Pattern: word boundary, two or more words not in quotes, followed by edge or EOL
        # We do this by collapsing runs of Title Case words into CamelCase IDs
        # Only touch sequences outside of quoted strings and edge labels ("...")
        line = fix_bare_node_refs(line)

        fixed.append(line)
    return "\n".join(fixed)


def fix_bare_node_refs(line: str) -> str:
    """
    On an edge line like:
        Swapping Elements --> Iteration Through Array
    convert bare multi-word node refs to single-word IDs:
        SwappingElements --> IterationThroughArray
    Leave edge labels (-- "text" -->) untouched.
    """
    # Tokenise: split on Mermaid edge operators, keeping them
    edge_pattern = re.compile(r'(--+>|==+>|--+\|[^|]*\|-->|--[^-].*?-->|\.\.\.|:::|\|)')
    parts = edge_pattern.split(line)
    result = []
    for part in parts:
        if edge_pattern.match(part):
            result.append(part)
        else:
            # This part is a node reference possibly with label
            # If it already has ["..."] or ("...") it's fine
            if re.search(r'[\[(]["\']', part):
                result.append(part)
            else:
                # Collapse bare multi-word sequences into CamelCase
                part = re.sub(
                    r'\b([A-Z][a-z]+(?:\s+[A-Z][a-z]+)+)\b',
                    lambda m: re.sub(r'\s+', '', m.group(0)),
                    part
                )
                result.append(part)
    return "".join(result)


def sanitize_mermaid(raw: str) -> str:
    raw = strip_fences(raw)
    raw = find_diagram_start(raw)
    raw = quote_bare_node_labels(raw)
    return raw.strip()


# ── Mermaid generation ───────────────────────────────────────────────────────

async def generate_mermaid(contract: GenerationContract) -> str:
    viz = contract.visualizationType
    difficulty = contract.settings.difficulty

    diagram_type = "flowchart TD" if viz == "flowchart" else "graph TD"

    # Build pre-defined node IDs to force the LLM to use valid identifiers
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
            error = validate_mermaid(mermaid, contract.components)
            if error:
                return GenerationResult(
                    jobId=contract.jobId,
                    status="Failed",
                    errorCode=error,
                    retryable=True,
                )

            duration = time.time() - start
            return GenerationResult(
                jobId=contract.jobId,
                status="Completed",
                outputType="mermaid",
                outputContent=mermaid,
                metadata=GenerationMetadata(
                    componentsCovered=contract.components,
                    generationDurationSeconds=round(duration, 2),
                ),
            )

        return GenerationResult(
            jobId=contract.jobId,
            status="Failed",
            errorCode="UNSUPPORTED_TYPE",
            retryable=False,
        )

    except Exception as exc:
        return GenerationResult(
            jobId=contract.jobId,
            status="Failed",
            errorCode="PYTHON_EXCEPTION",
            retryable=True,
        )
