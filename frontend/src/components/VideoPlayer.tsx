interface Props {
  url: string;
}

const PYTHON_BASE = 'http://localhost:8000';

export function VideoPlayer({ url }: Props) {
  const fullUrl = url.startsWith('http') ? url : `${PYTHON_BASE}${url}`;

  return (
    <div style={s.wrapper}>
      <div style={s.label}>Video Output</div>
      <video
        src={fullUrl}
        controls
        style={s.video}
        onError={() => console.error('Video load error:', fullUrl)}
      >
        Your browser does not support video playback.
      </video>
      <div style={s.hint}>
        <a href={fullUrl} download style={s.link}>⬇ Download MP4</a>
      </div>
    </div>
  );
}

const s: Record<string, React.CSSProperties> = {
  wrapper: { display: 'flex', flexDirection: 'column', gap: 8, padding: 12, background: '#0f172a', borderRadius: 8 },
  label: { color: '#94a3b8', fontSize: 11, fontWeight: 600, letterSpacing: '0.05em', textTransform: 'uppercase' },
  video: { width: '100%', maxHeight: 400, borderRadius: 6, background: '#000' },
  hint: { display: 'flex', justifyContent: 'flex-end' },
  link: { color: '#60a5fa', fontSize: 12, textDecoration: 'none' },
};
