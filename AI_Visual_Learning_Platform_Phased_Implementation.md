# AI Visual Learning Assistant & Interactive Visualization Platform
## Phased Implementation Plan

---

> **How to use this document**
> Each phase builds on the previous one. Complete and test every checklist item before moving to
> the next phase. Phases 1–2 deliver a working product. Phases 3–5 add quality, scale, and
> production hardening. Never skip a phase checklist — each gate prevents regressions in later
> phases.

---

# Phase Overview

| Phase | Name | Goal | Deliverable |
|---|---|---|---|
| 1 | Core Foundation | Working 10-step workflow with text output | Usable MVP |
| 2 | Visualization & Generation | Diagrams, async jobs, Python service | Visual output |
| 3 | Intelligence & RAG | Knowledge retrieval, multi-model AI | Grounded responses |
| 4 | Resilience & Recovery | Retry, fallback, failure handling | Production-safe |
| 5 | Security, Observability & Scale | Auth, tracing, asset library, advanced features | Production-ready |

---

---

# Phase 1 — Core Foundation

## Goal

A working end-to-end learning workflow that guides users through all 10 steps and produces a
text-based educational explanation. No 3D, no video, no async jobs yet. Prove the workflow
engine works before adding complexity.

---

## Scope

### What is included

- Project scaffolding (React frontend + ASP.NET Core backend + PostgreSQL)
- Docker Compose development environment
- Basic chat UI (no auth — hardcoded dev user)
- Learning Workflow Engine (state machine, core happy path only)
- Intent Analysis Service (rule-based or simple LLM call via Ollama)
- Domain Classification Service (rule-based or simple LLM call)
- Concept Breakdown Service (LLM-generated component lists)
- Educational Explanation Service (LLM-generated explanations)
- Component Selection Service (chat-based selection, no UI widgets yet)
- Visualization Planning Service (generates plan as JSON, displayed as text)
- Preview Service (text display of plan)
- Approval step (user types "yes" / "no")
- Text Explanation output (final output is a structured text explanation)
- Basic session persistence in PostgreSQL (session ID, current state, timestamps)
- LearningSessionEvents table (record every state transition)
- Ollama integration (local LLM: Qwen3 or Gemma)

### What is NOT included in Phase 1

- Authentication / JWT
- Diagram or 3D generation
- Async job queue
- Python AI Service
- RAG / Qdrant
- Retry or fallback logic
- SSE progress streaming
- Narration
- Asset library
- Observability tooling

---

## Architecture (Phase 1)

```
React Frontend (Chat UI only)
        ↓ REST
ASP.NET Core API
        ↓
Learning Workflow Engine
        ↓
Intent → Domain → Explanation → Selection → Plan → Approval → Text Output
        ↓
PostgreSQL (Sessions + Events)
        ↓
Ollama (local LLM)
```

---

## Database Schema (Phase 1)

### LearningSessions

```sql
CREATE TABLE learning_sessions (
  session_id    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id       UUID NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001', -- hardcoded dev user
  current_state VARCHAR(50) NOT NULL DEFAULT 'Created',
  topic         TEXT,
  created_date  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_date  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
```

### LearningSessionEvents

```sql
CREATE TABLE learning_session_events (
  event_id       UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  session_id     UUID NOT NULL REFERENCES learning_sessions(session_id),
  previous_state VARCHAR(50),
  new_state      VARCHAR(50) NOT NULL,
  trigger        VARCHAR(100),
  event_payload  JSONB,
  created_at     TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
```

---

## Workflow States (Phase 1)

Active states in this phase:

```
Created → IntentAnalyzed → DomainClassified → ConceptExplained
→ ComponentSelectionPending → VisualizationPlanned → ApprovalPending
→ Completed
```

All other states (Failed, Retrying, FallbackGeneration, Paused, etc.) are deferred to Phase 4.

---

## Workflow State Machine (Phase 1 — Happy Path Only)

| From State | Trigger | Guard | To State |
|---|---|---|---|
| Created | User submits message | Message non-empty | IntentAnalyzed |
| IntentAnalyzed | Intent classified | Intent recognized | DomainClassified |
| DomainClassified | Domain identified | Domain recognized | ConceptExplained |
| ConceptExplained | User ready to select | — | ComponentSelectionPending |
| ComponentSelectionPending | Selections submitted | At least one component selected | VisualizationPlanned |
| VisualizationPlanned | Plan displayed | — | ApprovalPending |
| ApprovalPending | User approves | Explicit "yes" received | Completed |
| ApprovalPending | User rejects | Explicit "no" or "change" | ComponentSelectionPending |

---

## API Endpoints (Phase 1)

| Method | Endpoint | Description |
|---|---|---|
| POST | /api/sessions | Create new learning session |
| GET | /api/sessions/{id} | Get session state |
| POST | /api/sessions/{id}/messages | Send user message, advance workflow |
| GET | /api/sessions/{id}/events | Get session event history |

---

## Configuration (Phase 1)

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=localhost;Database=visuallearning;Username=dev;Password=dev"
  },
  "AI": {
    "ModelProvider": "ollama",
    "OllamaBaseUrl": "http://localhost:11434",
    "OllamaModel": "qwen3"
  },
  "WorkflowEngine": {
    "IntentConfidenceThreshold": 0.75,
    "DomainConfidenceThreshold": 0.70
  }
}
```

---

## Docker Compose (Phase 1)

```yaml
services:
  frontend:
    build: ./frontend
    ports: ["3000:3000"]

  api:
    build: ./api
    ports: ["5000:5000"]
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
    depends_on: [postgres]

  postgres:
    image: postgres:16
    environment:
      POSTGRES_DB: visuallearning
      POSTGRES_USER: dev
      POSTGRES_PASSWORD: dev
    ports: ["5432:5432"]
    volumes: ["pgdata:/var/lib/postgresql/data"]

  ollama:
    image: ollama/ollama
    ports: ["11434:11434"]
    volumes: ["ollama_data:/root/.ollama"]

volumes:
  pgdata:
  ollama_data:
