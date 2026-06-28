import { useState, useCallback, Component, type ReactNode } from 'react';
import { Chat } from './components/Chat';
import { SessionHistory } from './components/SessionHistory';

class ErrorBoundary extends Component<{ children: ReactNode }, { error: string | null }> {
  state = { error: null };
  static getDerivedStateFromError(e: Error) { return { error: e.message }; }
  render() {
    if (this.state.error) {
      return (
        <div style={{ padding: 32, color: '#f87171', fontFamily: 'monospace', background: '#020617', minHeight: '100vh' }}>
          <h2>App crashed</h2>
          <pre style={{ whiteSpace: 'pre-wrap' }}>{this.state.error}</pre>
        </div>
      );
    }
    return this.props.children;
  }
}

function App() {
  const [activeSessionId, setActiveSessionId] = useState<string | null>(null);
  const [historyRefresh, setHistoryRefresh] = useState(0);

  const handleSessionChange = useCallback((id: string | null) => {
    setActiveSessionId(id);
    // Refresh history list when a new session is created or changed
    setHistoryRefresh(n => n + 1);
  }, []);

  const handleSessionCloned = useCallback((newId: string) => {
    setActiveSessionId(newId);
    setHistoryRefresh(n => n + 1);
  }, []);

  return (
    <ErrorBoundary>
    <div style={{ display: 'flex', height: '100vh', overflow: 'hidden', background: '#020617' }}>
      <SessionHistory
        activeSessionId={activeSessionId}
        onSelectSession={setActiveSessionId}
        onSessionCloned={handleSessionCloned}
        refreshTrigger={historyRefresh}
      />
      <div style={{ flex: 1, overflow: 'hidden' }}>
        <Chat
          externalSessionId={activeSessionId}
          onSessionChange={handleSessionChange}
        />
      </div>
    </div>
    </ErrorBoundary>
  );
}

export default App;
