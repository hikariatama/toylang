import type { RefObject } from "react";
import type { WasmSession } from "@/lib/wasm/browser/harness";

const createRef = <T>(initial: T): RefObject<T> =>
  ({
    current: initial,
  }) as RefObject<T>;

export const sessionRef = createRef<WasmSession | null>(null);
export const pendingTailRef = createRef<string>("");
export const runTokenRef = createRef<symbol | null>(null);
export const terminalContainerRef = createRef<HTMLDivElement | null>(null);
export const terminalInputRef = createRef<HTMLInputElement | null>(null);
