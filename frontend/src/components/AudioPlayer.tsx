interface Props {
  url: string;
}

const PYTHON_BASE = 'http://localhost:8000';

export function AudioPlayer({ url }: Props) {
  const fullUrl = url.startsWith('http') ? url : `${PYTHON_BASE}${url}`;

  return (
    <div style={s.wrapper}>
      <div style={s.label}>Narration Audio</div>
      <div style={s.icon}>🔊</div>
      <audio
        src={fullUrl}
        controls
        style={s.audio}
        onError={() => console.error('Audio load error:', fullUrl)}
      >
        Your browser does not support audio playback.
      </audio>
      <div style={s.hint}>
        <a href={fullUrl} download style={s.link}>⬇ Download MP3</a>
      </div>
    </div>
  );
}

const s: Record<string, React.CSSProperties> = {
  wrapper: { display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 10, padding: 16, background: '#0f172a', borderRadius: 8 },
  label: { color: '#94a3b8', fontSize: 11, fontWeight: 600, letterSpacing: '0.05em', textTransform: 'uppercase', alignSelf: 'flex-start' },
  icon: { fontSize: 40 },
  audio: { width: '100%' },
  hint: { display: 'flex', justifyContent: 'flex-end', width: '100%' },
  link: { color: '#60a5fa', fontSize: 12, textDecoration: 'none' },
};
