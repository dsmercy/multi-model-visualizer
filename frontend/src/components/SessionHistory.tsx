import { useEffect, useState, useCallback } from 'react';
import type { SessionSummary } from '../types';
import { getSessions, cloneSession } from '../api/client';

interface Props {
  activeSessionId: string | null;
  onSelectSession: (id: string) => void;
  onSessionCloned: (newId: string) => void;
  refreshTrigger: number;
}

const STATE_COLORS: Record<string, string> = {
  Completed: '#4ade80',
  Reviewed: '#4ade80',
  Generated: '#86efac',
  Cancelled: '#64748b',
  Failed: '#f87171',
  RetryExhausted: '#f87171',
  Paused: '#fbbf24',
  ApprovalExpired: '#a78bfa',
  Retrying: '#60a5fa',
  GenerationQueued: '#60a5fa',
  Generating: '#60a5fa',
};

function stateColor(s: string) {
  return STATE_COLORS[s] ?? '#94a3b8';
}

function relativeTime(iso: string) {
  const diff = Date.now() - new Date(iso).getTime();
  const min = Math.floor(diff / 60000);
  if (min < 1) return 'just now';
  if (min < 60) return `${min}m ago`;
  const hr = Math.floor(min / 60);
  if (hr < 24) return `${hr}h ago`;
  return `${Math.floor(hr / 24)}d ago`;
}

export function SessionHistory({ activeSessionId, onSelectSession, onSessionCloned, refreshTrigger }: Props) {
  const [sessions, setSessions] = useState<SessionSummary[]>([]);
  const [cloningId, setCloningId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const data = await getSessions(20);
      setSessions(data);
    } catch {
      // silently ignore — sidebar is non-critical
    }
  }, []);

  useEffect(() => { void load(); }, [load, refreshTrigger]);

  const handleClone = async (e: React.MouseEvent, sessionId: string) => {
    e.stopPropagation();
    setCloningId(sessionId);
    setError(null);
    try {
      const resp = await cloneSession(sessionId);
      onSessionCloned(resp.newSessionId);
      void load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Clone failed');
    } finally {
      setCloningId(null);
    }
  };

  return (
    <aside style={s.sidebar}>
      <div style={s.header}>
        <span style={s.title}>Sessions</span>
        <button style={s.refreshBtn} onClick={() => void load()} title="Refresh">↻</button>
      </div>

      {error && <div style={s.error}>{error}</div>}

      <div style={s.list}>
        {sessions.length === 0 && (
          <div style={s.empty}>No sessions yet</div>
        )}
        {sessions.map(session => (
          <div
            key={session.sessionId}
            style={{
              ...s.item,
              ...(session.sessionId === activeSessionId ? s.itemActive : {}),
            }}
            onClick={() => onSelectSession(session.sessionId)}
          >
            <div style={s.itemTop}>
              <span style={{ ...s.dot, background: stateColor(session.currentState) }} />
              <span style={s.topic}>{session.topic ?? 'New session'}</span>
            </div>
            <div style={s.itemMeta}>
              <span style={s.state}>{session.currentState}</span>
              <span style={s.time}>{relativeTime(session.updatedDate)}</span>
            </div>
            {session.domain && <div style={s.domain}>{session.domain}</div>}
            <div style={s.itemActions}>
              <button
                style={s.cloneBtn}
                onClick={(e) => void handleClone(e, session.sessionId)}
                disabled={cloningId === session.sessionId}
                title="Clone this session"
              >
                {cloningId === session.sessionId ? '...' : '⎘ Clone'}
              </button>
            </div>
          </div>
        ))}
      </div>
    </aside>
  );
}

const s: Record<string, React.CSSProperties> = {
  sidebar: {
    width: 220,
    minWidth: 220,
    background: '#0f172a',
    borderRight: '1px solid #1e293b',
    display: 'flex',
    flexDirection: 'column',
    overflow: 'hidden',
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    padding: '12px 12px 8px',
    borderBottom: '1px solid #1e293b',
  },
  title: { color: '#94a3b8', fontSize: 12, fontWeight: 600, letterSpacing: '0.05em', textTransform: 'uppercase' },
  refreshBtn: { background: 'none', border: 'none', color: '#475569', cursor: 'pointer', fontSize: 14, padding: '0 4px' },
  list: { flex: 1, overflowY: 'auto', padding: '6px 0' },
  empty: { color: '#334155', fontSize: 12, padding: '12px', textAlign: 'center' },
  item: {
    padding: '8px 12px',
    cursor: 'pointer',
    borderBottom: '1px solid #0f172a',
    background: '#0f172a',
    transition: 'background 0.1s',
  },
  itemActive: { background: '#1e293b' },
  itemTop: { display: 'flex', alignItems: 'center', gap: 6, marginBottom: 3 },
  dot: { width: 7, height: 7, borderRadius: '50%', flexShrink: 0 },
  topic: { color: '#e2e8f0', fontSize: 12, fontWeight: 500, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' },
  itemMeta: { display: 'flex', justifyContent: 'space-between', alignItems: 'center' },
  state: { color: '#475569', fontSize: 10 },
  time: { color: '#334155', fontSize: 10 },
  domain: { color: '#334155', fontSize: 10, marginTop: 1 },
  itemActions: { marginTop: 5 },
  cloneBtn: {
    background: 'none', border: '1px solid #1e293b', borderRadius: 4,
    color: '#475569', cursor: 'pointer', fontSize: 10, padding: '2px 6px',
  },
  error: { color: '#f87171', fontSize: 11, padding: '4px 12px' },
};
