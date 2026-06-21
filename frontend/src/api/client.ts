import type {
  CreateSessionResponse, SendMessageResponse, Session,
  SessionEvent, ApproveResponse, JobResult, JobProgressEvent,
} from '../types';

const BASE = '/api';

async function req<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(BASE + path, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({})) as Record<string, string>;
    throw new Error(body['error'] ?? `HTTP ${res.status}`);
  }
  return res.json() as Promise<T>;
}

export const createSession = () =>
  req<CreateSessionResponse>('/sessions', { method: 'POST' });

export const getSession = (id: string) =>
  req<Session>(`/sessions/${id}`);

export const sendMessage = (id: string, content: string) =>
  req<SendMessageResponse>(`/sessions/${id}/messages`, {
    method: 'POST',
    body: JSON.stringify({ content }),
  });

export const approveSession = (id: string) =>
  req<ApproveResponse>(`/sessions/${id}/approve`, { method: 'POST' });

export const rejectSession = (id: string) =>
  req<SendMessageResponse>(`/sessions/${id}/reject`, { method: 'POST' });

export const refineSession = (id: string) =>
  req<SendMessageResponse>(`/sessions/${id}/refine`, { method: 'POST' });

export const getSessionEvents = (id: string) =>
  req<SessionEvent[]>(`/sessions/${id}/events`);

export const getJobResult = (jobId: string) =>
  req<JobResult>(`/jobs/${jobId}/result`);

export function subscribeJobProgress(
  jobId: string,
  onEvent: (evt: JobProgressEvent) => void,
  onDone: () => void,
  signal: AbortSignal,
) {
  const es = new EventSource(`${BASE}/jobs/${jobId}/progress`);

  es.onmessage = (e) => {
    try {
      const evt = JSON.parse(e.data as string) as JobProgressEvent;
      onEvent(evt);
      if (evt.status === 'Completed' || evt.status === 'Failed') {
        es.close();
        onDone();
      }
    } catch { /* ignore */ }
  };

  es.onerror = () => { es.close(); onDone(); };
  signal.addEventListener('abort', () => es.close());
  return () => es.close();
}
