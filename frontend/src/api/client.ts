import type { CreateSessionResponse, SendMessageResponse, Session, SessionEvent } from '../types';

const BASE_URL = '/api';

async function handleResponse<T>(res: Response): Promise<T> {
  if (!res.ok) {
    let errorMsg = `HTTP ${res.status}`;
    try {
      const body = await res.json();
      errorMsg = body.error || errorMsg;
    } catch {
      // ignore parse errors
    }
    throw new Error(errorMsg);
  }
  return res.json() as Promise<T>;
}

export async function createSession(): Promise<CreateSessionResponse> {
  const res = await fetch(`${BASE_URL}/sessions`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
  });
  return handleResponse<CreateSessionResponse>(res);
}

export async function getSession(sessionId: string): Promise<Session> {
  const res = await fetch(`${BASE_URL}/sessions/${sessionId}`);
  return handleResponse<Session>(res);
}

export async function sendMessage(sessionId: string, content: string): Promise<SendMessageResponse> {
  const res = await fetch(`${BASE_URL}/sessions/${sessionId}/messages`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ content }),
  });
  return handleResponse<SendMessageResponse>(res);
}

export async function getSessionEvents(sessionId: string): Promise<SessionEvent[]> {
  const res = await fetch(`${BASE_URL}/sessions/${sessionId}/events`);
  return handleResponse<SessionEvent[]>(res);
}
