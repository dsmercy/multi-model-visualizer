import { useEffect, useRef, useState } from 'react';
import mermaid from 'mermaid';

mermaid.initialize({
  startOnLoad: false,
  theme: 'dark',
  themeVariables: {
    background: '#0d0d1f',
    primaryColor: '#4f46e5',
    primaryTextColor: '#e2e8f0',
    primaryBorderColor: '#6366f1',
    lineColor: '#94a3b8',
    secondaryColor: '#1e1e2e',
    tertiaryColor: '#1e1e2e',
    edgeLabelBackground: '#1e1e2e',
    nodeTextColor: '#e2e8f0',
  },
  fontFamily: 'system-ui, -apple-system, sans-serif',
  securityLevel: 'loose',
});

let diagramCounter = 0;

interface Props {
  code: string;
}

export function MermaidDiagram({ code }: Props) {
  const containerRef = useRef<HTMLDivElement>(null);
  const [error, setError] = useState<string | null>(null);
  const [scale, setScale] = useState(1);

  useEffect(() => {
    if (!containerRef.current || !code.trim()) return;
    const id = `mermaid-${++diagramCounter}`;
    setError(null);

    mermaid.render(id, code.trim()).then(({ svg }) => {
      if (containerRef.current) {
        containerRef.current.innerHTML = svg;
        // Make SVG responsive
        const svgEl = containerRef.current.querySelector('svg');
        if (svgEl) {
          svgEl.style.width = '100%';
          svgEl.style.height = 'auto';
          svgEl.style.maxWidth = '100%';
        }
      }
    }).catch((err: unknown) => {
      setError(err instanceof Error ? err.message : 'Failed to render diagram');
      if (containerRef.current) containerRef.current.innerHTML = '';
    });
  }, [code]);

  const containerStyle: React.CSSProperties = {
    backgroundColor: '#0d0d1f',
    border: '1px solid #2d2d3f',
    borderRadius: '12px',
    padding: '20px',
    position: 'relative',
    overflow: 'auto',
  };

  const controlsStyle: React.CSSProperties = {
    display: 'flex',
    gap: '8px',
    marginBottom: '12px',
    alignItems: 'center',
  };

  const btnStyle: React.CSSProperties = {
    padding: '4px 10px',
    fontSize: '12px',
    backgroundColor: '#1e1e2e',
    border: '1px solid #2d2d3f',
    borderRadius: '6px',
    color: '#94a3b8',
    cursor: 'pointer',
  };

  const labelStyle: React.CSSProperties = {
    fontSize: '11px',
    color: '#475569',
    marginLeft: 'auto',
  };

  if (error) {
    return (
      <div style={{ ...containerStyle, color: '#fca5a5' }}>
        <div style={{ fontSize: '13px', marginBottom: '8px' }}>Diagram render error</div>
        <pre style={{ fontSize: '11px', color: '#64748b', whiteSpace: 'pre-wrap' }}>{code}</pre>
      </div>
    );
  }

  return (
    <div style={containerStyle}>
      <div style={controlsStyle}>
        <button style={btnStyle} onClick={() => setScale(s => Math.min(s + 0.2, 3))}>+ Zoom In</button>
        <button style={btnStyle} onClick={() => setScale(s => Math.max(s - 0.2, 0.3))}>− Zoom Out</button>
        <button style={btnStyle} onClick={() => setScale(1)}>Reset</button>
        <span style={labelStyle}>{Math.round(scale * 100)}%</span>
      </div>
      <div
        ref={containerRef}
        style={{ transform: `scale(${scale})`, transformOrigin: 'top left', transition: 'transform 0.2s' }}
      />
    </div>
  );
}
