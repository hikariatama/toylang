import { useAnalyzerController } from "@/lib/hooks/useAnalyzerController";
import {
  pendingTailRef,
  runTokenRef,
  sessionRef,
  terminalContainerRef,
  terminalInputRef,
} from "@/lib/runtime/runtimeState";
import type { CompilerState } from "@/lib/stores/useCompiler";
import { useCompilerStore } from "@/lib/stores/useCompiler";
import { instantiateWithIo } from "@/lib/wasm/browser/harness";
import { isEqual } from "lodash";
import { useCallback, useEffect } from "react";
import { useStoreWithEqualityFn } from "zustand/traditional";

export const useTerminalRuntime = (): {
  containerRef: typeof terminalContainerRef;
  inputRef: typeof terminalInputRef;
  terminalLines: string[];
  terminalTail: string;
  runError: string | null;
  showRecompilePrompt: boolean;
  terminalInput: string;
  isRunning: boolean;
  waitingForInput: CompilerState["waitingForInput"];
  inputEnabled: boolean;
  handleTerminalSubmit: () => void;
  runRecompile: () => void;
  handleInputChange: (value: string) => void;
  restartExecution: () => void;
  canRestart: boolean;
} => {
  const terminalLines = useStoreWithEqualityFn(
    useCompilerStore,
    (state) => state.terminalLines,
    isEqual,
  );
  const terminalTail = useCompilerStore((state) => state.terminalTail);
  const runError = useCompilerStore((state) => state.runError);
  const showRecompilePrompt = useCompilerStore(
    (state) => state.showRecompilePrompt,
  );
  const terminalInput = useCompilerStore((state) => state.terminalInput);
  const isRunning = useCompilerStore((state) => state.isRunning);
  const waitingForInput = useCompilerStore((state) => state.waitingForInput);
  const wasmModule = useCompilerStore((state) => state.wasmModule);

  const setTerminalLines = useCompilerStore((state) => state.setTerminalLines);
  const setTerminalTail = useCompilerStore((state) => state.setTerminalTail);
  const setWaitingForInput = useCompilerStore(
    (state) => state.setWaitingForInput,
  );
  const setTerminalInput = useCompilerStore((state) => state.setTerminalInput);
  const setIsRunning = useCompilerStore((state) => state.setIsRunning);
  const setRunError = useCompilerStore((state) => state.setRunError);

  const { runRecompile } = useAnalyzerController();

  const measureScreen = useCallback((): { width: number; height: number } => {
    if (typeof document === "undefined") return { width: 80, height: 24 };
    const host = terminalContainerRef.current;
    if (!host) return { width: 80, height: 24 };
    const textRoot = host.querySelector("pre") ?? host;
    const textStyle = window.getComputedStyle(textRoot);
    const probe = document.createElement("span");
    probe.textContent = "M";
    probe.style.visibility = "hidden";
    probe.style.position = "absolute";
    probe.style.pointerEvents = "none";
    probe.style.whiteSpace = "pre";
    probe.style.fontFamily = textStyle.fontFamily;
    probe.style.fontSize = textStyle.fontSize;
    probe.style.fontWeight = textStyle.fontWeight;
    probe.style.lineHeight = textStyle.lineHeight;
    document.body.appendChild(probe);
    const rect = probe.getBoundingClientRect();
    probe.remove();
    const charWidth = rect.width || 6;
    const charHeight = rect.height || 12;
    const availableWidth = Math.max(1, host.clientWidth);
    const availableHeight = Math.max(1, host.clientHeight);
    return {
      width: availableWidth / charWidth,
      height: availableHeight / charHeight,
    };
  }, []);

  const appendOutput = useCallback(
    (chunk: string): void => {
      const normalized = chunk.replace(/\r\n?/g, "\n");
      let buffer = pendingTailRef.current + normalized;

      const cursorHome = /\x1b\[(?:\d+(?:;\d+)*)?H/g;
      let cursorHomeSeen = false;
      let sliceStart = 0;
      while (cursorHome.exec(buffer) !== null) {
        cursorHomeSeen = true;
        sliceStart = cursorHome.lastIndex;
      }
      if (cursorHomeSeen) {
        buffer = buffer.slice(sliceStart);
        pendingTailRef.current = "";
        setTerminalTail("");
      }

      const parts = buffer.split("\n");
      const nextTail = parts.pop() ?? "";
      if (parts.length > 0 || cursorHomeSeen) {
        setTerminalLines((prev) =>
          cursorHomeSeen ? [...parts] : [...prev, ...parts],
        );
      }
      pendingTailRef.current = nextTail;
      setTerminalTail(nextTail);
    },
    [setTerminalLines, setTerminalTail],
  );

  const flushPendingOutput = useCallback(
    (input?: string): void => {
      if (pendingTailRef.current.length > 0) {
        const tail = pendingTailRef.current;
        pendingTailRef.current = "";
        if (input !== undefined && !tail.endsWith("\n")) {
          setTerminalLines((prev) => [...prev, tail + input]);
        } else if (input !== undefined) {
          setTerminalLines((prev) => [...prev, tail, input]);
        } else {
          setTerminalLines((prev) => [...prev, tail]);
        }
      } else if (input !== undefined) {
        setTerminalLines((prev) => [...prev, input]);
      }
      setTerminalTail("");
    },
    [setTerminalLines, setTerminalTail],
  );

  const dispatchInputToSession = useCallback(
    (value: string): void => {
      const session = sessionRef.current;
      if (!session) return;
      if (waitingForInput === "char") {
        session.sendText(value);
        return;
      }
      session.sendLine(value);
    },
    [waitingForInput],
  );

  const handleTerminalSubmit = useCallback((): void => {
    if (!waitingForInput || showRecompilePrompt) return;
    const value = terminalInput;
    flushPendingOutput(value);
    dispatchInputToSession(value);
    setTerminalInput("");
    setWaitingForInput(null);
  }, [
    dispatchInputToSession,
    flushPendingOutput,
    setTerminalInput,
    setWaitingForInput,
    showRecompilePrompt,
    terminalInput,
    waitingForInput,
  ]);

  const handleInputChange = useCallback(
    (value: string): void => {
      setTerminalInput(value);
    },
    [setTerminalInput],
  );

  const startRun = useCallback(
    async (base64: string): Promise<void> => {
      if (!base64) return;
      sessionRef.current?.terminate();
      sessionRef.current = null;
      const token = Symbol("run");
      runTokenRef.current = token;
      setIsRunning(true);
      setRunError(null);
      setTerminalLines([]);
      setTerminalTail("");
      setWaitingForInput(null);
      pendingTailRef.current = "";
      setTerminalInput("");
      const guarded =
        <T extends unknown[]>(
          fn: (...args: T) => void,
        ): ((...args: T) => void) =>
        (...args: T) => {
          if (runTokenRef.current !== token) return;
          fn(...args);
        };
      const finishRun = (
        resultMessage: string | null,
        errored: boolean,
      ): void => {
        if (runTokenRef.current !== token) return;
        flushPendingOutput();
        if (resultMessage) setTerminalLines((prev) => [...prev, resultMessage]);
        setWaitingForInput(null);
        setIsRunning(false);
        sessionRef.current = null;
        runTokenRef.current = null;
        if (errored && resultMessage) setRunError(resultMessage);
        else if (errored && !resultMessage) setRunError("Execution failed");
      };
      try {
        const binary = globalThis.atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i += 1)
          bytes[i] = binary.charCodeAt(i);
        const screen = measureScreen();
        const session = await instantiateWithIo(
          bytes,
          {
            onOutput: guarded(appendOutput),
            onWaiting: guarded((kind) => setWaitingForInput(kind)),
            onExit: guarded((result) => {
              const message =
                result !== null ? `[process exited with code ${result}]` : null;
              finishRun(message, false);
            }),
            onError: guarded((message) => finishRun(message, true)),
          },
          { screen },
        );
        if (runTokenRef.current !== token) {
          session.terminate();
          return;
        }
        sessionRef.current = session;
        window.requestAnimationFrame(() =>
          terminalContainerRef.current?.scrollTo({
            top: terminalContainerRef.current.scrollHeight,
          }),
        );
      } catch (err) {
        const message =
          err instanceof Error ? err.message : "Failed to execute module.";
        finishRun(message, true);
      }
    },
    [
      appendOutput,
      flushPendingOutput,
      measureScreen,
      setIsRunning,
      setRunError,
      setTerminalInput,
      setTerminalLines,
      setTerminalTail,
      setWaitingForInput,
    ],
  );

  useEffect(() => {
    if (!wasmModule) return;
    void startRun(wasmModule);
  }, [startRun, wasmModule]);

  const restartExecution = useCallback((): void => {
    if (!wasmModule) return;
    void startRun(wasmModule);
  }, [startRun, wasmModule]);

  useEffect(() => {
    const host = terminalContainerRef.current;
    if (!host) return;
    host.scrollTo({ top: host.scrollHeight });
  }, [terminalLines, terminalTail, showRecompilePrompt]);

  useEffect(() => {
    if (waitingForInput) {
      window.requestAnimationFrame(() => {
        if (terminalInputRef.current) {
          terminalInputRef.current.focus();
          terminalInputRef.current.select();
        }
      });
    }
  }, [waitingForInput]);

  return {
    containerRef: terminalContainerRef,
    inputRef: terminalInputRef,
    terminalLines,
    terminalTail,
    runError,
    showRecompilePrompt,
    terminalInput,
    isRunning,
    waitingForInput,
    inputEnabled: waitingForInput !== null,
    handleTerminalSubmit,
    runRecompile,
    handleInputChange,
    restartExecution,
    canRestart: Boolean(wasmModule),
  };
};
