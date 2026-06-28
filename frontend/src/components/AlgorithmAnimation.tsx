import { useEffect, useRef, useState } from 'react';
import * as d3 from 'd3';

// ── Types ────────────────────────────────────────────────────────────────────

type AnimationMode = 'sort' | 'graph';

interface SortStep {
  label: string;
  description: string;
  values: number[];
  highlight: number[];
}

interface GraphStep {
  label: string;
  description: string;
  activeNode: number;   // index into components
  visitedNodes: number[];
}

interface AlgorithmAnimationProps {
  topic: string;
  components: string[];
}

// ── Sorting step builders ────────────────────────────────────────────────────

function buildBubbleSortSteps(arr: number[]): SortStep[] {
  const steps: SortStep[] = [];
  const a = [...arr];
  steps.push({ label: 'Initial Array', description: 'Starting state', values: [...a], highlight: [] });
  for (let i = 0; i < a.length - 1; i++) {
    for (let j = 0; j < a.length - i - 1; j++) {
      steps.push({ label: `Compare [${j}] & [${j+1}]`, description: `Comparing ${a[j]} and ${a[j+1]}`, values: [...a], highlight: [j, j+1] });
      if (a[j] > a[j + 1]) {
        [a[j], a[j+1]] = [a[j+1], a[j]];
        steps.push({ label: `Swap [${j}] ↔ [${j+1}]`, description: `Swapped — larger element bubbles right`, values: [...a], highlight: [j, j+1] });
      }
    }
    steps.push({ label: `Pass ${i+1} complete`, description: `${a[a.length-i-1]} is in its final position`, values: [...a], highlight: Array.from({ length: i+1 }, (_, k) => a.length-1-k) });
  }
  steps.push({ label: 'Sorted!', description: 'All elements in order', values: [...a], highlight: [] });
  return steps;
}

function buildInsertionSortSteps(arr: number[]): SortStep[] {
  const steps: SortStep[] = [];
  const a = [...arr];
  steps.push({ label: 'Initial Array', description: 'Left portion grows as sorted section', values: [...a], highlight: [] });
  for (let i = 1; i < a.length; i++) {
    const key = a[i];
    steps.push({ label: `Pick key = ${key}`, description: `Element at [${i}] to insert into sorted section`, values: [...a], highlight: [i] });
    let j = i - 1;
    while (j >= 0 && a[j] > key) {
      steps.push({ label: `Shift ${a[j]} right`, description: `${a[j]} > ${key}, shift right`, values: [...a], highlight: [j, j+1] });
      a[j+1] = a[j];
      j--;
    }
    a[j+1] = key;
    steps.push({ label: `Insert ${key} at [${j+1}]`, description: `Key placed in correct position`, values: [...a], highlight: [j+1] });
    steps.push({ label: `Pass ${i} complete`, description: `Sorted: [${a.slice(0, i+1).join(', ')}]`, values: [...a], highlight: Array.from({ length: i+1 }, (_, k) => k) });
  }
  steps.push({ label: 'Sorted!', description: 'All elements inserted in order', values: [...a], highlight: [] });
  return steps;
}

function buildSelectionSortSteps(arr: number[]): SortStep[] {
  const steps: SortStep[] = [];
  const a = [...arr];
  steps.push({ label: 'Initial Array', description: 'Will find minimum each pass', values: [...a], highlight: [] });
  for (let i = 0; i < a.length - 1; i++) {
    let minIdx = i;
    steps.push({ label: `Pass ${i+1}: scan for minimum`, description: `Scanning unsorted section [${i}..${a.length-1}]`, values: [...a], highlight: [i] });
    for (let j = i + 1; j < a.length; j++) {
      steps.push({ label: `Check [${j}] = ${a[j]}`, description: `${a[j]} vs current min ${a[minIdx]}`, values: [...a], highlight: [j, minIdx] });
      if (a[j] < a[minIdx]) {
        minIdx = j;
        steps.push({ label: `New minimum: ${a[minIdx]}`, description: `Smaller element found at [${minIdx}]`, values: [...a], highlight: [minIdx] });
      }
    }
    if (minIdx !== i) {
      [a[i], a[minIdx]] = [a[minIdx], a[i]];
      steps.push({ label: `Swap [${i}] ↔ [${minIdx}]`, description: `Place minimum ${a[i]} at position ${i}`, values: [...a], highlight: [i, minIdx] });
    }
    steps.push({ label: `Pass ${i+1} complete`, description: `${a[i]} is in final position`, values: [...a], highlight: Array.from({ length: i+1 }, (_, k) => k) });
  }
  steps.push({ label: 'Sorted!', description: 'All minimums selected and placed', values: [...a], highlight: [] });
  return steps;
}

