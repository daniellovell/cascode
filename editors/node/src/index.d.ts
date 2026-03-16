export interface CascodeNative {
  createSession(optionsJson?: string): number;
  destroySession(session: number): void;
  call(session: number, method: string, requestJson: string): string;
  lastErrorJson(session: number): string | null;
  apiVersion(): string;
  schemaVersion(): string;
}

export const native: CascodeNative;

/** Absolute path to the bundled standard library (lib/std) directory. */
export const stdlibPath: string;

export function createSession(optionsJson?: string): number;
export function destroySession(session: number): void;
export function call(session: number, method: string, requestJson: string): string;
export function lastErrorJson(session: number): string | null;
export function apiVersion(): string;
export function schemaVersion(): string;

export type NativeMethodCall = <Req extends object, Res>(
  native: CascodeNative,
  session: number,
  req: Req
) => Res;

export type RenderSourceMode = "auto" | "manual";

export interface PointValue {
  x: number;
  y: number;
}

export interface SegmentValue {
  from: PointValue;
  to: PointValue;
}

export interface BboxValue {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface DiagnosticEntityRefs {
  deviceId?: string;
  portName?: string;
  netName?: string;
  segmentIndex?: number;
}

export interface DiagnosticGeometry {
  point?: PointValue;
  segment?: SegmentValue;
  bbox?: BboxValue;
}

export interface ApiDiagnostic {
  severity: string;
  code: string;
  message: string;
  entityRefs?: DiagnosticEntityRefs;
  geometry?: DiagnosticGeometry;
}

export interface RenderSourceInfo {
  hasRenderBlock: boolean;
  mode: RenderSourceMode;
}

export interface SetNetSegmentsOperation {
  type: "setNetSegments";
  net: string;
  segments: SegmentValue[];
}

export const open: NativeMethodCall;
export const updateText: NativeMethodCall;
export const close: NativeMethodCall;
export const render: NativeMethodCall;
export const applyOps: NativeMethodCall;
export const erc: NativeMethodCall;
export const emit: NativeMethodCall;
export const verify: NativeMethodCall;
export const jobStart: NativeMethodCall;
export const jobPoll: NativeMethodCall;
export const jobCancel: NativeMethodCall;
