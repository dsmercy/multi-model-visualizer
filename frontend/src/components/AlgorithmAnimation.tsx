import { useEffect, useRef, useState } from 'react';
import * as d3 from 'd3';

interface AnimationStep {
  label: string;
  description: string;
  highlight?: number[];
  values?: number[];
}

interface AlgorithmAnimationProps {
  topic: string;
  components: string[];
}

// Generate generic step-based animation from topic + components
function buildSteps(topic: string, components: string[]): AnimationStep[] {
  return components.map((comp, i) => ({
    label: `Step ${i + 1}: ${comp}`,
    description: `Processing ${comp} as part of ${topic}`,
    highlight: [i],
    values: components.map((_, j) => (j <= i ? 80 - j * 10 : 30 + j * 5)),
  }));
}

// Bubble sort animation — triggered when topic mentions sorting
function buildBubbleSortSteps(arr: number[]): AnimationStep[] {
  const steps: AnimationStep[] = [];
  const a = [...arr];

  steps.push({ label: 'Initial Array', description: 'Starting state of the array', values: [...a] });

  for (let i = 0; i < a.length - 1; i++) {
    for (let j = 0; j < a.length - i - 1; j++) {
      steps.push({
        label: `Compare [${j}] & [${j + 1}]`,
        description: `Comparing ${a[j]} and ${a[j + 1]}`,
        highlight: [j, j + 1],
        values: [...a],
      });
      if (a[j] > a[j + 1]) {
        [a[j], a[j + 1]] = [a[j + 1], a[j]];
        steps.push({
          label: `Swap [${j}] ↔ [${j + 1}]`,
          description: `Swapped ${a[j + 1]} and ${a[j]}`,
          highlight: [j, j + 1],
          values: [...a],
        });
      }
    }
    steps.push({
      label: `Pass ${i + 1} Complete`,
      description: `Element ${a[a.length - i - 1]} is in its final position`,
      highlight: Array.from({ length: i + 1 }, (_, k) => a.length - 1 - k),
      values: [...a],
    });
  }

  steps.push({ label: 'Sorted!', description: 'Array is fully sorted', values: [...a] });
  return steps;
}

const BAR_COLORS = {
  default: '#4f6ef7',
  highlight: '#f59e0b',
  sorted: '#22c55e',
};