// ── Graph step builder (conceptual topics) ───────────────────────────────────

function buildGraphSteps(topic: string, components: string[]): GraphStep[] {
  const steps: GraphStep[] = [];
  steps.push({ label: `Overview: ${topic}`, description: `${components.length} key components — stepping through each`, activeNode: -1, visitedNodes: [] });
  const visited: number[] = [];
  for (let i = 0; i < components.length; i++) {
    steps.push({
      label: `Component: ${components[i]}`,
      description: `Exploring "${components[i]}" as part of ${topic}`,
      activeNode: i,
      visitedNodes: [...visited],
    });
    visited.push(i);
    steps.push({
      label: `${components[i]} — integrated`,
      description: `"${components[i]}" connected to ${visited.length > 1 ? `${visited.length - 1} prior components` : 'the system'}`,
      activeNode: -1,
      visitedNodes: [...visited],
    });
  }
  steps.push({ label: 'Complete', description: `All ${components.length} components of "${topic}" explored`, activeNode: -1, visitedNodes: components.map((_, i) => i) });
  return steps;
}

// ── Bar chart renderer (sorting) ─────────────────────────────────────────────

function renderBarChart(
  svg: d3.Selection<SVGSVGElement, unknown, null, undefined>,
  step: SortStep,
) {
  const width = 560, height = 220;
  const margin = { top: 10, right: 10, bottom: 30, left: 10 };
  const innerW = width - margin.left - margin.right;
  const innerH = height - margin.top - margin.bottom;
  const highlight = new Set(step.highlight);

  svg.attr('width', width).attr('height', height);

  let g = svg.select<SVGGElement>('g.inner');
  if (g.empty()) g = svg.append('g').attr('class', 'inner').attr('transform', `translate(${margin.left},${margin.top})`);
  // Clear graph nodes if switching from graph mode
  g.selectAll('circle.node,line.edge,text.node-label').remove();

  const xScale = d3.scaleBand().domain(step.values.map((_, i) => String(i))).range([0, innerW]).padding(0.1);
  const yScale = d3.scaleLinear().domain([0, d3.max(step.values) ?? 100]).range([innerH, 0]);

  const bars = g.selectAll<SVGRectElement, number>('rect.bar').data(step.values);
  bars.enter().append('rect').attr('class', 'bar').merge(bars)
    .transition().duration(300)
    .attr('x', (_, i) => xScale(String(i)) ?? 0)
    .attr('y', d => yScale(d))
    .attr('width', xScale.bandwidth())
    .attr('height', d => innerH - yScale(d))
    .attr('rx', 3)
    .attr('fill', (_, i) => highlight.has(i) ? '#f59e0b' : '#4f6ef7');
  bars.exit().remove();

  const labels = g.selectAll<SVGTextElement, number>('text.val').data(step.values);
  labels.enter().append('text').attr('class', 'val').merge(labels)
    .transition().duration(300)
    .attr('x', (_, i) => (xScale(String(i)) ?? 0) + xScale.bandwidth() / 2)
    .attr('y', d => yScale(d) - 4)
    .attr('text-anchor', 'middle').attr('font-size', '11px').attr('fill', '#e2e8f0')
    .text(d => String(d));
  labels.exit().remove();
}

// ── Node graph renderer (conceptual topics) ──────────────────────────────────

