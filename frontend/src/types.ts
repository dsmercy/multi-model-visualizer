export interface Session {
  sessionId: string;
  userId: string;
  currentState: string;
  topic: string | null;
  intent: string | null;
  domain: string | null;
  selectedComponents: string | null;
  difficultyLevel: string | null;
  visualizationType: string | null;
  visualizationPlan: string | null;
  explanation: string | null;
  finalOutput: string | null;
  createdDate: string;
  updatedDate: string;
}

export interface CreateSessionResponse {
  sessionId: string;
  currentState: string;
  createdAt: string;
}

export interface SendMessageResponse {
  message: string;
  newState: string;
  sessionId: string;
}

export interface ApproveResponse {
  jobId: string;
  status: string;
  sessionId: string;
}

export interface JobProgressEvent {
  jobId: string;
  status: string;
  progress: number;
  outputType?: string;
  outputUrl?: string;
  errorCode?: string;
}

export interface JobResult {
  jobId: string;
  sessionId: string;
  status: string;
  outputType: string | null;
  outputUrl: string | null;
  outputContent: string | null;
  fallbackAttempt: number;
  progress: number;
  createdAt: string;
  updatedAt: string;
}

export interface SessionEvent {
  eventId: string;
  sessionId: string;
  previousState: string | null;
  newState: string;
  trigger: string | null;
  eventPayload: string | null;
  createdAt: string;
}

export interface ChatMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  timestamp: Date;
  jobId?: string;
  jobResult?: JobResult;
}

export interface CitationDto {
  chunkId: string;
  source: string;
  domain: string;
  topic: string | null;
  score: number;
  excerpt: string;
}

export interface SessionCitationsResponse {
  sessionId: string;
  componentSourceStrategy: string;
  citations: CitationDto[];
}

export const WORKFLOW_STATES = [
  'Created',
  'IntentAnalyzed',
  'DomainClassified',
  'KnowledgeRetrieved',
  'ConceptExplained',
  'ComponentSelectionPending',
  'VisualizationPlanned',
  'ApprovalPending',
  'GenerationQueued',
  'Generating',
  'Generated',
  'Failed',
  'FallbackGeneration',
  'Completed',
] as const;

export type WorkflowState = typeof WORKFLOW_STATES[number];

export const GENERATION_STATES: WorkflowState[] = ['GenerationQueued', 'Generating', 'Generated', 'FallbackGeneration'];
export const TERMINAL_STATES: WorkflowState[] = ['Completed', 'Failed'];
