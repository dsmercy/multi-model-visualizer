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
}

export const WORKFLOW_STATES = [
  'Created',
  'IntentAnalyzed',
  'DomainClassified',
  'ConceptExplained',
  'ComponentSelectionPending',
  'VisualizationPlanned',
  'ApprovalPending',
  'Completed',
] as const;

export type WorkflowState = typeof WORKFLOW_STATES[number];
