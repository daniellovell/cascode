/** Absolute path to the bundled standard library (lib/std) directory. */
export const stdlibPath: string;

export function createSession(optionsJson?: string): number;
export function destroySession(session: number): void;
export function call(session: number, method: string, requestJson: string): string;