function renderGraph(
  svg: d3.Selection<SVGSVGElement, unknown, null, undefined>,
  step: GraphStep,
  components: string[],
) {
  const width = 560, height = 220;
  svg.attr('width', width).attr('height', height);

  let g = svg.select<SVGGElement>('g.inner');
  if (g.empty()) g = svg.append('g').attr('class', 'inner');
  // Clear bar chart elements if switching from sort mode
  g.selectAll('rect.bar,text.val').remove();

  const n = components.length;
  const cx = width / 2, cy = height / 2;
  const r = Math.min(cx, cy) - 40;

  // Positions: evenly spaced around a circle
  const positions = components.map((_, i) => ({
    x: n === 1 ? cx : cx + r * Math.cos((2 * Math.PI * i) / n - Math.PI / 2),
    y: n === 1 ? cy : cy + r * Math.sin((2 * Math.PI * i) / n - Math.PI / 2),
  }));

  const visitedSet = new Set(step.visitedNodes);

  // Edges between visited nodes
  const edgeData: [number, number][] = [];
  const visited = step.visitedNodes;
  for (let i = 1; i < visited.length; i++) edgeData.push([visited[i-1], visited[i]]);
  if (step.activeNode >= 0 && visited.length > 0) edgeData.push([visited[visited.length - 1], step.activeNode]);

  const edges = g.selectAll<SVGLineElement, [number, number]>('line.edge').data(edgeData, d => `${d[0]}-${d[1]}`);
  edges.enter().append('line').attr('class', 'edge')
    .attr('x1', d => positions[d[0]].x).attr('y1', d => positions[d[0]].y)
    .attr('x2', d => positions[d[1]].x).attr('y2', d => positions[d[1]].y)
    .attr('stroke', '#334155').attr('stroke-width', 1.5).attr('opacity', 0)
    .transition().duration(300).attr('opacity', 1);
  edges.exit().transition().duration(200).attr('opacity', 0).remove();

  // Nodes
  const nodeData = components.map((_, i) => i);
  const nodes = g.selectAll<SVGCircleElement, number>('circle.node').data(nodeData, d => String(d));
  nodes.enter().append('circle').attr('class', 'node')
    .attr('cx', i => positions[i].x).attr('cy', i => positions[i].y)
    .attr('r', 0)
    .transition().duration(300).attr('r', 22);
  g.selectAll<SVGCircleElement, number>('circle.node')
    .transition().duration(300)
    .attr('fill', i =>
      i === step.activeNode ? '#f59e0b' :
      visitedSet.has(i) ? '#22c55e' : '#1e293b')
    .attr('stroke', i =>
      i === step.activeNode ? '#fbbf24' :
      visitedSet.has(i) ? '#4ade80' : '#4f6ef7')
    .attr('stroke-width', i => (i === step.activeNode ? 3 : 1.5));

  // Labels
  const nodeLabels = g.selectAll<SVGTextElement, number>('text.node-label').data(nodeData, d => String(d));
  nodeLabels.enter().append('text').attr('class', 'node-label')
    .attr('x', i => positions[i].x).attr('y', i => positions[i].y)
    .attr('text-anchor', 'middle').attr('dominant-baseline', 'middle')
    .attr('font-size', '9px').attr('fill', '#e2e8f0').attr('pointer-events', 'none')
    .text(i => components[i].length > 12 ? components[i].slice(0, 11) + '…' : components[i]);
  g.selectAll<SVGTextElement, number>('text.node-label')
    .attr('fill', i => i === step.activeNode ? '#0f172a' : '#e2e8f0');
}

// ── Main component ───────────────────────────────────────────────────────────

