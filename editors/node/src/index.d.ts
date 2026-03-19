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
export function invoke<Req extends object, Res>(
  native: CascodeNative,
  session: number,
  method: string,
  request?: Req
): Res;
export function lastErrorJson(session: number): string | null;
export function apiVersion(): string;
export function schemaVersion(): string;

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
