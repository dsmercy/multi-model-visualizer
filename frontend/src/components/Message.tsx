import type { ChatMessage } from '../types';

interface MessageProps {
  message: ChatMessage;
}

function formatContent(content: string): string {
  return content;
}

export function Message({ message }: MessageProps) {
  const isUser = message.role === 'user';
  const timeStr = message.timestamp.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });

  const containerStyle: React.CSSProperties = {
    display: 'flex',
    flexDirection: 'column',
    alignItems: isUser ? 'flex-end' : 'flex-start',
    marginBottom: '16px',
    maxWidth: '100%',
  };

  const bubbleStyle: React.CSSProperties = {
    maxWidth: '75%',
    padding: '12px 16px',
    borderRadius: isUser ? '18px 18px 4px 18px' : '18px 18px 18px 4px',
    backgroundColor: isUser ? '#4f46e5' : '#1e1e2e',
    color: isUser ? '#ffffff' : '#e2e8f0',
    fontSize: '14px',
    lineHeight: '1.6',
    whiteSpace: 'pre-wrap',
    wordBreak: 'break-word',
    border: isUser ? 'none' : '1px solid #2d2d3f',
    fontFamily: 'system-ui, -apple-system, sans-serif',
  };

  const metaStyle: React.CSSProperties = {
    fontSize: '11px',
    color: '#64748b',
    marginTop: '4px',
    paddingLeft: isUser ? '0' : '4px',
    paddingRight: isUser ? '4px' : '0',
  };

  const labelStyle: React.CSSProperties = {
    fontSize: '11px',
    fontWeight: '600',
    color: isUser ? '#818cf8' : '#94a3b8',
    marginBottom: '4px',
    textTransform: 'uppercase',
    letterSpacing: '0.05em',
  };

  return (
    <div style={containerStyle}>
      <div style={labelStyle}>{isUser ? 'You' : 'AI Assistant'}</div>
      <div style={bubbleStyle}>{formatContent(message.content)}</div>
      <div style={metaStyle}>{timeStr}</div>
    </div>
  );
}
