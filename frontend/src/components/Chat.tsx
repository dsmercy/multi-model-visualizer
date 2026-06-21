import { useState, useEffect, useRef, useCallback } from 'react';
import { createSession, sendMessage, getSessionEvents } from '../api/client';
import type { ChatMessage, SessionEvent, WorkflowState } from '../types';
import { Message } from './Message';
import { SessionState } from './SessionState';

let msgCounter = 0;
const genId = () => `msg-${++msgCounter}-${Date.now()}`;

export function Chat() {
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [currentState, setCurrentState] = useState<WorkflowState | string>('Created');
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [isInitializing, setIsInitializing] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showEvents, setShowEvents] = useState(false);
  const [events, setEvents] = useState<SessionEvent[]>([]);
  const [eventsLoading, setEventsLoading] = useState(false);
  const messagesEndRef = useRef<HTMLDivElement>(null);

  const scrollToBottom = useCallback(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, []);

  useEffect(() => {
    scrollToBottom();
  }, [messages, scrollToBottom]);

  useEffect(() => {
    const init = async () => {
      try {
        setIsInitializing(true);
        const session = await createSession();
        setSessionId(session.sessionId);
        setCurrentState(session.currentState as WorkflowState);
        setMessages([
          {
            id: genId(),
            role: 'assistant',
            content: 'Welcome to the AI Visual Learning Platform!\n\nI can help you understand complex topics through structured explanations and visualizations.\n\nWhat would you like to learn today? For example:\n• "Explain how TCP/IP networking works"\n• "Help me understand photosynthesis"\n• "Walk me through how a CPU processes instructions"',
            timestamp: new Date(),
          },
        ]);
        setError(null);
      } catch (err) {
        setError('Failed to connect to the backend. Please ensure the API server is running on port 5000.');
        console.error('Session initialization error:', err);
      } finally {
        setIsInitializing(false);
      }
    };
    init();
  }, []);

  const handleSend = async () => {
    if (!input.trim() || !sessionId || isLoading) return;

    const userMsg: ChatMessage = {
      id: genId(),
      role: 'user',
      content: input.trim(),
      timestamp: new Date(),
    };

    setMessages(prev => [...prev, userMsg]);
    const sentContent = input.trim();
    setInput('');
    setIsLoading(true);
    setError(null);

    try {
      const response = await sendMessage(sessionId, sentContent);
      setCurrentState(response.newState as WorkflowState);

      const assistantMsg: ChatMessage = {
        id: genId(),
        role: 'assistant',
        content: response.message,
        timestamp: new Date(),
      };
      setMessages(prev => [...prev, assistantMsg]);
    } catch (err) {
      const errMsg = err instanceof Error ? err.message : 'An unexpected error occurred.';
      setError(errMsg);
      const errAssistantMsg: ChatMessage = {
        id: genId(),
        role: 'assistant',
        content: `Sorry, I encountered an error: ${errMsg}\n\nPlease try again.`,
        timestamp: new Date(),
      };
      setMessages(prev => [...prev, errAssistantMsg]);
    } finally {
      setIsLoading(false);
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  };

  const handleToggleEvents = async () => {
    if (!showEvents && sessionId) {
      setEventsLoading(true);
      try {
        const data = await getSessionEvents(sessionId);
        setEvents(data);
      } catch (err) {
        console.error('Failed to load events:', err);
      } finally {
        setEventsLoading(false);
      }
    }
    setShowEvents(prev => !prev);
  };

  const handleNewSession = async () => {
    setIsInitializing(true);
    setMessages([]);
    setError(null);
    setShowEvents(false);
    setEvents([]);
    try {
      const session = await createSession();
      setSessionId(session.sessionId);
      setCurrentState(session.currentState as WorkflowState);
      setMessages([
        {
          id: genId(),
          role: 'assistant',
          content: 'New session started! What would you like to learn today?',
          timestamp: new Date(),
        },
      ]);
    } catch (err) {
      setError('Failed to create a new session.');
    } finally {
      setIsInitializing(false);
    }
  };

  const outerStyle: React.CSSProperties = {
    display: 'flex',
    flexDirection: 'column',
    height: '100vh',
    backgroundColor: '#0a0a14',
    color: '#e2e8f0',
    fontFamily: 'system-ui, -apple-system, sans-serif',
  };

  const headerStyle: React.CSSProperties = {
    backgroundColor: '#0d0d1f',
    borderBottom: '1px solid #1e1e2e',
    padding: '16px 20px',
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
  };

  const titleStyle: React.CSSProperties = {
    fontSize: '18px',
    fontWeight: '700',
    color: '#a5b4fc',
    letterSpacing: '-0.02em',
  };

  const subtitleStyle: React.CSSProperties = {
    fontSize: '12px',
    color: '#475569',
    marginTop: '2px',
  };

  const buttonStyle: React.CSSProperties = {
    padding: '6px 14px',
    borderRadius: '8px',
    border: '1px solid #2d2d3f',
    backgroundColor: '#1e1e2e',
    color: '#94a3b8',
    cursor: 'pointer',
    fontSize: '12px',
    transition: 'all 0.2s',
  };

  const messagesContainerStyle: React.CSSProperties = {
    flex: 1,
    overflowY: 'auto',
    padding: '20px',
    display: 'flex',
    flexDirection: 'column',
  };

  const inputAreaStyle: React.CSSProperties = {
    borderTop: '1px solid #1e1e2e',
    padding: '16px 20px',
    backgroundColor: '#0d0d1f',
  };

  const inputRowStyle: React.CSSProperties = {
    display: 'flex',
    gap: '12px',
    alignItems: 'flex-end',
  };

  const textareaStyle: React.CSSProperties = {
    flex: 1,
    backgroundColor: '#1e1e2e',
    border: '1px solid #2d2d3f',
    borderRadius: '12px',
    padding: '12px 16px',
    color: '#e2e8f0',
    fontSize: '14px',
    fontFamily: 'inherit',
    resize: 'none',
    outline: 'none',
    minHeight: '48px',
    maxHeight: '120px',
    lineHeight: '1.5',
  };

  const sendButtonStyle: React.CSSProperties = {
    padding: '12px 20px',
    backgroundColor: isLoading || !input.trim() ? '#2d2d3f' : '#4f46e5',
    color: isLoading || !input.trim() ? '#64748b' : '#ffffff',
    border: 'none',
    borderRadius: '12px',
    cursor: isLoading || !input.trim() ? 'not-allowed' : 'pointer',
    fontSize: '14px',
    fontWeight: '600',
    transition: 'all 0.2s',
    whiteSpace: 'nowrap',
  };

  const hintStyle: React.CSSProperties = {
    fontSize: '11px',
    color: '#374151',
    marginTop: '8px',
    textAlign: 'center',
  };

  const errorStyle: React.CSSProperties = {
    backgroundColor: '#2d1515',
    border: '1px solid #7f1d1d',
    color: '#fca5a5',
    padding: '10px 16px',
    borderRadius: '8px',
    fontSize: '13px',
    marginBottom: '12px',
  };

  const loadingStyle: React.CSSProperties = {
    display: 'flex',
    alignItems: 'center',
    gap: '8px',
    color: '#64748b',
    fontSize: '13px',
    padding: '8px 0',
  };

  const eventsStyle: React.CSSProperties = {
    backgroundColor: '#0d0d1f',
    borderTop: '1px solid #1e1e2e',
    maxHeight: '200px',
    overflowY: 'auto',
    padding: '12px 20px',
  };

  const eventItemStyle: React.CSSProperties = {
    padding: '6px 0',
    borderBottom: '1px solid #1e1e2e',
    fontSize: '12px',
    color: '#94a3b8',
    fontFamily: 'monospace',
  };

  if (isInitializing) {
    return (
      <div style={{ ...outerStyle, justifyContent: 'center', alignItems: 'center' }}>
        <div style={{ color: '#64748b', fontSize: '16px' }}>Initializing session...</div>
      </div>
    );
  }

  return (
    <div style={outerStyle}>
      <div style={headerStyle}>
        <div>
          <div style={titleStyle}>AI Visual Learning Platform</div>
          <div style={subtitleStyle}>Powered by Ollama + gemma3:4b</div>
        </div>
        <div style={{ display: 'flex', gap: '8px' }}>
          <button style={buttonStyle} onClick={handleToggleEvents}>
            {showEvents ? 'Hide Events' : 'Show Events'}
          </button>
          <button style={buttonStyle} onClick={handleNewSession}>
            New Session
          </button>
        </div>
      </div>

      <SessionState currentState={currentState} sessionId={sessionId} />

      <div style={messagesContainerStyle}>
        {error && <div style={errorStyle}>{error}</div>}

        {messages.map(msg => (
          <Message key={msg.id} message={msg} />
        ))}

        {isLoading && (
          <div style={loadingStyle}>
            <span style={{ animation: 'pulse 1.5s infinite' }}>&#9679;</span>
            <span>AI is thinking...</span>
          </div>
        )}

        <div ref={messagesEndRef} />
      </div>

      {showEvents && (
        <div style={eventsStyle}>
          <div style={{ fontSize: '11px', fontWeight: '600', color: '#64748b', marginBottom: '8px', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
            Session Events
          </div>
          {eventsLoading ? (
            <div style={{ color: '#64748b', fontSize: '12px' }}>Loading events...</div>
          ) : events.length === 0 ? (
            <div style={{ color: '#475569', fontSize: '12px' }}>No events yet.</div>
          ) : (
            events.map(evt => (
              <div key={evt.eventId} style={eventItemStyle}>
                <span style={{ color: '#475569' }}>{new Date(evt.createdAt).toLocaleTimeString()}</span>
                {' '}
                <span style={{ color: '#94a3b8' }}>{evt.previousState ?? '—'}</span>
                {' → '}
                <span style={{ color: '#a5b4fc' }}>{evt.newState}</span>
                {evt.trigger && <span style={{ color: '#64748b' }}> [{evt.trigger}]</span>}
              </div>
            ))
          )}
        </div>
      )}

      <div style={inputAreaStyle}>
        <div style={inputRowStyle}>
          <textarea
            style={textareaStyle}
            value={input}
            onChange={e => setInput(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder={currentState === 'Completed' ? 'Session completed. Start a new session above.' : 'Type your message... (Enter to send, Shift+Enter for new line)'}
            disabled={isLoading || currentState === 'Completed'}
            rows={1}
          />
          <button
            style={sendButtonStyle}
            onClick={handleSend}
            disabled={isLoading || !input.trim() || currentState === 'Completed'}
          >
            {isLoading ? 'Sending...' : 'Send'}
          </button>
        </div>
        <div style={hintStyle}>
          Press Enter to send • Shift+Enter for new line
        </div>
      </div>
    </div>
  );
}
