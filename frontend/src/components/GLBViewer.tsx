import { Suspense } from 'react';
import { Canvas } from '@react-three/fiber';
import { OrbitControls, useGLTF, Environment, Center } from '@react-three/drei';

const PYTHON_BASE = 'http://localhost:8000';

interface ModelProps { url: string }

function Model({ url }: ModelProps) {
  const { scene } = useGLTF(url);
  return (
    <Center>
      <primitive object={scene} />
    </Center>
  );
}

interface Props { url: string }

export function GLBViewer({ url }: Props) {
  const fullUrl = url.startsWith('http') ? url : `${PYTHON_BASE}${url}`;

  return (
    <div style={s.wrapper}>
      <div style={s.label}>3D Scene — drag to orbit, scroll to zoom</div>
      <div style={s.canvasWrap}>
        <Canvas camera={{ position: [0, 2, 8], fov: 45 }}>
          <ambientLight intensity={0.6} />
          <directionalLight position={[5, 5, 5]} intensity={1} />
          <Suspense fallback={null}>
            <Model url={fullUrl} />
            <Environment preset="city" />
          </Suspense>
          <OrbitControls autoRotate autoRotateSpeed={0.5} />
        </Canvas>
      </div>
      <div style={s.hint}>
        <a href={fullUrl} download style={s.link}>⬇ Download GLB</a>
      </div>
    </div>
  );
}

const s: Record<string, React.CSSProperties> = {
  wrapper: { display: 'flex', flexDirection: 'column', gap: 8, padding: 12, background: '#0f172a', borderRadius: 8 },
  label: { color: '#94a3b8', fontSize: 11, fontWeight: 600, letterSpacing: '0.05em', textTransform: 'uppercase' },
  canvasWrap: { width: '100%', height: 400, borderRadius: 6, overflow: 'hidden', background: '#020617' },
  hint: { display: 'flex', justifyContent: 'flex-end' },
  link: { color: '#60a5fa', fontSize: 12, textDecoration: 'none' },
};