export default function AlgorithmAnimation({ topic, components }: AlgorithmAnimationProps) {
  const svgRef = useRef<SVGSVGElement>(null);
  const [steps, setSteps] = useState<AnimationStep[]>([]);
  const [stepIdx, setStepIdx] = useState(0);
  const [playing, setPlaying] = useState(false);
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

  // Determine which animation to use
  useEffect(() => {
    const lowerTopic = topic.toLowerCase();
    if (lowerTopic.includes('sort') || lowerTopic.includes('bubble')) {
      const sampleData = [64, 34, 25, 12, 22, 11, 90];
      setSteps(buildBubbleSortSteps(sampleData));
    } else {
      setSteps(buildSteps(topic, components));
    }
    setStepIdx(0);
    setPlaying(false);
  }, [topic, components]);

  // D3 render on step change
  useEffect(() => {
    if (!svgRef.current || steps.length === 0) return;

    const step = steps[stepIdx];
    const values = step.values ?? components.map((_, i) => 50 + i * 8);
    const highlight = new Set(step.highlight ?? []);

    const width = 560;
    const height = 200;
    const margin = { top: 10, right: 10, bottom: 30, left: 10 };
    const innerW = width - margin.left - margin.right;
    const innerH = height - margin.top - margin.bottom;

    const svg = d3.select(svgRef.current);
    svg.attr('width', width).attr('height', height);

    let g = svg.select<SVGGElement>('g.inner');
    if (g.empty()) {
      g = svg.append('g').attr('class', 'inner')
        .attr('transform', `translate(${margin.left},${margin.top})`);
    }

    const xScale = d3.scaleBand()
      .domain(values.map((_, i) => String(i)))
      .range([0, innerW])
      .padding(0.1);

    const yScale = d3.scaleLinear()
      .domain([0, d3.max(values) ?? 100])
      .range([innerH, 0]);

    const bars = g.selectAll<SVGRectElement, number>('rect.bar')
      .data(values);

    bars.enter()
      .append('rect')
      .attr('class', 'bar')
      .merge(bars)
      .transition()
      .duration(300)
      .attr('x', (_, i) => xScale(String(i)) ?? 0)
      .attr('y', d => yScale(d))
      .attr('width', xScale.bandwidth())
      .attr('height', d => innerH - yScale(d))
      .attr('rx', 3)
      .attr('fill', (_, i) => highlight.has(i) ? BAR_COLORS.highlight : BAR_COLORS.default);

    bars.exit().remove();

    // Value labels
    const labels = g.selectAll<SVGTextElement, number>('text.val')
      .data(values);

    labels.enter()
      .append('text')
      .attr('class', 'val')
      .merge(labels)
      .transition()
      .duration(300)
      .attr('x', (_, i) => (xScale(String(i)) ?? 0) + xScale.bandwidth() / 2)
      .attr('y', d => yScale(d) - 4)
      .attr('text-anchor', 'middle')
      .attr('font-size', '11px')
      .attr('fill', '#e2e8f0')
      .text(d => String(d));

    labels.exit().remove();
  }, [steps, stepIdx, components]);

  // Auto-play
  useEffect(() => {
    if (playing) {
      intervalRef.current = setInterval(() => {
        setStepIdx(prev => {
          if (prev >= steps.length - 1) {
            setPlaying(false);
            return prev;
          }
          return prev + 1;
        });
      }, 800);
    } else {
      if (intervalRef.current) clearInterval(intervalRef.current);
    }
    return () => { if (intervalRef.current) clearInterval(intervalRef.current); };
  }, [playing, steps.length]);

  if (steps.length === 0) return null;

  const step = steps[stepIdx];

  return (
    <div style={{ background: '#1e293b', borderRadius: 8, padding: 16, color: '#e2e8f0' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
        <h3 style={{ margin: 0, fontSize: 14, color: '#94a3b8' }}>Algorithm Animation — {topic}</h3>
        <span style={{ fontSize: 12, color: '#64748b' }}>Step {stepIdx + 1} / {steps.length}</span>
      </div>

      <svg ref={svgRef} style={{ display: 'block', margin: '0 auto' }} />

      <div style={{ marginTop: 12, padding: '8px 12px', background: '#0f172a', borderRadius: 6, minHeight: 48 }}>
        <div style={{ fontSize: 13, fontWeight: 600, color: '#f8fafc' }}>{step.label}</div>
        <div style={{ fontSize: 12, color: '#94a3b8', marginTop: 4 }}>{step.description}</div>
      </div>

      <div style={{ display: 'flex', gap: 8, marginTop: 12 }}>
        <button
          onClick={() => setStepIdx(0)}
          disabled={stepIdx === 0}
          style={btnStyle}
        >⏮</button>
        <button
          onClick={() => setStepIdx(p => Math.max(0, p - 1))}
          disabled={stepIdx === 0}
          style={btnStyle}
        >◀</button>
        <button
          onClick={() => setPlaying(p => !p)}
          style={{ ...btnStyle, background: playing ? '#dc2626' : '#4f6ef7', minWidth: 72 }}
        >{playing ? '⏸ Pause' : '▶ Play'}</button>
        <button
          onClick={() => setStepIdx(p => Math.min(steps.length - 1, p + 1))}
          disabled={stepIdx >= steps.length - 1}
          style={btnStyle}
        >▶</button>
        <button
          onClick={() => setStepIdx(steps.length - 1)}
          disabled={stepIdx >= steps.length - 1}
          style={btnStyle}
        >⏭</button>
      </div>

      {/* Progress bar */}
      <div style={{ marginTop: 10, height: 4, background: '#1e3a5f', borderRadius: 2 }}>
        <div
          style={{
            height: '100%',
            width: `${((stepIdx + 1) / steps.length) * 100}%`,
            background: '#4f6ef7',
            borderRadius: 2,
            transition: 'width 0.3s',
          }}
        />
      </div>
    </div>
  );
}

const btnStyle: React.CSSProperties = {
  padding: '4px 10px',
  background: '#334155',
  border: 'none',
  borderRadius: 4,
  color: '#e2e8f0',
  cursor: 'pointer',
  fontSize: 12,
};