export default function AlgorithmAnimation({ topic, components }: AlgorithmAnimationProps) {
  const svgRef = useRef<SVGSVGElement>(null);
  const [mode, setMode] = useState<AnimationMode>('graph');
  const [sortSteps, setSortSteps] = useState<SortStep[]>([]);
  const [graphSteps, setGraphSteps] = useState<GraphStep[]>([]);
  const [stepIdx, setStepIdx] = useState(0);
  const [playing, setPlaying] = useState(false);
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const totalSteps = mode === 'sort' ? sortSteps.length : graphSteps.length;

  useEffect(() => {
    const t = topic.toLowerCase();
    const sampleData = [64, 34, 25, 12, 22, 11, 90];
    if (t.includes('insertion')) {
      setMode('sort'); setSortSteps(buildInsertionSortSteps(sampleData));
    } else if (t.includes('selection')) {
      setMode('sort'); setSortSteps(buildSelectionSortSteps(sampleData));
    } else if (t.includes('bubble') || t.includes('sort')) {
      setMode('sort'); setSortSteps(buildBubbleSortSteps(sampleData));
    } else {
      setMode('graph'); setGraphSteps(buildGraphSteps(topic, components));
    }
    setStepIdx(0);
    setPlaying(false);
  }, [topic, components]);

  // D3 render on step change
  useEffect(() => {
    if (!svgRef.current) return;
    const svg = d3.select(svgRef.current);
    if (mode === 'sort' && sortSteps.length > 0) {
      renderBarChart(svg, sortSteps[Math.min(stepIdx, sortSteps.length - 1)]);
    } else if (mode === 'graph' && graphSteps.length > 0) {
      renderGraph(svg, graphSteps[Math.min(stepIdx, graphSteps.length - 1)], components);
    }
  }, [mode, sortSteps, graphSteps, stepIdx, components]);

  // Auto-play
  useEffect(() => {
    if (playing) {
      intervalRef.current = setInterval(() => {
        setStepIdx(prev => {
          if (prev >= totalSteps - 1) { setPlaying(false); return prev; }
          return prev + 1;
        });
      }, 800);
    } else {
      if (intervalRef.current) clearInterval(intervalRef.current);
    }
    return () => { if (intervalRef.current) clearInterval(intervalRef.current); };
  }, [playing, totalSteps]);

  if (totalSteps === 0) return null;

  const currentStep = mode === 'sort' ? sortSteps[stepIdx] : graphSteps[stepIdx];
  if (!currentStep) return null;

  return (
    <div style={{ background: '#1e293b', borderRadius: 8, padding: 16, color: '#e2e8f0' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
        <h3 style={{ margin: 0, fontSize: 14, color: '#94a3b8' }}>
          {mode === 'sort' ? 'Sorting Animation' : 'Concept Explorer'} — {topic}
        </h3>
        <span style={{ fontSize: 12, color: '#64748b' }}>Step {stepIdx + 1} / {totalSteps}</span>
      </div>

      <svg ref={svgRef} style={{ display: 'block', margin: '0 auto' }} />

      <div style={{ marginTop: 12, padding: '8px 12px', background: '#0f172a', borderRadius: 6, minHeight: 48 }}>
        <div style={{ fontSize: 13, fontWeight: 600, color: '#f8fafc' }}>{currentStep.label}</div>
        <div style={{ fontSize: 12, color: '#94a3b8', marginTop: 4 }}>{currentStep.description}</div>
      </div>

      {/* Legend for graph mode */}
      {mode === 'graph' && (
        <div style={{ display: 'flex', gap: 16, marginTop: 8, fontSize: 11, color: '#64748b' }}>
          <span><span style={{ color: '#4f6ef7' }}>●</span> unvisited</span>
          <span><span style={{ color: '#f59e0b' }}>●</span> active</span>
          <span><span style={{ color: '#22c55e' }}>●</span> visited</span>
        </div>
      )}

      <div style={{ display: 'flex', gap: 8, marginTop: 12 }}>
        <button onClick={() => setStepIdx(0)} disabled={stepIdx === 0} style={btnStyle}>⏮</button>
        <button onClick={() => setStepIdx(p => Math.max(0, p - 1))} disabled={stepIdx === 0} style={btnStyle}>◀</button>
        <button
          onClick={() => setPlaying(p => !p)}
          style={{ ...btnStyle, background: playing ? '#dc2626' : '#4f6ef7', minWidth: 72 }}
        >{playing ? '⏸ Pause' : '▶ Play'}</button>
        <button onClick={() => setStepIdx(p => Math.min(totalSteps - 1, p + 1))} disabled={stepIdx >= totalSteps - 1} style={btnStyle}>▶</button>
        <button onClick={() => setStepIdx(totalSteps - 1)} disabled={stepIdx >= totalSteps - 1} style={btnStyle}>⏭</button>
      </div>

      <div style={{ marginTop: 10, height: 4, background: '#1e3a5f', borderRadius: 2 }}>
        <div style={{
          height: '100%',
          width: `${((stepIdx + 1) / totalSteps) * 100}%`,
          background: '#4f6ef7',
          borderRadius: 2,
          transition: 'width 0.3s',
        }} />
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
