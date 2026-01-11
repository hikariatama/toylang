import { useCallback, useEffect, useRef } from "react";
import { useCompilerStore } from "@/lib/stores/useCompiler";
import { useSourceRanges } from "@/lib/hooks/useSourceRanges";
import { buildRoot, readDiagnostics } from "@/lib/utils";
import type { PipelineOutput } from "@/server/api/routers/analyzer";
import { api } from "@/trpc/react";

export const useAnalyzerController = (): {
  runRecompile: () => void;
} => {
  const source = useCompilerStore((state) => state.source);
  const { sourceLineStarts } = useSourceRanges();
  const resetBeforeAnalyze = useCompilerStore(
    (state) => state.resetBeforeAnalyze,
  );
  const setDiagnostics = useCompilerStore((state) => state.setDiagnostics);
  const setStageError = useCompilerStore((state) => state.setStageError);
  const setTokens = useCompilerStore((state) => state.setTokens);
  const setTree = useCompilerStore((state) => state.setTree);
  const setOptTree = useCompilerStore((state) => state.setOptTree);
  const setOptimizations = useCompilerStore((state) => state.setOptimizations);
  const setWasmModule = useCompilerStore((state) => state.setWasmModule);
  const setTerminalLines = useCompilerStore((state) => state.setTerminalLines);
  const setTerminalTail = useCompilerStore((state) => state.setTerminalTail);
  const setRunError = useCompilerStore((state) => state.setRunError);
  const setWaitingForInput = useCompilerStore(
    (state) => state.setWaitingForInput,
  );
  const setIsRunning = useCompilerStore((state) => state.setIsRunning);
  const setShowRecompilePrompt = useCompilerStore(
    (state) => state.setShowRecompilePrompt,
  );

  const { mutate: analyze, data } = api.analyzer.analyze.useMutation({
    onMutate: () => {
      resetBeforeAnalyze();
      setShowRecompilePrompt(false);
    },
  });

  const initializedRef = useRef(false);

  useEffect(() => {
    if (initializedRef.current) return;
    initializedRef.current = true;
    analyze({ source });
  }, [analyze, source]);

  useEffect(() => {
    if (!data) return;
    const payload = data as unknown as PipelineOutput;
    const allDiags = readDiagnostics(payload);
    setDiagnostics(allDiags);
    const stageFailure = payload.StageError ?? null;
    setStageError(stageFailure);
    setTokens(payload.Tokens ?? []);
    setTree(
      payload.Ast ? buildRoot(payload.Ast, source, sourceLineStarts) : null,
    );
    setOptTree(
      payload.OptimizedAst
        ? buildRoot(payload.OptimizedAst, source, sourceLineStarts)
        : null,
    );
    setOptimizations(payload.Optimizations ?? null);
    setWasmModule(payload.WasmModuleBase64 ?? null);
    if (stageFailure) {
      const message = stageFailure.Message?.trim().length
        ? stageFailure.Message.trim()
        : "Compilation failed.";
      const formatted = `Compilation failed: ${message}`;
      setTerminalLines([formatted]);
      setTerminalTail("");
      setRunError(null);
      setWaitingForInput(null);
      setIsRunning(false);
    }
  }, [
    data,
    setDiagnostics,
    setOptTree,
    setOptimizations,
    setStageError,
    setTokens,
    setTree,
    setWasmModule,
    setTerminalLines,
    setTerminalTail,
    setRunError,
    setWaitingForInput,
    setIsRunning,
    source,
    sourceLineStarts,
  ]);

  const runRecompile = useCallback(() => {
    setShowRecompilePrompt(false);
    analyze({ source });
  }, [analyze, setShowRecompilePrompt, source]);

  return { runRecompile };
};
