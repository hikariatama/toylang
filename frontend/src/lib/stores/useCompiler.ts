import { defaultCode } from "@/lib/consts";
import type { Range, TreeNode } from "@/lib/types";
import type {
  Diagnostic,
  OptimizationStep,
  PipelineOutput,
} from "@/server/api/routers/analyzer";
import type { InputRequestKind } from "@/lib/wasm/browser/harness";
import { create } from "zustand";

type Tokens = PipelineOutput["Tokens"];
type TerminalLinesUpdater = (prev: string[]) => string[];
type TerminalInputUpdater = (prev: string) => string;

export type CompilerState = {
  source: string;
  setSource: (value: string) => void;
  tree: TreeNode | null;
  setTree: (value: TreeNode | null) => void;
  optTree: TreeNode | null;
  setOptTree: (value: TreeNode | null) => void;
  tokens: Tokens;
  setTokens: (value: Tokens) => void;
  diagnostics: Diagnostic[];
  setDiagnostics: (value: Diagnostic[]) => void;
  stageError: Diagnostic | null;
  setStageError: (value: Diagnostic | null) => void;
  optimizations: OptimizationStep[] | null;
  setOptimizations: (value: OptimizationStep[] | null) => void;
  hoverRange: Range;
  setHoverRange: (value: Range) => void;
  wasmModule: string | null;
  setWasmModule: (value: string | null) => void;
  runError: string | null;
  setRunError: (value: string | null) => void;
  terminalLines: string[];
  setTerminalLines: (value: TerminalLinesUpdater | string[]) => void;
  terminalTail: string;
  setTerminalTail: (value: string) => void;
  waitingForInput: InputRequestKind | null;
  setWaitingForInput: (value: InputRequestKind | null) => void;
  isRunning: boolean;
  setIsRunning: (value: boolean) => void;
  terminalInput: string;
  setTerminalInput: (value: string | TerminalInputUpdater) => void;
  showRecompilePrompt: boolean;
  setShowRecompilePrompt: (value: boolean) => void;
  resetBeforeAnalyze: () => void;
};

export const useCompilerStore = create<CompilerState>((set) => ({
  source: defaultCode,
  setSource: (value: string) => set({ source: value }),
  tree: null,
  setTree: (value: TreeNode | null) => set({ tree: value }),
  optTree: null,
  setOptTree: (value: TreeNode | null) => set({ optTree: value }),
  tokens: [],
  setTokens: (value: Tokens) => set({ tokens: value }),
  diagnostics: [],
  setDiagnostics: (value: Diagnostic[]) => set({ diagnostics: value }),
  stageError: null,
  setStageError: (value: Diagnostic | null) => set({ stageError: value }),
  optimizations: null,
  setOptimizations: (value: OptimizationStep[] | null) =>
    set({ optimizations: value }),
  hoverRange: null,
  setHoverRange: (value: Range) => set({ hoverRange: value }),
  wasmModule: null,
  setWasmModule: (value: string | null) => set({ wasmModule: value }),
  runError: null,
  setRunError: (value: string | null) => set({ runError: value }),
  terminalLines: [],
  setTerminalLines: (value: TerminalLinesUpdater | string[]) =>
    set((state) => ({
      terminalLines:
        typeof value === "function" ? value(state.terminalLines) : value,
    })),
  terminalTail: "",
  setTerminalTail: (value: string) => set({ terminalTail: value }),
  waitingForInput: null,
  setWaitingForInput: (value: InputRequestKind | null) =>
    set({ waitingForInput: value }),
  isRunning: false,
  setIsRunning: (value: boolean) => set({ isRunning: value }),
  terminalInput: "",
  setTerminalInput: (value: string | TerminalInputUpdater) =>
    set((state) => ({
      terminalInput:
        typeof value === "function" ? value(state.terminalInput) : value,
    })),
  showRecompilePrompt: false,
  setShowRecompilePrompt: (value: boolean) =>
    set({ showRecompilePrompt: value }),
  resetBeforeAnalyze: () =>
    set({
      tree: null,
      optTree: null,
      tokens: [],
      diagnostics: [],
      stageError: null,
      optimizations: null,
      hoverRange: null,
      wasmModule: null,
      runError: null,
      terminalLines: [],
      terminalTail: "",
      waitingForInput: null,
      isRunning: false,
      terminalInput: "",
      showRecompilePrompt: false,
    }),
}));
