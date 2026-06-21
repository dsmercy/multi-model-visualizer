import { WORKFLOW_STATES } from '../types';
import type { WorkflowState } from '../types';

interface SessionStateProps {
  currentState: WorkflowState | string;
  sessionId: string | null;
}

const STATE_LABELS: Record<string, string> = {
  Created: 'Created',
  IntentAnalyzed: 'Intent Analyzed',
  DomainClassified: 'Domain Classified',
  ConceptExplained: 'Concept Explained',
  ComponentSelectionPending: 'Select Components',
  VisualizationPlanned: 'Plan Ready',
  ApprovalPending: 'Awaiting Approval',
  Completed: 'Completed',
};

const STATE_COLORS: Record<string, { bg: string; text: string }> = {
  Created: { bg: '#1e293b', text: '#94a3b8' },
  IntentAnalyzed: { bg: '#1e3a5f', text: '#60a5fa' },
  DomainClassified: { bg: '#1e3a5f', text: '#34d399' },
  ConceptExplained: { bg: '#2d1b69', text: '#a78bfa' },
  ComponentSelectionPending: { bg: '#1a2e1a', text: '#4ade80' },
  VisualizationPlanned: { bg: '#1a2535', text: '#38bdf8' },
  ApprovalPending: { bg: '#2d2000', text: '#fbbf24' },
  Completed: { bg: '#0f2d0f', text: '#22c55e' },
};

export function SessionState({ currentState, sessionId }: SessionStateProps) {
  const currentIndex = WORKFLOW_STATES.indexOf(currentState as WorkflowState);
  const colors = STATE_COLORS[currentState] ?? { bg: '#1e293b', text: '#94a3b8' };

  const containerStyle: React.CSSProperties = {
    backgroundColor: '#0f0f1a',
    borderBottom: '1px solid #1e1e2e',
    padding: '12px 20px',
    display: 'flex',
    alignItems: 'center',
    gap: '16px',
    flexWrap: 'wrap',
  };

  const badgeStyle: React.CSSProperties = {
    backgroundColor: colors.bg,
    color: colors.text,
    padding: '4px 12px',
    borderRadius: '20px',
    fontSize: '12px',
    fontWeight: '600',
    border: `1px solid ${colors.text}40`,
    whiteSpace: 'nowrap',
  };

  const progressStyle: React.CSSProperties = {
    display: 'flex',
    alignItems: 'center',
    gap: '4px',
    flex: 1,
    minWidth: '200px',
  };

  const stepStyle = (index: number): React.CSSProperties => ({
    width: '8px',
    height: '8px',
    borderRadius: '50%',
    backgroundColor: index <= currentIndex
      ? (index === currentIndex ? colors.text : '#4f46e5')
      : '#2d2d3f',
    transition: 'background-color 0.3s ease',
  });

  const sessionIdStyle: React.CSSProperties = {
    fontSize: '11px',
    color: '#475569',
    fontFamily: 'monospace',
  };

  return (
    <div style={containerStyle}>
      <div>
        <div style={{ fontSize: '10px', color: '#64748b', marginBottom: '4px', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
          Current State
        </div>
        <span style={badgeStyle}>
          {STATE_LABELS[currentState] ?? currentState}
        </span>
      </div>

      <div style={{ flex: 1 }}>
        <div style={{ fontSize: '10px', color: '#64748b', marginBottom: '6px', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
          Progress ({currentIndex >= 0 ? currentIndex + 1 : 1}/{WORKFLOW_STATES.length})
        </div>
        <div style={progressStyle}>
          {WORKFLOW_STATES.map((_, index) => (
            <div key={index} style={stepStyle(index)} title={STATE_LABELS[WORKFLOW_STATES[index]]} />
          ))}
        </div>
      </div>

      {sessionId && (
        <div style={sessionIdStyle}>
          Session: {sessionId.slice(0, 8)}...
        </div>
      )}
    </div>
  );
}