```

---

## Phase 1 Test Checklist

### Environment

- [ ] `docker compose up` starts all containers without errors
- [ ] PostgreSQL is accessible on port 5432
- [ ] Ollama is accessible on port 11434
- [ ] Ollama has at least one model pulled (`ollama pull qwen3`)
- [ ] Frontend loads at http://localhost:3000
- [ ] API health endpoint responds: `GET /health → 200 OK`

### Session Creation

- [ ] `POST /api/sessions` returns a session object with `sessionId` and `currentState: "Created"`
- [ ] Session record exists in PostgreSQL `learning_sessions` table
- [ ] A `Created` event exists in `learning_session_events` table

### Intent Analysis

- [ ] Sending "Generate a running car engine" advances state to `IntentAnalyzed`
- [ ] Sending "Explain bubble sort" advances state to `IntentAnalyzed`
- [ ] Sending "Show me how TCP works" advances state to `IntentAnalyzed`
- [ ] Intent is recorded in the session event payload
- [ ] Empty message is rejected (does not advance state)

### Domain Classification

- [ ] "Car engine" is classified as `mechanical_engineering`
- [ ] "Bubble sort" is classified as `computer_science` / `algorithms`
- [ ] "TCP handshake" is classified as `computer_science` / `networking`
- [ ] Domain is recorded in session event payload
- [ ] State advances to `DomainClassified`

### Concept Explanation

- [ ] System produces a concept explanation for car engine
- [ ] Explanation includes a list of major components (minimum 5)
- [ ] Explanation is appropriate for a general audience
- [ ] State advances to `ConceptExplained`
- [ ] Event recorded with explanation summary in payload

### Component Selection

- [ ] System asks user which components to include
- [ ] System asks for difficulty level (Beginner / Intermediate / Expert)
- [ ] System asks for visualization type preference
- [ ] User can select components by typing them
- [ ] State advances to `ComponentSelectionPending`
- [ ] Submitting zero components is rejected with a prompt to select at least one
- [ ] Submitting valid selections advances to `VisualizationPlanned`

### Visualization Plan

- [ ] System generates a visualization plan as structured JSON
- [ ] Plan includes: `visualizationType`, `components`, `difficulty`
- [ ] Plan is displayed to the user in a readable format
- [ ] State advances to `VisualizationPlanned`
- [ ] Event payload contains the full plan

### Approval

- [ ] System asks "Generate visualization?" clearly
- [ ] Typing "yes" advances to `Completed`
- [ ] Typing "no" or "change" returns to `ComponentSelectionPending`
- [ ] On return to `ComponentSelectionPending`, prior selections are pre-populated
- [ ] State is `ApprovalPending` while awaiting response

### Text Output (Phase 1 Generation)

- [ ] After approval, system generates a structured text explanation
- [ ] Text explanation covers all selected components
- [ ] Text explanation matches the requested difficulty level
- [ ] Minimum length: 200 characters
- [ ] State advances to `Completed`
- [ ] Completed event recorded

### Session Events

- [ ] Every state transition creates a record in `learning_session_events`
- [ ] Events are in chronological order
- [ ] `GET /api/sessions/{id}/events` returns complete transition history
- [ ] Each event contains `previousState`, `newState`, `trigger`, `createdAt`

### Workflow Integrity

- [ ] Sending a message from `Completed` does not change state
- [ ] Skipping component selection and sending approval directly is rejected
- [ ] Invalid state transitions are logged and return a 400 error
- [ ] Refreshing the browser and continuing from the same session ID works

---

---

# Phase 2 — Visualization & Generation

## Goal

Replace text-only output with real visual output. Add the diagram engine (Mermaid / React Flow),
async job processing, and the Python AI Service for image and basic animation generation.
Users should receive an actual visual after clicking "Generate."

---

## Scope

### What is added in Phase 2

- Async job architecture (in-process background worker — no RabbitMQ yet)
- Generation job table in PostgreSQL
- SSE progress streaming endpoint
- Python AI Service (FastAPI) — image generation and basic diagram generation
- Diagram Engine on frontend (Mermaid + React Flow)
- Visualization Generation Service (dispatches to Python or generates diagrams in-process)
- Output validation (diagram and text profiles)
- Fallback: if diagram generation fails, fall back to text explanation
- Job progress visible in chat UI
- Fast-track mode ("skip explanation, just generate")
- Refinement loop (user can modify selections after seeing output)

### What is NOT included in Phase 2

- RabbitMQ (background worker is in-process)
- 3D / Blender / video generation
- RAG / Qdrant (still LLM-only knowledge)
- Authentication
- Full retry/fallback hierarchy (only one fallback level: diagram → text)
- Narration
- Asset library
- Observability tooling

---

## New Database Tables (Phase 2)

### GenerationJobs

```sql
CREATE TABLE generation_jobs (
  job_id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  session_id      UUID NOT NULL REFERENCES learning_sessions(session_id),
  status          VARCHAR(50) NOT NULL DEFAULT 'Queued',
  visualization_type VARCHAR(50),
  fallback_attempt INT NOT NULL DEFAULT 0,
  output_type     VARCHAR(50),
  output_url      TEXT,
  thumbnail_url   TEXT,
  error_code      VARCHAR(100),
  progress        INT NOT NULL DEFAULT 0,
  created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
```

---

## New Workflow States (Phase 2)

Added to the state machine:

```
ApprovalPending → GenerationQueued → Generating → Generated → Completed
```

Minimal failure path:

```
Generating → Failed → FallbackGeneration → Generated
```

---

## New API Endpoints (Phase 2)

| Method | Endpoint | Description |
|---|---|---|
| POST | /api/sessions/{id}/approve | Approve plan, create job, return jobId |
| GET | /api/jobs/{id}/progress | SSE stream: progress updates |
| GET | /api/jobs/{id}/result | Get completed job result |
| POST | /api/sessions/{id}/reject | Reject plan, return to selection |
| POST | /api/sessions/{id}/refine | Trigger refinement (return to selection) |

---

## Generation Contract (Phase 2)

```json
{
  "jobId": "uuid",
  "sessionId": "uuid",
  "visualizationType": "diagram",
  "concept": "bubble_sort",
  "domain": "algorithms",
  "components": ["Array", "Comparisons", "Swaps", "Passes"],
  "settings": {
    "narration": false,
    "labels": true,
    "difficulty": "beginner",
    "detailLevel": "educational",
    "renderingStyle": "schematic"
  },
  "fallbackAttempt": 0
}
```

---

## Python AI Service (Phase 2)

Framework: FastAPI

Phase 2 responsibilities:
- Diagram generation (Mermaid syntax generation via LLM)
- Basic image generation (concept illustrations)

```
POST /generate
Body: GenerationContract
Response: GenerationResult
```

```json
{
  "jobId": "uuid",
  "status": "Completed",
  "outputType": "diagram",
  "outputUrl": "generated/session-456/diagram.svg",
  "fallbackAttempt": 0,
  "metadata": {
    "componentsCovered": ["Array", "Comparisons", "Swaps"],
    "generationDurationSeconds": 4
  }
}
```

---

## Routing Decision (Phase 2)

| Domain | Visualization Type | Engine |
|---|---|---|
| Any | diagram | Diagram Engine (Mermaid / React Flow) |
| Computer Science | flowchart | Diagram Engine |
| Any | text_explanation | Text (in-process) |

3D and video routing deferred to Phase 3.

---

## Phase 2 Test Checklist

### Async Job Architecture

- [ ] Approving a plan returns `{ jobId, status: "Queued" }` immediately (not after generation)
- [ ] Job record exists in `generation_jobs` table with status `Queued`
- [ ] Background worker picks up the job and transitions status to `Processing`
- [ ] Session state transitions: `ApprovalPending → GenerationQueued → Generating`
- [ ] Each state transition creates an event in `learning_session_events`

### SSE Progress Streaming

- [ ] `GET /api/jobs/{id}/progress` returns an SSE stream (Content-Type: text/event-stream)
- [ ] Progress events are received while job is processing: `{ jobId, status, progress }`
- [ ] Progress reaches 100 when job completes
- [ ] SSE stream closes automatically when job reaches `Completed` or `Failed`
- [ ] Frontend displays a progress indicator while generation runs
- [ ] Refreshing the browser and re-subscribing to the SSE stream resumes tracking

### Diagram Generation

- [ ] Requesting a diagram for "bubble sort" produces a valid Mermaid or React Flow diagram
- [ ] Requesting a diagram for "TCP handshake" produces a valid flowchart
- [ ] Requesting a diagram for "car engine components" produces a component diagram
- [ ] Diagram renders correctly in the frontend Diagram Viewer
- [ ] All selected components appear as nodes in the diagram
- [ ] Diagram output is saved and accessible via `outputUrl`

### Output Validation

- [ ] Diagram with missing required nodes is rejected (does not enter `Generated` state)
- [ ] Empty SVG output is rejected
- [ ] Text explanation below 200 characters is rejected
- [ ] Valid outputs pass validation and advance to `Generated → Completed`

### Fallback (Phase 2 — Diagram → Text)

- [ ] If diagram generation fails, system falls back to text explanation
- [ ] User sees: "A diagram could not be generated. A text explanation has been created instead."
- [ ] `fallbackAttempt: 1` is set in the job record
- [ ] Fallback event is recorded in `learning_session_events`
- [ ] Session still reaches `Completed` via fallback

### Python AI Service

- [ ] Python FastAPI service starts and is reachable at http://localhost:8000
- [ ] `GET /health` returns 200
- [ ] `POST /generate` with a diagram contract returns a valid result
- [ ] Python service does NOT store any session state or user state
- [ ] Errors from Python service return `{ status: "Failed", errorCode, retryable }`

### Fast-Track Mode

- [ ] Typing "skip explanation, just generate" bypasses Steps 1–3
- [ ] System proceeds directly to Component Selection
- [ ] Approval and generation still required (not skipped)
- [ ] Session event records `trigger: "fast_track"` on the ConceptExplained skip

### Refinement Loop

- [ ] After viewing output, user can request "add more components"
- [ ] Session returns to `ComponentSelectionPending` with prior selections pre-populated
- [ ] User modifies selections, re-approves, new job is created
- [ ] New output is displayed
- [ ] Both jobs are recorded in `generation_jobs` for the same session

### Frontend Visualization Viewer

- [ ] Diagram renders in Diagram Viewer panel (not just as text)
- [ ] User can zoom and pan the diagram
- [ ] Session history shows previous outputs in the chat
- [ ] Output panel and chat panel are visually separated

---

---

# Phase 3 — Intelligence & RAG

## Goal

Ground all educational content in a real knowledge base. Add Qdrant for vector search, RAG
pipeline for context retrieval, multi-model LLM support with provider switching, and the
Algorithm Animation Engine. The system should now produce content based on retrieved knowledge,
not just LLM hallucination.

---

## Scope

### What is added in Phase 3

- Qdrant vector database (Docker container)
- Knowledge Retrieval Service (RAG pipeline: embed → search → inject)
- BGE-M3 or Nomic Embed embedding model
- Hybrid search (dense + sparse)
- Knowledge base population (admin can ingest PDFs and text documents)
- RAG confidence threshold enforcement (0.65 default)
- `KnowledgeRetrieved` state added to workflow
- Multi-model LLM routing (Qwen3 → Gemma → cloud fallback)
- Algorithm Animation Engine (D3.js) on frontend
- Code Visualization Engine (basic)
- Concept Breakdown Service upgraded (knowledge-base-first, LLM fallback)
- `componentSourceStrategy` field in event payloads ("knowledge_base" vs "ai_generated")
- Citation support (sources shown to user when knowledge base is used)

### What is NOT included in Phase 3

- Authentication
- RabbitMQ (still in-process background worker)
- 3D / Blender / video generation
- Full retry/fallback hierarchy
- Narration
- Asset library
- Observability tooling

---

## RAG Configuration (Phase 3)

| Parameter | Default | Config Key |
|---|---|---|
| Chunk size | 512 tokens | `RAG:ChunkSize` |
| Chunk overlap | 50 tokens | `RAG:ChunkOverlap` |
| Chunking strategy | Semantic (sentence-boundary) | `RAG:ChunkingStrategy` |
| Top-K results | 5 | `RAG:TopK` |
| Retrieval strategy | Hybrid (dense + sparse) | `RAG:RetrievalStrategy` |
| Retrieval confidence threshold | 0.65 | `WorkflowEngine:RetrievalConfidenceThreshold` |
| De-duplication | MMR (Maximal Marginal Relevance) | `RAG:Deduplication` |

---

## Prompt Injection Template

```
You are an expert educational AI.

Context retrieved from the knowledge base:
---
{retrieved_chunks}
---

User question: {user_question}

Provide an accurate, educational response based on the context above.
If the context does not contain sufficient information, state this clearly.
Do not invent facts not present in the context.
```

---

## Knowledge Miss Behavior

When retrieval confidence falls below 0.65:

1. Expand search scope (lower threshold to 0.50, increase top-K to 10)
2. Query alternate knowledge sources if configured
3. Notify user: "I couldn't find detailed information on this topic in the knowledge base.
   The following explanation is based on general AI knowledge."
4. Proceed with LLM-only response, clearly flagged

---

## New Workflow State (Phase 3)

`KnowledgeRetrieved` is inserted between `DomainClassified` and `ConceptExplained`:

```
DomainClassified → KnowledgeRetrieved → ConceptExplained
```

---

## New API Endpoints (Phase 3)

| Method | Endpoint | Description |
|---|---|---|
| POST | /api/admin/knowledge/ingest | Upload and ingest a document |
| GET | /api/admin/knowledge/status | View ingestion queue status |
| GET | /api/sessions/{id}/citations | Get retrieval citations for a session |

---

## Qdrant Schema (Phase 3)

Knowledge chunk document:

```json
{
  "id": "uuid",
  "vector": [0.12, -0.34, ...],
  "payload": {
    "chunkId": "uuid",
    "sourceDocument": "introduction-to-algorithms.pdf",
    "domain": "computer_science",
    "topic": "bubble_sort",
    "chunkText": "Bubble sort is a simple sorting algorithm...",
    "chunkIndex": 4,
    "ingestionDate": "ISO8601"
  }
}
```

---

## Multi-Model LLM Routing (Phase 3)

Provider selection order:

```
1. Primary local model (Qwen3 via Ollama)
2. Secondary local model (Gemma via Ollama)
3. Cloud provider (OpenRouter / OpenAI — if configured)
```

Config:

```json
{
  "AI": {
    "ModelProvider": "ollama",
    "OllamaModel": "qwen3",
    "FallbackOllamaModel": "gemma",
    "CloudProvider": "openrouter",
    "CloudApiKey": "",
    "CloudModel": "openai/gpt-4o"
  }
}
```

---

## Phase 3 Test Checklist

### Qdrant Setup

- [ ] Qdrant container starts successfully
- [ ] Qdrant UI accessible at http://localhost:6333/dashboard
- [ ] Knowledge collection created in Qdrant
- [ ] Embedding model (BGE-M3 or Nomic Embed) is available and producing vectors

### Knowledge Ingestion

- [ ] Admin can upload a PDF via `POST /api/admin/knowledge/ingest`
- [ ] Document is chunked into segments of approximately 512 tokens with 50-token overlap
- [ ] Each chunk is embedded and stored in Qdrant
- [ ] Ingestion status is visible at `GET /api/admin/knowledge/status`
- [ ] Ingested document appears as retrievable in vector search

### RAG Pipeline

- [ ] Asking about a topic covered by an ingested document retrieves relevant chunks
- [ ] Retrieved chunks have confidence scores above 0.65
- [ ] Chunks are injected into the LLM prompt using the defined template
- [ ] Response cites the source document
- [ ] `KnowledgeRetrieved` state is reached before `ConceptExplained`
- [ ] State transition event includes retrieval confidence score in payload

### Knowledge Miss Handling

- [ ] Asking about a topic NOT in the knowledge base triggers expanded search
- [ ] After expanded search, user receives notification that response is AI-only
- [ ] Session still advances to `ConceptExplained` (does not get stuck)
- [ ] "AI-generated" flag appears in session event payload

### Concept Breakdown — Knowledge Base First

- [ ] Component list for "bubble sort" is retrieved from knowledge base (not generated)
- [ ] `componentSourceStrategy: "knowledge_base"` appears in event payload
- [ ] For a niche topic not in knowledge base, components are LLM-generated
- [ ] `componentSourceStrategy: "ai_generated"` appears in event payload for LLM-generated lists

### Multi-Model Routing

- [ ] System uses Qwen3 by default
- [ ] Stopping Qwen3 (ollama stop qwen3) causes system to switch to Gemma
- [ ] Log entry shows model switch event
- [ ] Response quality remains acceptable after model switch
- [ ] If configured, cloud provider activates when both local models are unavailable

### Algorithm Animation Engine

- [ ] Requesting "visualize bubble sort" produces an animated D3.js visualization
- [ ] Animation shows step-by-step array comparisons and swaps
- [ ] User can control animation speed
- [ ] User can step forward/backward through animation steps
- [ ] All selected algorithm components are represented

### Code Visualization Engine

- [ ] Requesting "show me dependency injection" produces a code flow diagram
- [ ] Requesting "visualize the request lifecycle in ASP.NET" produces a step diagram

### Citations

- [ ] When RAG is used, citations appear in the response
- [ ] `GET /api/sessions/{id}/citations` returns source documents used
- [ ] Citations include document name and relevant excerpt summary

---

---

# Phase 4 — Resilience & Recovery

## Goal

Make the system production-safe. Add RabbitMQ for real job queuing, full retry policy with
exponential backoff, the complete fallback hierarchy (3D → 2D → diagram → text), session
recovery (Paused state), approval timeout, and the 3D Visualization Engine with Blender
integration.

---

## Scope

### What is added in Phase 4

- RabbitMQ (replaces in-process background worker)
- Per-job-type queues + dead-letter queue
- Full retry policy (3 attempts, exponential backoff: 5s / 15s / 45s)
- Complete fallback hierarchy (all 7 levels)
- `RetryExhausted`, `FallbackGeneration`, `Escalated`, `Paused` states
- Session resume (`Paused → GenerationQueued`)
- Approval timeout (24h → `ApprovalExpired`)
- Session clone (from `Cancelled` or `Paused`)
- 3D Visualization Engine (Three.js / React Three Fiber on frontend)
- Blender integration in Python AI Service (GLB generation)
- Video generation (MP4 via Python AI Service)
- Narration Service (Piper TTS)
- Full output validation profiles (video, GLB, diagram, text)
- Review Service (async, non-blocking)

### What is NOT included in Phase 4

- Authentication / JWT
- OpenTelemetry / Grafana (structured logging only)
- Asset library
- Role-based access control

---

## New Docker Compose Services (Phase 4)

```yaml
  rabbitmq:
    image: rabbitmq:3-management
    ports:
      - "5672:5672"
      - "15672:15672"
    environment:
      RABBITMQ_DEFAULT_USER: dev
      RABBITMQ_DEFAULT_PASS: dev

  python-ai:
    build: ./python-ai-service
    ports: ["8000:8000"]
    depends_on: [rabbitmq]
    environment:
      RABBITMQ_URL: amqp://dev:dev@rabbitmq:5672/
```

---

## Queue Design (Phase 4)

| Queue | Purpose |
|---|---|
| generation.3d | Blender / GLB rendering jobs |
| generation.video | Video encoding jobs |
| generation.diagram | Diagram generation jobs |
| generation.text | Text and narration jobs |
| generation.fallback | Fallback generation jobs |
| generation.dlq | Dead-letter queue |

Message envelope:

```json
{
  "messageId": "uuid",
  "queuedAt": "ISO8601",
  "routingKey": "generation.3d",
  "retryCount": 0,
  "payload": {
    "jobId": "uuid",
    "sessionId": "uuid",
    "visualizationType": "3d_animation",
    "concept": "car_engine",
    "domain": "mechanical_engineering",
    "components": ["Cylinder", "Piston", "Crankshaft", "Spark Plug"],
    "settings": {
      "narration": true,
      "labels": true,
      "difficulty": "beginner",
      "detailLevel": "educational",
      "renderingStyle": "interactive"
    },
    "fallbackAttempt": 0
  }
}
```

---

## Retry Configuration (Phase 4)

```json
{
  "WorkflowEngine": {
    "MaxRetryAttempts": 3,
    "RetryBackoffSeconds": [5, 15, 45],
    "ApprovalTimeoutHours": 24,
    "PausedSessionTimeoutDays": 7,
    "QueueWaitTimeoutMinutes": 30
  }
}
```

---

## Fallback Hierarchy (Phase 4)

| Level | Type | Engine | Job Queue |
|---|---|---|---|
| 0 | 3D Animation | Three.js + Blender | generation.3d |
| 1 | 2D Animation | D3.js / Canvas | generation.diagram |
| 2 | Interactive Simulation | D3.js / Canvas | generation.diagram |
| 3 | Interactive Diagram | Mermaid / React Flow | generation.diagram |
| 4 | Static Diagram | Mermaid | generation.diagram |
| 5 | Narrated Explanation | Piper TTS | generation.text |
| 6 | Text Explanation | In-process LLM | generation.text |

---

## Review Severity Classification (Phase 4)

| Severity | Examples | State Machine Action |
|---|---|---|
| Minor | Slight wording, minor metadata gap | `Reviewed → Completed` (warning attached) |
| Major | Missing requested component, educational omission | `Reviewed → GenerationQueued` |
| Critical | Corrupted artifact, factually wrong content | `Reviewed → FallbackGeneration` |

Review runs asynchronously. Users access output in `Generated` state while review runs.

---

## Phase 4 Test Checklist

### RabbitMQ

- [ ] RabbitMQ management UI accessible at http://localhost:15672
- [ ] All queues created: `generation.3d`, `generation.video`, `generation.diagram`,
      `generation.text`, `generation.fallback`, `generation.dlq`
- [ ] Approving a plan publishes a message to the correct queue based on visualization type
- [ ] Python AI Service worker consumes messages from the queue
- [ ] Dead-letter queue receives messages that exceed max retry attempts

### Retry Policy

- [ ] Simulating a generation failure triggers retry after 5 seconds
- [ ] Second failure triggers retry after 15 seconds
- [ ] Third failure triggers retry after 45 seconds
- [ ] Fourth failure (retry exhausted) does NOT retry again
- [ ] `RetryCount` in the job record increments correctly (0 → 1 → 2 → 3)
- [ ] After retry exhaustion, session transitions to `RetryExhausted`
- [ ] Retry events are recorded in `learning_session_events`

### Fallback Hierarchy

- [ ] When 3D generation fails after retries, system attempts 2D animation
- [ ] When 2D animation fails, system attempts interactive diagram
- [ ] When diagram fails, system attempts static diagram
- [ ] When static diagram fails, system attempts narrated explanation
- [ ] When narration fails, system falls back to text explanation
- [ ] User receives a message explaining which fallback was used
- [ ] `fallbackAttempt` in job record reflects the level that succeeded
- [ ] All fallback attempts are recorded as separate job records

### 3D Visualization Engine

- [ ] Requesting "show me a car engine" generates a GLB file
- [ ] GLB file loads and renders in Three.js viewer
- [ ] User can rotate the 3D model
- [ ] User can zoom and pan
- [ ] Selected components are labeled in the 3D scene
- [ ] GLB file size exceeds 20KB (validation passes)
- [ ] All selected components appear in the 3D scene graph

### Blender Integration

- [ ] Python AI Service can invoke Blender headlessly
- [ ] Blender produces a valid GLB output
- [ ] Blender crash is detected and returns `{ status: "Failed", errorCode: "BLENDER_CRASH", retryable: true }`
- [ ] Job times out after 10 minutes and transitions to `Failed`

### Video Generation

- [ ] Requesting a video animation produces an MP4 file
- [ ] MP4 is playable in the frontend viewer
- [ ] Duration > 1 second (validation passes)
- [ ] File size > 50KB (validation passes)

### Narration Service

- [ ] Piper TTS generates speech from a text explanation
- [ ] Audio plays in the frontend narration controls
- [ ] Narration can be enabled/disabled from the UI
- [ ] Subtitle file is generated alongside audio

### Approval Timeout

- [ ] Session in `ApprovalPending` transitions to `ApprovalExpired` after configured timeout
  (set timeout to 1 minute in test config to verify)
- [ ] User is notified of expiry
- [ ] "Resume Planning" option restores session to `VisualizationPlanned`
- [ ] "Regenerate Plan" option returns to `ComponentSelectionPending` with prior selections
- [ ] "Cancel Session" moves session to `Cancelled`
- [ ] `ApprovalExpired` event is recorded with `expiredAt` timestamp

### Session Resume (Paused State)

- [ ] Simulating LLM unavailability transitions session to `Escalated → Paused`
- [ ] User sees: "The AI generation service is temporarily unavailable. Your session has been saved."
- [ ] Session state is preserved in PostgreSQL
- [ ] When service recovers, user can click "Resume" to return to `GenerationQueued`
- [ ] Paused session transitions to `Cancelled` after 7-day timeout (set to 1 minute in test)

### Session Clone

- [ ] Cancelled session shows "Clone Session" option in session history
- [ ] Clicking clone creates a new session at `ComponentSelectionPending`
- [ ] Prior component selections are pre-populated
- [ ] New session has `clonedFromSessionId` set to original session ID
- [ ] Explanation steps are skipped (new session starts at selection)

### Review Service (Non-Blocking)

- [ ] User can access generated output immediately without waiting for review
- [ ] Review runs asynchronously in the background
- [ ] Minor review issue: session completes with a warning attached to output
- [ ] Major review issue: session returns to `GenerationQueued` for regeneration
- [ ] Critical review issue: session enters `FallbackGeneration`
- [ ] Review events are recorded in `learning_session_events`

### Concurrency Policy

- [ ] User cannot have two simultaneous active generation jobs
- [ ] Second generation request while one is active is queued and user notified
- [ ] Queued request auto-starts when first job completes
- [ ] Queued request is cancellable
- [ ] Queued request that waits longer than 30 minutes is cancelled with notification

---

---

# Phase 5 — Security, Observability & Scale

## Goal

Harden the platform for production. Add JWT authentication, role-based authorization,
OpenTelemetry distributed tracing, Grafana metrics dashboards, the Visualization Asset Library,
alert thresholds, and the Interactive Simulation Engine. The platform is now suitable for
multi-user production deployment.

---

## Scope

### What is added in Phase 5

- JWT authentication (ASP.NET Core Identity)
- Role-based authorization (Student / Educator / Admin)
- Anonymous access policy
- OpenTelemetry instrumentation (ASP.NET Core + Python Service)
- OTLP export to Jaeger / Grafana Tempo
- Prometheus metrics + Grafana dashboards
- Alert thresholds (queue depth, retry rate, generation duration)
- Visualization Asset Library (Qdrant asset index, reuse decision rule)
- Asset ingestion by Educators / Admins
- Interactive Simulation Engine (D3.js physics simulations)
- External OAuth 2.0 provider support (optional)
- Session data retained 30 days post-cancellation
- Production Docker Compose (with resource limits)
- Knowledge base population by Educator role
- Educator content review queue (for AI-generated component lists)

### What is NOT included in Phase 5

- Kubernetes deployment (future)
- Fine-tuning of local models (future)
- Community asset contributions (future)
- Mobile app (future)

---

## Auth Specification (Phase 5)

### Mechanism

JWT tokens issued by ASP.NET Core Identity on login.

Access token lifetime: 1 hour
Refresh token lifetime: 7 days

All API endpoints require a valid JWT except:
- `GET /api/domains` (domain list, public)
- `GET /api/examples` (example visualizations, public)
- `POST /api/auth/login`
- `POST /api/auth/register`

### Roles

| Role | Permissions |
|---|---|
| Student | Create sessions, generate visualizations, view own history |
| Educator | All Student permissions + ingest knowledge base content + review AI-generated component lists |
| Admin | All permissions + user management + role assignment + system configuration |

### New Endpoints (Phase 5 Auth)

| Method | Endpoint | Description |
|---|---|---|
| POST | /api/auth/register | Register new user |
| POST | /api/auth/login | Login, receive JWT |
| POST | /api/auth/refresh | Refresh access token |
| GET | /api/users/me | Get current user profile |
| GET | /api/admin/users | List users (Admin only) |
| PATCH | /api/admin/users/{id}/role | Assign role (Admin only) |

---

## Asset Library (Phase 5)

### Asset Schema (Qdrant)

```json
{
  "assetId": "uuid",
  "assetType": "3d_model",
  "domain": "mechanical_engineering",
  "componentType": "piston",
  "detailLevel": "educational",
  "renderingStyle": "realistic",
  "fileUrl": "assets/mechanical/piston-educational-realistic.glb",
  "format": "glb",
  "fileSizeKB": 340,
  "tags": ["piston", "engine", "mechanical"],
  "createdDate": "ISO8601",
  "usageCount": 142
}
```

### Reuse Decision Rule

An asset is eligible for reuse when all four match: `domain` + `componentType` +
`detailLevel` + `renderingStyle`.

Partial match (wrong detail level): adapt the asset (simplify or enhance) rather than
generate from scratch.

No match: generate new asset, validate, cache in asset library for future reuse.

### New Endpoints (Phase 5 Assets)

| Method | Endpoint | Role Required |
|---|---|---|
| GET | /api/assets | Student |
| POST | /api/assets/ingest | Educator / Admin |
| GET | /api/assets/{id} | Student |
| DELETE | /api/assets/{id} | Admin |

---

## Observability Stack (Phase 5)

```yaml
  jaeger:
    image: jaegertracing/all-in-one:latest
    ports: ["16686:16686", "4317:4317"]

  prometheus:
    image: prom/prometheus
    ports: ["9090:9090"]
    volumes: ["./prometheus.yml:/etc/prometheus/prometheus.yml"]

  grafana:
    image: grafana/grafana
    ports: ["3001:3000"]
    depends_on: [prometheus]
```

### Metrics Collected

| Metric | Description |
|---|---|
| `session.completion_rate` | % of sessions reaching Completed |
| `job.retry_rate` | % of jobs requiring at least one retry |
| `job.fallback_rate` | % of jobs triggering fallback generation |
| `job.generation_duration_seconds` | Time from GenerationQueued to Generated |
| `retrieval.latency_ms` | RAG retrieval latency |
| `queue.depth` | Current depth per queue |

### Alert Thresholds

| Metric | Warning | Critical |
|---|---|---|
| `queue.depth` (any queue) | > 50 | > 200 |
| `job.retry_rate` (5-min window) | > 20% | > 50% |
| `job.generation_duration_seconds` (3D) | > 5 minutes | > 10 minutes |
| `retrieval.latency_ms` | > 2000ms | > 5000ms |

---

## Phase 5 Test Checklist

### Authentication

- [ ] `POST /api/auth/register` creates a new user with Student role by default
- [ ] `POST /api/auth/login` returns a JWT access token and refresh token
- [ ] Valid JWT required to access `/api/sessions` (401 without token)
- [ ] Expired JWT is rejected (401)
- [ ] `POST /api/auth/refresh` issues a new access token
- [ ] User data is stored in PostgreSQL `users` table

### Authorization

- [ ] Student can create sessions and generate visualizations
- [ ] Student cannot access `/api/admin/users` (403)
- [ ] Student cannot ingest knowledge base content (403)
- [ ] Educator can ingest documents via `POST /api/admin/knowledge/ingest`
- [ ] Educator cannot access user management endpoints (403)
- [ ] Admin can assign roles via `PATCH /api/admin/users/{id}/role`
- [ ] Admin can access all endpoints
- [ ] Anonymous user can access `GET /api/domains` and `GET /api/examples`
- [ ] Anonymous user receives 401 when attempting `POST /api/sessions`

### Anonymous Access

- [ ] Domain list loads without authentication
- [ ] Example visualizations load without authentication
- [ ] No session data or personal data is exposed to anonymous users

### Asset Library

- [ ] Admin can upload a GLB asset via `POST /api/assets/ingest`
- [ ] Asset is stored in Qdrant with correct schema fields
- [ ] Visualization Planner queries asset library before generating
- [ ] Exact match: existing asset is reused, no new generation job queued
- [ ] Partial match: asset is adapted, generation time is shorter
- [ ] No match: new generation job queued, result cached as new asset after validation
- [ ] `usageCount` increments each time an asset is reused
- [ ] Admin can delete an asset

### Interactive Simulation Engine

- [ ] Requesting "simulate projectile motion" produces an interactive physics simulation
- [ ] User can adjust initial velocity with a slider
- [ ] User can adjust launch angle with a slider
- [ ] Simulation updates in real time based on user input
- [ ] Requesting "circuit simulation" produces a basic circuit diagram with current flow

### OpenTelemetry

- [ ] ASP.NET Core emits traces to Jaeger/Tempo via OTLP
- [ ] Python AI Service emits traces to Jaeger/Tempo via OTLP
- [ ] A single user request can be traced end-to-end: frontend → API → queue → Python → result
- [ ] Trace IDs match between ASP.NET Core logs and Python service logs
- [ ] `TraceId` and `SpanId` in `learning_session_events` match Jaeger traces
- [ ] Jaeger UI shows full trace for a 3D generation job

### Prometheus & Grafana

- [ ] Prometheus scrapes metrics from ASP.NET Core (`/metrics` endpoint)
- [ ] Grafana dashboard shows `session.completion_rate` over time
- [ ] Grafana dashboard shows `queue.depth` per queue
- [ ] Grafana dashboard shows `job.generation_duration_seconds` histogram
- [ ] Grafana dashboard shows `retrieval.latency_ms` percentiles

### Alerting

- [ ] Artificially filling the queue past 50 messages triggers a warning log/alert
- [ ] Artificially filling past 200 messages triggers a critical alert
- [ ] Simulating > 20% retry rate over 5 minutes triggers a retry rate warning

### Data Retention

- [ ] Cancelled sessions are retained in PostgreSQL for 30 days
- [ ] After 30 days, cancelled sessions are soft-deleted or archived
- [ ] Session event history is preserved for retained sessions
- [ ] User can view cancelled sessions in session history during retention period

### Educator Content Review

- [ ] AI-generated component lists are flagged with `componentSourceStrategy: "ai_generated"`
- [ ] Educator can view flagged lists at `GET /api/educator/review-queue`
- [ ] Educator can approve a flagged list, promoting it to the knowledge base
- [ ] Approved component lists are subsequently retrieved (not re-generated)

---

---

# Complete Workflow State Machine Reference

This is the authoritative unified transition table for all phases.

| From State | Trigger | Guard Condition | To State | Phase |
|---|---|---|---|---|
| Created | User submits request | Session valid, request non-empty | IntentAnalyzed | 1 |
| IntentAnalyzed | Intent classified | Confidence ≥ 0.75 | DomainClassified | 1 |
| IntentAnalyzed | Confidence low | Confidence < 0.75 | IntentAnalyzed (clarify) | 1 |
| DomainClassified | Domain identified | Domain recognized | KnowledgeRetrieved | 3 |
| DomainClassified | Domain identified | Domain recognized (Phase 1–2) | ConceptExplained | 1 |
| KnowledgeRetrieved | Knowledge retrieved | Confidence ≥ 0.65 | ConceptExplained | 3 |
| KnowledgeRetrieved | Knowledge miss | Confidence < 0.65 after expansion | ConceptExplained (AI-only flag) | 3 |
| ConceptExplained | User follow-up question | — | ConceptExplained (sub-dialogue) | 1 |
| ConceptExplained | User ready to select | — | ComponentSelectionPending | 1 |
| ComponentSelectionPending | Selections submitted | Visualization type + ≥1 component | VisualizationPlanned | 1 |
| ComponentSelectionPending | Incomplete | Missing required field | ComponentSelectionPending (prompt) | 1 |
| VisualizationPlanned | Plan validated | Plan passes validation | ApprovalPending | 1 |
| VisualizationPlanned | Plan invalid | Missing required components | VisualizationPlanned (regenerate) | 1 |
| ApprovalPending | User approves | Explicit approval received | GenerationQueued | 2 |
| ApprovalPending | User modifies | User requests component changes | ComponentSelectionPending | 1 |
| ApprovalPending | User restarts | User requests new explanation | ConceptExplained | 1 |
| ApprovalPending | Timeout elapsed | Time since entry ≥ 24h | ApprovalExpired | 4 |
| ApprovalExpired | User resumes | — | VisualizationPlanned | 4 |
| ApprovalExpired | User regenerates | — | ComponentSelectionPending | 4 |
| ApprovalExpired | User cancels | — | Cancelled | 4 |
| GenerationQueued | Worker picks up job | Queue available, worker available | Generating | 2 |
| Generating | Generation succeeds | Output valid | Generated | 2 |
| Generating | Generation fails | Output missing or invalid | Failed | 4 |
| Generated | User accesses output | — | Completed | 2 |
| Generated | Review completes (async) | No critical issues | Reviewed | 4 |
| Reviewed | Minor issues | Minor severity | Completed (warning) | 4 |
| Reviewed | Major issues | Major severity | GenerationQueued | 4 |
| Reviewed | Critical issues | Critical severity | FallbackGeneration | 4 |
| Failed | Retry allowed | RetryCount < 3 | Retrying | 4 |
| Failed | Retry exhausted | RetryCount ≥ 3, rendering failure | RetryExhausted → FallbackGeneration | 4 |
| Failed | Retry exhausted | RetryCount ≥ 3, LLM failure | RetryExhausted → Escalated | 4 |
| Retrying | Backoff elapsed | — | Generating | 4 |
| FallbackGeneration | Fallback succeeds | Any fallback level succeeds | Generated | 4 |
| FallbackGeneration | All fallbacks exhausted | All 7 levels failed | Escalated | 4 |
| Escalated | — | — | Paused | 4 |
| Paused | User resumes | Dependency healthy | GenerationQueued | 4 |
| Paused | Timeout elapsed | Time since entry ≥ 7 days | Cancelled | 4 |

---

# Complete Configuration Reference

All configurable values across all phases:

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=localhost;Database=visuallearning;Username=dev;Password=dev",
    "RabbitMQ": "amqp://dev:dev@localhost:5672/"
  },
  "AI": {
    "ModelProvider": "ollama",
    "OllamaBaseUrl": "http://localhost:11434",
    "OllamaModel": "qwen3",
    "FallbackOllamaModel": "gemma",
    "CloudProvider": "openrouter",
    "CloudApiKey": "",
    "CloudModel": "openai/gpt-4o"
  },
  "WorkflowEngine": {
    "IntentConfidenceThreshold": 0.75,
    "DomainConfidenceThreshold": 0.70,
    "RetrievalConfidenceThreshold": 0.65,
    "MaxRetryAttempts": 3,
    "RetryBackoffSeconds": [5, 15, 45],
    "ApprovalTimeoutHours": 24,
    "PausedSessionTimeoutDays": 7,
    "QueueWaitTimeoutMinutes": 30
  },
  "RAG": {
    "ChunkSize": 512,
    "ChunkOverlap": 50,
    "ChunkingStrategy": "semantic",
    "TopK": 5,
    "RetrievalStrategy": "hybrid",
    "Deduplication": "mmr"
  },
  "Validation": {
    "TextMinimumLength": 200,
    "VideoMinimumSizeKB": 50,
    "GLBMinimumSizeKB": 20
  },
  "PythonService": {
    "BaseUrl": "http://localhost:8000",
    "JobTimeoutMinutes": 10
  },
  "Auth": {
    "JwtSecret": "",
    "AccessTokenLifetimeMinutes": 60,
    "RefreshTokenLifetimeDays": 7
  }
}
```

---

# Success Criteria by Phase

| Metric | Phase 1 Target | Phase 2 Target | Phase 3 Target | Phase 4 Target | Phase 5 Target |
|---|---|---|---|---|---|
| Workflow completion rate | ≥ 60% | ≥ 65% | ≥ 70% | ≥ 75% | ≥ 80% |
| Generation success rate | n/a (text only) | ≥ 85% | ≥ 90% | ≥ 95% | ≥ 97% |
| Steps to first visualization | ≤ 10 | ≤ 9 | ≤ 8 | ≤ 8 | ≤ 7 |
| Fallback trigger rate | n/a | < 20% | < 15% | < 10% | < 8% |
| Retrieval latency (p95) | n/a | n/a | < 3s | < 2.5s | < 2s |
| 3D generation duration (p95) | n/a | n/a | n/a | < 7 min | < 5 min |

---

# Architectural Principles (All Phases)

- **Workflow Driven** — every interaction follows a defined state machine
- **Event Driven** — every state transition produces an auditable event
- **State Driven** — no implicit state; all state lives in PostgreSQL
- **Deterministic** — the same inputs always produce the same state transitions
- **Recoverable** — every failure has a defined recovery path
- **Retryable** — all generation operations support configurable retry with backoff
- **Observable** — all transitions, jobs, and failures are traced and metered
- **Auditable** — complete event history is retained and replayable
- **Extensible** — new visualization engines, domains, and AI providers are added via new
  service implementations behind existing interfaces, without modifying the workflow engine

ASP.NET Core is the brain of the platform. Python is a specialized AI worker.

The system must always maximize educational value, gracefully degrade under failure, and ensure
the user receives the highest-quality learning experience possible.
