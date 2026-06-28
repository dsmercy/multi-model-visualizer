import { useState, useEffect, useRef, useCallback, lazy, Suspense } from 'react';
import {
  createSession, getSession, sendMessage, approveSession, rejectSession,
  refineSession, getSessionEvents, getJobResult, subscribeJobProgress,
  getSessionCitations, resumeSession, cloneSession, cancelSession,
} from '../api/client';
import type { ChatMessage, SessionEvent, JobProgressEvent, JobResult, CitationDto } from '../types';
import { Message } from './Message';
import { SessionState } from './SessionState';
import { MermaidDiagram } from './MermaidDiagram';
import { JobProgress } from './JobProgress';
import AlgorithmAnimation from './AlgorithmAnimation';
import { VideoPlayer } from './VideoPlayer';
import { AudioPlayer } from './AudioPlayer';
const GLBViewer = lazy(() => import('./GLBViewer').then(m => ({ default: m.GLBViewer })));

let msgCounter = 0;
const genId = () => `msg-${++msgCounter}-${Date.now()}`;

const thinkingStyle = document.createElement('style');
thinkingStyle.textContent = `
  @keyframes blink {
    0%, 80%, 100% { opacity: 0; }
    40% { opacity: 1; }
  }
  .thinking-dot {
    display: inline-block;
    width: 4px;
    height: 4px;
    border-radius: 50%;
    background: #64748b;
    margin: 0 2px;
    animation: blink 1.4s infinite both;
  }
  .thinking-dot:nth-child(2) { animation-delay: 0.2s; }
  .thinking-dot:nth-child(3) { animation-delay: 0.4s; }
`;
if (!document.head.querySelector('[data-thinking]')) {
  thinkingStyle.setAttribute('data-thinking', '');
  document.head.appendChild(thinkingStyle);
}

function ThinkingIndicator() {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '6px 0', color: '#64748b', fontSize: 13 }}>
      <span>AI is thinking</span>
      <span className="thinking-dot" />
      <span className="thinking-dot" />
      <span className="thinking-dot" />
    </div>
  );
}

interface ChatProps {
  externalSessionId?: string | null;
  onSessionChange?: (id: string | null) => void;
}

