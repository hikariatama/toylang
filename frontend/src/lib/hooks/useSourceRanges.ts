import { useCallback, useMemo } from "react";
import { flattenLines, makeRangeFromSpan } from "@/lib/utils";
import { useCompilerStore } from "@/lib/stores/useCompiler";
import type { Range } from "@/lib/types";

export const useSourceRanges = (): {
  sourceLineStarts: number[];
  createSourceRange: (
    start: number | null | undefined,
    end: number | null | undefined,
    fallbackLine: number | null | undefined,
  ) => Range;
} => {
  const showRecompilePrompt = useCompilerStore(
    (state) => state.showRecompilePrompt,
  );

  const sourceLineStarts = useMemo(() => {
    const source = useCompilerStore.getState().source;
    return flattenLines(source);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [showRecompilePrompt]);

  const createSourceRange = useCallback(
    (
      start: number | null | undefined,
      end: number | null | undefined,
      fallbackLine: number | null | undefined,
    ): Range => {
      const source = useCompilerStore.getState().source;
      return makeRangeFromSpan(
        start ?? null,
        end ?? null,
        fallbackLine ?? null,
        source,
        sourceLineStarts,
      );
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [showRecompilePrompt, sourceLineStarts],
  );

  return { sourceLineStarts, createSourceRange };
};
