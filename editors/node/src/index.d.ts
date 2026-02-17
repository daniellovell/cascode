export interface CascodeNative {
  createSession(optionsJson?: string): number;
  destroySession(session: number): void;
  call(session: number, method: string, requestJson: string): string;
  lastErrorJson(session: number): string | null;
  apiVersion(): string;
  schemaVersion(): string;
}

export const native: CascodeNative;

export function createSession(optionsJson?: string): number;
export function destroySession(session: number): void;
export function call(session: number, method: string, requestJson: string): string;
export function lastErrorJson(session: number): string | null;
export function apiVersion(): string;
export function schemaVersion(): string;

export function open(native: CascodeNative, session: number, req: unknown): unknown;
export function updateText(native: CascodeNative, session: number, req: unknown): unknown;
export function close(native: CascodeNative, session: number, req: unknown): unknown;
export function render(native: CascodeNative, session: number, req: unknown): unknown;
export function applyOps(native: CascodeNative, session: number, req: unknown): unknown;
export function erc(native: CascodeNative, session: number, req: unknown): unknown;
export function emit(native: CascodeNative, session: number, req: unknown): unknown;
export function verify(native: CascodeNative, session: number, req: unknown): unknown;
export function jobStart(native: CascodeNative, session: number, req: unknown): unknown;
export function jobPoll(native: CascodeNative, session: number, req: unknown): unknown;
export function jobCancel(native: CascodeNative, session: number, req: unknown): unknown;