export function Chat({ externalSessionId, onSessionChange }: ChatProps = {}) {
  const [sessionId, setSessionIdState] = useState<string | null>(null);

  const setSessionId = (id: string | null) => {
    setSessionIdState(id);
    onSessionChange?.(id);
  };

  // Sync if parent changes the session (e.g. user clicked a history entry)
  useEffect(() => {
    if (externalSessionId === undefined || externalSessionId === sessionId) return;
    setSessionIdState(externalSessionId);
    // Reset derived state so stale topic/components from previous session don't bleed in
    setTopic('');
    setComponents([]);
    setShowAnimation(false);
    setJobResult(null);
    setCitations([]);
    setComponentSourceStrategy('ai_generated');
    setSelectedVizType('');
    if (externalSessionId) {
      // Load persisted topic + components from API
      getSession(externalSessionId).then(s => {
        if (s.topic) setTopic(s.topic);
        if (s.selectedComponents) setComponents(s.selectedComponents.split(',').map((c: string) => c.trim()));
        setCurrentState(s.currentState);
      }).catch(() => {});
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [externalSessionId]);
  const [currentState, setCurrentState] = useState<string>('Created');
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [isInitializing, setIsInitializing] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showEvents, setShowEvents] = useState(false);
  const [events, setEvents] = useState<SessionEvent[]>([]);
  const [eventsLoading, setEventsLoading] = useState(false);

  // Phase 2 job tracking
  const [activeJobId, setActiveJobId] = useState<string | null>(null);
  const [jobProgress, setJobProgress] = useState<JobProgressEvent | null>(null);
  const [jobResult, setJobResult] = useState<JobResult | null>(null);
  const jobAbortRef = useRef<AbortController | null>(null);
  const messagesEndRef = useRef<HTMLDivElement>(null);

  // Phase 3 — citations + animation
  const [citations, setCitations] = useState<CitationDto[]>([]);
  const [componentSourceStrategy, setComponentSourceStrategy] = useState<string>('ai_generated');
  const [showAnimation, setShowAnimation] = useState(false);
  const [topic, setTopic] = useState<string>('');
  const [components, setComponents] = useState<string[]>([]);

  // Output type selector
  const [selectedVizType, setSelectedVizType] = useState<string>('');

  const scrollToBottom = useCallback(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, []);

  useEffect(() => { scrollToBottom(); }, [messages, scrollToBottom]);

  const addAssistantMessage = useCallback((content: string, extras?: Partial<ChatMessage>) => {
    setMessages(prev => [...prev, { id: genId(), role: 'assistant', content, timestamp: new Date(), ...extras }]);
  }, []);

  // Start SSE subscription for a job
  const watchJob = useCallback((jobId: string) => {
    if (jobAbortRef.current) jobAbortRef.current.abort();
    const ctrl = new AbortController();
    jobAbortRef.current = ctrl;
    setActiveJobId(jobId);
    setJobProgress(null);
    setJobResult(null);

    subscribeJobProgress(
      jobId,
      (evt) => {
        setJobProgress(evt);
        if (evt.status === 'Generating') setCurrentState('Generating');
      },
      async () => {
        // SSE stream closed — fetch final result
        try {
          const result = await getJobResult(jobId);
          setJobResult(result);
          setCurrentState(result.status === 'Completed' ? 'Completed' : 'Failed');

          if (result.status === 'Completed') {
            const isMermaid = result.outputType === 'mermaid';
            const isText = result.outputType === 'text';
            const isVideo = result.outputType === 'video';
            const isAudio = result.outputType === 'audio';
            const isGlb = result.outputType === 'glb';
            let msg = '**Visualization Generated!**\n\n';
            if (result.fallbackAttempt > 0) {
              msg += `_Generated at fallback level ${result.fallbackAttempt} (${result.outputType})._\n\n`;
            }
            if (isText && result.outputContent) {
              msg += result.outputContent;
            } else if (isMermaid) {
              msg += '_Diagram rendered in the output panel →_';
            } else if (isVideo) {
              msg += '_Video rendered in the output panel →_';
            } else if (isAudio) {
              msg += '_Audio narration rendered in the output panel →_';
            } else if (isGlb) {
              msg += '_3D scene rendered in the output panel →_';
            }
            msg += '\n\n---\nSay **"refine"** to modify components, or start a **New Session**.';
            setMessages(prev => [...prev, {
              id: genId(), role: 'assistant', content: msg,
              timestamp: new Date(), jobId, jobResult: result,
            }]);

            // Fetch citations for this session
            if (sessionId) {
              getSessionCitations(sessionId).then(c => {
                setCitations(c.citations);
                setComponentSourceStrategy(c.componentSourceStrategy);
              }).catch(() => {});
            }
          } else {
            addAssistantMessage(`Generation failed: ${result.outputContent ?? 'Unknown error'}.\n\nSay **"refine"** to try again.`);
          }
        } catch {
          addAssistantMessage('Generation complete — could not fetch final result.');
        }
        setActiveJobId(null);
      },
      ctrl.signal,
    );
  }, [addAssistantMessage]);

  // Init session
  useEffect(() => {
    const init = async () => {
      try {
        setIsInitializing(true);
        const session = await createSession();
        setSessionId(session.sessionId);
        setCurrentState(session.currentState);
        setMessages([{
          id: genId(), role: 'assistant', timestamp: new Date(),
          content: 'Welcome to the AI Visual Learning Platform!\n\nI guide you through a 7-step workflow to create educational visualizations.\n\nWhat would you like to learn today? Examples:\n• "Explain how TCP/IP networking works"\n• "Help me understand bubble sort"\n• "Walk me through how a car engine works"\n\nTip: say **"skip explanation, just generate"** to jump straight to component selection.',
        }]);
        setError(null);
      } catch {
        setError('Failed to connect to the API server on port 5000.');
      } finally {
        setIsInitializing(false);
      }
    };
    init();
  }, []);

  const handleSend = async () => {
    if (!input.trim() || !sessionId || isLoading) return;
    const content = input.trim();
    setInput('');
    setMessages(prev => [...prev, { id: genId(), role: 'user', content, timestamp: new Date() }]);
    setIsLoading(true);
    setError(null);

    try {
      const resp = await sendMessage(sessionId, content);
      setCurrentState(resp.newState);
      // Capture topic from ComponentSelectionPending message (has "Topic: ..." header)
      if (resp.newState === 'ComponentSelectionPending' && resp.message.includes('**Topic:')) {
        const topicMatch = resp.message.match(/\*\*Topic:\s*([^*\n]+)\*\*/);
        if (topicMatch) setTopic(topicMatch[1].trim());
        const compMatch = resp.message.match(/\d+\.\s*(.+)/g);
        if (compMatch) setComponents(compMatch.map(c => c.replace(/^\d+\.\s*/, '').trim()));
      }
      addAssistantMessage(resp.message);
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Unexpected error';
      setError(msg);
      addAssistantMessage(`Error: ${msg}\n\nPlease try again.`);
    } finally {
      setIsLoading(false);
    }
  };

  const handleApprove = async () => {
    if (!sessionId) return;
    setIsLoading(true);
    setError(null);
    try {
      const resp = await approveSession(sessionId, selectedVizType || undefined);
      setCurrentState('GenerationQueued');
      addAssistantMessage(`Generation queued! Your visualization is being created.\n\nJob: \`${resp.jobId}\`\n\nWatch the progress indicator below.`);
      watchJob(resp.jobId);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to approve');
    } finally {
      setIsLoading(false);
    }
  };

  const handleReject = async () => {
    if (!sessionId) return;
    setIsLoading(true);
    try {
      const resp = await rejectSession(sessionId);
      setCurrentState(resp.newState);
      addAssistantMessage(resp.message);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to reject');
    } finally {
      setIsLoading(false);
    }
  };

  const handleRefine = async () => {
    if (!sessionId) return;
    setIsLoading(true);
    setJobResult(null);
    setJobProgress(null);
    setActiveJobId(null);
    setCitations([]);
    setShowAnimation(false);
    setSelectedVizType('');
    try {
      const resp = await refineSession(sessionId);
      setCurrentState(resp.newState);
      addAssistantMessage(resp.message);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to refine');
    } finally {
      setIsLoading(false);
    }
  };

  const handleResume = async () => {
    if (!sessionId) return;
    setIsLoading(true);
    try {
      const resp = await resumeSession(sessionId);
      setCurrentState(resp.newState);
      addAssistantMessage(resp.message);
      if (resp.newState === 'GenerationQueued') {
        // fetch the new job id from status
        const status = await (await fetch(`/api/sessions/${sessionId}/status`)).json() as { activeJobs: number };
        void status; // job will be tracked by SSE when approved next
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to resume');
    } finally { setIsLoading(false); }
  };

  const handleClone = async () => {
    if (!sessionId) return;
    setIsLoading(true);
    try {
      const resp = await cloneSession(sessionId);
      // Switch to the new session
      setSessionId(resp.newSessionId);
      setCurrentState(resp.currentState);
      setMessages([{
        id: genId(), role: 'assistant', timestamp: new Date(),
        content: `Session cloned! Starting fresh from your previous component selection.\n\nSession ID: \`${resp.newSessionId}\``,
      }]);
      setCitations([]); setComponentSourceStrategy('ai_generated');
      setShowAnimation(false); setJobResult(null); setJobProgress(null); setActiveJobId(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to clone session');
    } finally { setIsLoading(false); }
  };

  const handleCancel = async () => {
    if (!sessionId) return;
    if (!window.confirm('Cancel this session? You can clone it later to restart from your component selection.')) return;
    setIsLoading(true);
    try {
      const resp = await cancelSession(sessionId);
      setCurrentState(resp.newState);
      addAssistantMessage('Session cancelled. Use **Clone Session** to start a new one with the same topic and components.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to cancel');
    } finally { setIsLoading(false); }
  };

  const handleNewSession = async () => {
    if (jobAbortRef.current) { jobAbortRef.current.abort(); jobAbortRef.current = null; }
    setIsInitializing(true);
    setMessages([]); setError(null); setShowEvents(false); setEvents([]);
    setActiveJobId(null); setJobProgress(null); setJobResult(null);
    setCitations([]); setComponentSourceStrategy('ai_generated'); setShowAnimation(false); setTopic(''); setComponents([]); setSelectedVizType('');
    try {
      const session = await createSession();
      setSessionId(session.sessionId);
      setCurrentState(session.currentState);
      setMessages([{ id: genId(), role: 'assistant', content: 'New session started! What would you like to learn today?', timestamp: new Date() }]);
    } catch { setError('Failed to create a new session.'); }
    finally { setIsInitializing(false); }
  };

  const handleToggleEvents = async () => {
    if (!showEvents && sessionId) {
      setEventsLoading(true);
      try { setEvents(await getSessionEvents(sessionId)); } catch { /* ignore */ }
      finally { setEventsLoading(false); }
    }
    setShowEvents(p => !p);
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); handleSend(); }
  };

  const isGenerating = ['GenerationQueued', 'Generating', 'FallbackGeneration', 'Retrying'].includes(currentState);
  const isApprovalPending = currentState === 'ApprovalPending';
  const isCompleted = ['Completed', 'Reviewed'].includes(currentState);
  const isFailed = ['Failed', 'RetryExhausted'].includes(currentState);
  const isPaused = currentState === 'Paused';
  const isCancelled = currentState === 'Cancelled';
  const isApprovalExpired = currentState === 'ApprovalExpired';
  const hasOutput = jobResult?.status === 'Completed' && (jobResult.outputContent || jobResult.outputUrl);
  const isMermaid = jobResult?.outputType === 'mermaid' && !!jobResult.outputContent;
  const hasKnowledgeBase = componentSourceStrategy === 'knowledge_base' && citations.length > 0;

  // ── Styles ────────────────────────────────────────────────────────────────
  const s = {
    outer: { display: 'flex', flexDirection: 'column' as const, height: '100vh', backgroundColor: '#0a0a14', color: '#e2e8f0', fontFamily: 'system-ui, -apple-system, sans-serif' },
    header: { backgroundColor: '#0d0d1f', borderBottom: '1px solid #1e1e2e', padding: '12px 20px', display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexShrink: 0 },
    title: { fontSize: '17px', fontWeight: '700' as const, color: '#a5b4fc' },
    subtitle: { fontSize: '11px', color: '#475569', marginTop: '2px' },
    btn: { padding: '5px 12px', borderRadius: '7px', border: '1px solid #2d2d3f', backgroundColor: '#1e1e2e', color: '#94a3b8', cursor: 'pointer', fontSize: '12px' },
    body: { flex: 1, display: 'flex', overflow: 'hidden' as const },
    chatCol: { flex: hasOutput ? '0 0 55%' : 1, display: 'flex', flexDirection: 'column' as const, overflow: 'hidden' as const, borderRight: hasOutput ? '1px solid #1e1e2e' : 'none' },
    outputCol: { flex: '1 1 45%', overflow: 'auto' as const, padding: '16px', backgroundColor: '#08080f', display: hasOutput ? 'block' : 'none' },
    msgs: { flex: 1, overflowY: 'auto' as const, padding: '16px 20px', display: 'flex', flexDirection: 'column' as const },
    inputArea: { borderTop: '1px solid #1e1e2e', padding: '12px 16px', backgroundColor: '#0d0d1f', flexShrink: 0 },
    inputRow: { display: 'flex', gap: '10px', alignItems: 'flex-end' },
    textarea: { flex: 1, backgroundColor: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: '10px', padding: '10px 14px', color: '#e2e8f0', fontSize: '14px', fontFamily: 'inherit', resize: 'none' as const, outline: 'none', minHeight: '44px', maxHeight: '100px', lineHeight: '1.5' },
    sendBtn: (disabled: boolean) => ({ padding: '10px 18px', backgroundColor: disabled ? '#2d2d3f' : '#4f46e5', color: disabled ? '#64748b' : '#fff', border: 'none', borderRadius: '10px', cursor: disabled ? 'not-allowed' as const : 'pointer' as const, fontSize: '14px', fontWeight: '600' as const }),
    approveBtn: { padding: '8px 20px', backgroundColor: '#16a34a', color: '#fff', border: 'none', borderRadius: '8px', cursor: 'pointer', fontSize: '13px', fontWeight: '600' as const },
    rejectBtn: { padding: '8px 16px', backgroundColor: '#1e1e2e', color: '#94a3b8', border: '1px solid #2d2d3f', borderRadius: '8px', cursor: 'pointer', fontSize: '13px' },
    refineBtn: { padding: '8px 16px', backgroundColor: '#4338ca', color: '#e2e8f0', border: 'none', borderRadius: '8px', cursor: 'pointer', fontSize: '13px' },
    actionRow: { display: 'flex', gap: '8px', marginTop: '8px', alignItems: 'center' },
    errorBox: { backgroundColor: '#2d1515', border: '1px solid #7f1d1d', color: '#fca5a5', padding: '8px 14px', borderRadius: '7px', fontSize: '13px', marginBottom: '10px' },
    loading: { color: '#64748b', fontSize: '13px', padding: '6px 0' },
    outputHeader: { fontSize: '12px', fontWeight: '600' as const, color: '#64748b', textTransform: 'uppercase' as const, letterSpacing: '0.05em', marginBottom: '12px' },
    hint: { fontSize: '11px', color: '#374151', marginTop: '6px', textAlign: 'center' as const },
  };

  if (isInitializing) {
    return <div style={{ ...s.outer, justifyContent: 'center', alignItems: 'center' }}><div style={{ color: '#64748b' }}>Initializing session...</div></div>;
  }

  return (
    <div style={s.outer}>
      {/* Header */}
      <div style={s.header}>
        <div>
          <div style={s.title}>AI Visual Learning Platform</div>
          <div style={s.subtitle}>Phase 4 — Resilience & Recovery</div>
        </div>
        <div style={{ display: 'flex', gap: '8px' }}>
          <button style={s.btn} onClick={handleToggleEvents}>{showEvents ? 'Hide Events' : 'Show Events'}</button>
          <button style={s.btn} onClick={handleNewSession}>New Session</button>
        </div>
      </div>

      <SessionState currentState={currentState} sessionId={sessionId} />

      {/* Body — chat | output */}
      <div style={s.body}>
        {/* Chat column */}
        <div style={s.chatCol}>
          <div style={s.msgs}>
            {error && <div style={s.errorBox}>{error}</div>}

            {messages.map(msg => <Message key={msg.id} message={msg} />)}

            {/* Inline job progress in chat */}
            {activeJobId && (
              <JobProgress jobId={activeJobId} event={jobProgress} />
            )}

            {isLoading && <ThinkingIndicator />}
            <div ref={messagesEndRef} />
          </div>

          {/* Events panel */}
          {showEvents && (
            <div style={{ backgroundColor: '#0d0d1f', borderTop: '1px solid #1e1e2e', maxHeight: '180px', overflowY: 'auto', padding: '10px 16px', flexShrink: 0 }}>
              <div style={{ fontSize: '11px', fontWeight: '600', color: '#64748b', marginBottom: '6px', textTransform: 'uppercase' }}>Session Events</div>
              {eventsLoading ? <div style={{ color: '#64748b', fontSize: '12px' }}>Loading...</div> : events.length === 0 ? (
                <div style={{ color: '#475569', fontSize: '12px' }}>No events yet.</div>
              ) : events.map(evt => (
                <div key={evt.eventId} style={{ padding: '4px 0', borderBottom: '1px solid #1e1e2e', fontSize: '11px', color: '#94a3b8', fontFamily: 'monospace' }}>
                  <span style={{ color: '#475569' }}>{new Date(evt.createdAt).toLocaleTimeString()}</span>
                  {' '}<span>{evt.previousState ?? '—'}</span>{' → '}<span style={{ color: '#a5b4fc' }}>{evt.newState}</span>
                  {evt.trigger && <span style={{ color: '#64748b' }}> [{evt.trigger}]</span>}
                </div>
              ))}
            </div>
          )}

          {/* Input area */}
          <div style={s.inputArea}>
            {/* Approval action buttons + output type selector */}
            {isApprovalPending && (
              <div style={{ marginBottom: 8 }}>
                {/* Output type selector */}
                <div style={{ background: '#0d0d1f', border: '1px solid #1e1e2e', borderRadius: 8, padding: '10px 14px', marginBottom: 8 }}>
                  <div style={{ fontSize: 11, fontWeight: 600, color: '#64748b', textTransform: 'uppercase' as const, letterSpacing: '0.05em', marginBottom: 8 }}>
                    Output Type <span style={{ color: '#374151', fontWeight: 400, textTransform: 'none' as const }}>(optional — leave blank to auto-select)</span>
                  </div>
                  <div style={{ display: 'flex', flexWrap: 'wrap' as const, gap: '6px' }}>
                    {[
                      { value: '', label: '🎲 Auto', desc: 'Let AI pick best type' },
                      { value: '3d_animation', label: '🧊 3D Scene', desc: 'Interactive 3D GLB' },
                      { value: 'video', label: '🎬 Video', desc: 'Animated MP4 slides' },
                      { value: '2d_animation', label: '📊 Diagram', desc: 'Mermaid chart' },
                      { value: 'narration', label: '🔊 Audio', desc: 'Text-to-speech MP3' },
                      { value: 'text', label: '📝 Text', desc: 'Structured explanation' },
                    ].map(opt => {
                      const active = selectedVizType === opt.value;
                      return (
                        <button
                          key={opt.value}
                          onClick={() => setSelectedVizType(opt.value)}
                          style={{
                            padding: '5px 10px', borderRadius: 6, fontSize: 12, cursor: 'pointer',
                            border: active ? '1px solid #4f46e5' : '1px solid #2d2d3f',
                            background: active ? '#312e81' : '#1e1e2e',
                            color: active ? '#c7d2fe' : '#94a3b8',
                            display: 'flex', flexDirection: 'column' as const, alignItems: 'flex-start',
                          }}
                        >
                          <span style={{ fontWeight: 600 }}>{opt.label}</span>
                          <span style={{ fontSize: 10, color: active ? '#818cf8' : '#475569' }}>{opt.desc}</span>
                        </button>
                      );
                    })}
                  </div>
                </div>
                <div style={s.actionRow}>
                  <button style={s.approveBtn} onClick={handleApprove} disabled={isLoading}>✓ Generate Visualization</button>
                  <button style={s.rejectBtn} onClick={handleReject} disabled={isLoading}>✗ Change Components</button>
                  <button style={{ ...s.rejectBtn, color: '#f87171' }} onClick={handleCancel} disabled={isLoading}>✕ Cancel</button>
                </div>
              </div>
            )}
            {/* Refine button after completion */}
            {(isCompleted || isFailed) && (
              <div style={s.actionRow}>
                <button style={s.refineBtn} onClick={handleRefine} disabled={isLoading}>↺ Refine / Try Again</button>
                <button style={s.rejectBtn} onClick={handleCancel} disabled={isLoading}>✕ Cancel Session</button>
              </div>
            )}
            {/* Phase 4 — Paused state */}
            {isPaused && (
              <div style={{ background: '#1c1917', border: '1px solid #78350f', borderRadius: 8, padding: '10px 14px', marginBottom: 8 }}>
                <div style={{ color: '#fbbf24', fontSize: 13, fontWeight: 600, marginBottom: 6 }}>⚠ Session Paused</div>
                <div style={{ color: '#92400e', fontSize: 12, marginBottom: 8 }}>Generation service is unavailable. Your session is saved.</div>
                <div style={s.actionRow}>
                  <button style={{ ...s.approveBtn, background: '#92400e' }} onClick={handleResume} disabled={isLoading}>▶ Resume</button>
                  <button style={s.refineBtn} onClick={handleClone} disabled={isLoading}>⎘ Clone Session</button>
                  <button style={{ ...s.rejectBtn, color: '#f87171' }} onClick={handleCancel} disabled={isLoading}>✕ Cancel</button>
                </div>
              </div>
            )}
            {/* Phase 4 — ApprovalExpired state */}
            {isApprovalExpired && (
              <div style={{ background: '#1c1917', border: '1px solid #7c3aed', borderRadius: 8, padding: '10px 14px', marginBottom: 8 }}>
                <div style={{ color: '#a78bfa', fontSize: 13, fontWeight: 600, marginBottom: 6 }}>⏰ Approval Expired</div>
                <div style={{ color: '#6d28d9', fontSize: 12, marginBottom: 8 }}>Your approval window expired. Resume to continue planning.</div>
                <div style={s.actionRow}>
                  <button style={{ ...s.approveBtn, background: '#7c3aed' }} onClick={handleResume} disabled={isLoading}>↩ Resume Planning</button>
                  <button style={s.refineBtn} onClick={handleClone} disabled={isLoading}>⎘ Clone Session</button>
                </div>
              </div>
            )}
            {/* Phase 4 — Cancelled state */}
            {isCancelled && (
              <div style={{ background: '#0f172a', border: '1px solid #334155', borderRadius: 8, padding: '10px 14px', marginBottom: 8 }}>
                <div style={{ color: '#64748b', fontSize: 13, fontWeight: 600, marginBottom: 6 }}>✕ Session Cancelled</div>
                <div style={s.actionRow}>
                  <button style={s.refineBtn} onClick={handleClone} disabled={isLoading}>⎘ Clone Session</button>
                  <button style={s.btn} onClick={handleNewSession} disabled={isLoading}>+ New Session</button>
                </div>
              </div>
            )}
            {/* Retrying banner */}
            {currentState === 'Retrying' && (
              <div style={{ color: '#f59e0b', fontSize: 12, padding: '4px 0', marginBottom: 4 }}>
                ↻ Retrying generation with backoff...
              </div>
            )}
            <div style={s.inputRow}>
              <textarea
                style={s.textarea}
                value={input}
                onChange={e => setInput(e.target.value)}
                onKeyDown={handleKeyDown}
                placeholder={
                  isGenerating ? 'Generation in progress...' :
                  currentState === 'Retrying' ? 'Retrying generation...' :
                  isPaused ? 'Session paused. Use Resume or Clone above.' :
                  isCancelled ? 'Session cancelled. Clone or start a new session.' :
                  isApprovalExpired ? 'Approval expired. Use Resume Planning above.' :
                  isApprovalPending ? 'Click "Generate" above, or type "no" to change components...' :
                  isCompleted ? 'Say "refine" to change, or start a New Session...' :
                  'Type your message... (Enter to send)'
                }
                disabled={isLoading || isGenerating || isPaused || isCancelled || isApprovalExpired}
                rows={1}
              />
              <button
                style={s.sendBtn(isLoading || !input.trim() || isGenerating)}
                onClick={handleSend}
                disabled={isLoading || !input.trim() || isGenerating}
              >
                {isLoading ? '...' : 'Send'}
              </button>
            </div>
            <div style={s.hint}>Enter to send • Shift+Enter for new line</div>
          </div>
        </div>

        {/* Output panel */}
        {hasOutput && (
          <div style={s.outputCol}>
            {/* Source strategy badge */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
              <div style={s.outputHeader}>
                {isMermaid ? '📊 Diagram Output' : '📄 Text Output'}
                {jobResult!.fallbackAttempt > 0 && <span style={{ color: '#fb923c', marginLeft: '8px' }}>(fallback)</span>}
              </div>
              <span style={{
                fontSize: 10, padding: '2px 8px', borderRadius: 20,
                background: hasKnowledgeBase ? '#1e3a5f' : '#1e2a1e',
                color: hasKnowledgeBase ? '#60a5fa' : '#4ade80',
              }}>
                {hasKnowledgeBase ? '🔍 knowledge base' : '🤖 ai generated'}
              </span>
            </div>

            {/* Fallback notice when user-selected type was overridden */}
            {jobResult!.fallbackAttempt > 0 && selectedVizType && jobResult!.outputType !== selectedVizType && (
              <div style={{ background: '#1c1917', border: '1px solid #78350f', borderRadius: 6, padding: '8px 12px', marginBottom: 12, fontSize: 12, color: '#fbbf24' }}>
                ⚠ Could not generate <strong>{selectedVizType}</strong> — showing fallback: <strong>{jobResult!.outputType}</strong>
              </div>
            )}

            {/* Output viewer — type-specific */}
            {jobResult!.outputType === 'glb' && jobResult!.outputUrl ? (
              <Suspense fallback={<div style={{ color: '#64748b', padding: 16 }}>Loading 3D viewer…</div>}>
                <GLBViewer url={jobResult!.outputUrl} />
              </Suspense>
            ) : jobResult!.outputType === 'video' && jobResult!.outputUrl ? (
              <VideoPlayer url={jobResult!.outputUrl} />
            ) : jobResult!.outputType === 'audio' && jobResult!.outputUrl ? (
              <AudioPlayer url={jobResult!.outputUrl} />
            ) : isMermaid ? (
              <MermaidDiagram code={jobResult!.outputContent!} />
            ) : (
              <div style={{ whiteSpace: 'pre-wrap', fontSize: '13px', color: '#94a3b8', lineHeight: 1.6 }}>
                {jobResult!.outputContent}
              </div>
            )}

            {/* Algorithm Animation toggle */}
            {topic && components.length > 0 && (
              <div style={{ marginTop: 16 }}>
                <button
                  onClick={() => setShowAnimation(p => !p)}
                  style={{ ...s.btn, width: '100%', textAlign: 'center', padding: '8px', color: '#a5b4fc', borderColor: '#4f6ef7' }}
                >
                  {showAnimation ? '▲ Hide' : '▼ Show'} Algorithm Animation
                </button>
                {showAnimation && (
                  <div style={{ marginTop: 12 }}>
                    <AlgorithmAnimation topic={topic} components={components} />
                  </div>
                )}
              </div>
            )}

            {/* Citations panel */}
            {hasKnowledgeBase && (
              <div style={{ marginTop: 16, background: '#0f172a', borderRadius: 8, padding: 12 }}>
                <div style={{ fontSize: 11, fontWeight: 600, color: '#64748b', textTransform: 'uppercase', letterSpacing: '0.05em', marginBottom: 8 }}>
                  Sources ({citations.length})
                </div>
                {citations.map((c, i) => (
                  <div key={c.chunkId} style={{ marginBottom: 8, padding: '6px 8px', background: '#1e293b', borderRadius: 4, borderLeft: '3px solid #4f6ef7' }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 4 }}>
                      <span style={{ fontSize: 11, fontWeight: 600, color: '#94a3b8' }}>[{i + 1}] {c.source}</span>
                      <span style={{ fontSize: 10, color: '#475569' }}>score: {c.score.toFixed(2)}</span>
                    </div>
                    <div style={{ fontSize: 11, color: '#64748b', lineHeight: 1.5 }}>{c.excerpt}</div>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
