import { useCallback } from "react";
import type { RefObject } from "react";
import { useCompilerStore } from "@/lib/stores/useCompiler";
import { scrollToLine } from "@/lib/utils";

const editorHostRef: RefObject<HTMLDivElement | null> = {
  current: null,
};

export const useEditorNavigation = (): {
  editorRef: RefObject<HTMLDivElement | null>;
  onJump: (
    line: number,
    columnStart?: number | null,
    columnEnd?: number | null,
  ) => void;
} => {
  const onJump = useCallback(
    (line: number, columnStart?: number | null, columnEnd?: number | null) => {
      const source = useCompilerStore.getState().source;
      scrollToLine(line, source, editorHostRef, columnStart, columnEnd);
    },
    [],
  );

  return { editorRef: editorHostRef, onJump };
};
