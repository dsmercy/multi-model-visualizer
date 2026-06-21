import type { JobProgressEvent } from '../types';

interface Props {
  jobId: string;
  event: JobProgressEvent | null;
}

export function JobProgress({ jobId, event }: Props) {
  const status = event?.status ?? 'Queued';
  const progress = event?.progress ?? 0;

  const statusColors: Record<string, string> = {
    Queued: '#94a3b8',
    Processing: '#a5b4fc',
    Completed: '#4ade80',
    Failed: '#f87171',
    FallbackGeneration: '#fb923c',
  };

  const color = statusColors[status] ?? '#94a3b8';

  const containerStyle: React.CSSProperties = {
    backgroundColor: '#0d0d1f',
    border: '1px solid #2d2d3f',
    borderRadius: '10px',
    padding: '14px 16px',
    margin: '8px 0',
  };

  const labelRowStyle: React.CSSProperties = {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: '8px',
    fontSize: '13px',
  };

  const trackStyle: React.CSSProperties = {
    height: '6px',
    backgroundColor: '#1e1e2e',
    borderRadius: '4px',
    overflow: 'hidden',
  };

  const fillStyle: React.CSSProperties = {
    height: '100%',
    width: `${progress}%`,
    backgroundColor: color,
    borderRadius: '4px',
    transition: 'width 0.4s ease',
  };

  const idStyle: React.CSSProperties = {
    fontSize: '10px',
    color: '#374151',
    marginTop: '6px',
    fontFamily: 'monospace',
  };

  return (
    <div style={containerStyle}>
      <div style={labelRowStyle}>
        <span style={{ color }}>⬤ {status}</span>
        <span style={{ color: '#64748b' }}>{progress}%</span>
      </div>
      <div style={trackStyle}>
        <div style={fillStyle} />
      </div>
      <div style={idStyle}>Job: {jobId}</div>
    </div>
  );
}
