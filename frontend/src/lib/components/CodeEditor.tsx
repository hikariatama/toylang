import { isEqual } from "lodash";
import React, { useCallback, useMemo, type JSX } from "react";
import Editor from "react-simple-code-editor";
import { useStoreWithEqualityFn } from "zustand/traditional";
import { useCompilerStore } from "@/lib/stores/useCompiler";
import type { CompilerState } from "@/lib/stores/useCompiler";
import { useEditorNavigation } from "@/lib/hooks/useEditorNavigation";
import { useSourceRanges } from "@/lib/hooks/useSourceRanges";
import { cn, tokenizeLine } from "@/lib/utils";
import {
  pendingTailRef,
  runTokenRef,
  sessionRef,
} from "@/lib/runtime/runtimeState";

const useHighlight = (): ((code: string) => JSX.Element) => {
  const hoverRange = useCompilerStore((state) => state.hoverRange);
  const diagnostics = useStoreWithEqualityFn(
    useCompilerStore,
    (state) => state.diagnostics,
    isEqual,
  );
  const stageError = useCompilerStore((state) => state.stageError);
  const { createSourceRange } = useSourceRanges();

  const diagnosticLineInfo = useMemo(() => {
    const map = new Map<
      number,
      { hasDiagnostic: boolean; hasError: boolean; hasWarning: boolean }
    >();
    const record = (
      line: number | null,
      severity: CompilerState["diagnostics"][number]["Severity"],
    ): void => {
      if (typeof line !== "number" || !Number.isFinite(line) || line <= 0)
        return;
      const entry = map.get(line) ?? {
        hasDiagnostic: false,
        hasError: false,
        hasWarning: false,
      };
      entry.hasDiagnostic = true;
      if (severity === "Error") entry.hasError = true;
      else if (severity === "Warning") entry.hasWarning = true;
      map.set(line, entry);
    };

    diagnostics.forEach((diagnostic) => {
      const range = createSourceRange(
        diagnostic.Start ?? null,
        diagnostic.End ?? null,
        diagnostic.Line ?? null,
      );
      const derivedLine = range?.startLine ?? diagnostic.Line ?? null;
      record(derivedLine, diagnostic.Severity);
    });

    if (stageError) {
      const range = createSourceRange(
        stageError.Start ?? null,
        stageError.End ?? null,
        stageError.Line ?? null,
      );
      const derivedLine = range?.startLine ?? stageError.Line ?? null;
      record(derivedLine, stageError.Severity);
    }

    return map;
  }, [diagnostics, stageError, createSourceRange]);

  const highlight = useCallback(
    (code: string): JSX.Element => {
      const renderTokens = (
        text: string,
        keyPrefix: string,
      ): React.ReactNode => {
        const segments = tokenizeLine(text);
        if (segments.length === 0) return "\u00A0";
        return segments.map((token, idx) => (
          <span
            key={`${keyPrefix}_${idx}`}
            className={token.className ?? undefined}
          >
            {token.text}
          </span>
        ));
      };

      const renderSegment = (
        text: string,
        keyPrefix: string,
      ): React.ReactNode => {
        if (text.length === 0) return null;
        return renderTokens(text, keyPrefix);
      };

      const lines = code.split("\n");
      const range = hoverRange;
      const startLine = range?.startLine ?? null;
      const endLine = range?.endLine ?? startLine;

      return (
        <>
          {lines.map((line, index) => {
            const lineNumber = index + 1;
            const diagInfo = diagnosticLineInfo.get(lineNumber);
            const hasDiag = !!diagInfo?.hasDiagnostic;
            const hasError = !!diagInfo?.hasError;
            const hasWarning = !!diagInfo?.hasWarning && !hasError;
            const lineClasses = cn(
              hasDiag && "diag-line",
              hasError && "error-line",
              hasWarning && "warning-line",
            );

            const hasRangeBounds =
              range !== null &&
              startLine !== null &&
              endLine !== null &&
              lineNumber >= startLine &&
              lineNumber <= endLine;

            let content: React.ReactNode;
            if (!hasRangeBounds) {
              content = renderTokens(line, `tok_${lineNumber}_full`);
            } else {
              const lineLength = line.length;
              const defaultEndCol = lineLength > 0 ? lineLength : 1;
              const startColumnRaw =
                lineNumber === startLine &&
                typeof range?.startColumn === "number" &&
                range.startColumn > 0
                  ? range.startColumn
                  : 1;
              const endColumnRaw =
                lineNumber === endLine &&
                typeof range?.endColumn === "number" &&
                range.endColumn > 0
                  ? range.endColumn
                  : defaultEndCol;
              const normalizedStartColumn = Math.max(
                1,
                Math.min(startColumnRaw, lineLength + 1),
              );
              const normalizedEndColumnInclusive = Math.max(
                normalizedStartColumn,
                Math.min(
                  endColumnRaw,
                  lineLength > 0 ? lineLength : normalizedStartColumn,
                ),
              );
              const startIdx = normalizedStartColumn - 1;
              const endExclusive =
                lineLength === 0
                  ? startIdx
                  : Math.min(lineLength, normalizedEndColumnInclusive);
              const before = line.slice(0, startIdx);
              const middle =
                lineLength === 0 && startIdx === endExclusive
                  ? ""
                  : line.slice(startIdx, endExclusive);
              const after = line.slice(endExclusive);
              const beforeContent = renderSegment(
                before,
                `tok_${lineNumber}_before`,
              );
              const afterContent = renderSegment(
                after,
                `tok_${lineNumber}_after`,
              );
              const coversWholeLine =
                startIdx === 0 &&
                (lineLength === 0 || endExclusive >= lineLength);
              const middleContent =
                middle.length === 0
                  ? "\u00A0"
                  : renderTokens(middle, `tok_${lineNumber}_mid`);

              content = (
                <>
                  {beforeContent}
                  <span
                    className={cn(
                      "highlighted-fragment",
                      coversWholeLine && "highlighted-line",
                    )}
                  >
                    {middleContent}
                  </span>
                  {afterContent}
                </>
              );

              if (!beforeContent && middle.length === 0 && !afterContent) {
                content = (
                  <span className="highlighted-fragment highlighted-line">
                    {"\u00A0"}
                  </span>
                );
              }
            }

            return (
              <React.Fragment key={`l_${index}`}>
                <span className={lineClasses}>{content}</span>
                {"\n"}
              </React.Fragment>
            );
          })}
        </>
      );
    },
    [diagnosticLineInfo, hoverRange],
  );

  return highlight;
};

const useCodeEditorInteractions = (): {
  highlight: (code: string) => JSX.Element;
  handleValueChange: (value: string) => void;
} => {
  const highlight = useHighlight();
  const source = useCompilerStore((state) => state.source);
  const setSource = useCompilerStore((state) => state.setSource);
  const setIsRunning = useCompilerStore((state) => state.setIsRunning);
  const setWaitingForInput = useCompilerStore(
    (state) => state.setWaitingForInput,
  );
  const setTerminalTail = useCompilerStore((state) => state.setTerminalTail);
  const setTerminalInput = useCompilerStore((state) => state.setTerminalInput);
  const setRunError = useCompilerStore((state) => state.setRunError);
  const setShowRecompilePrompt = useCompilerStore(
    (state) => state.setShowRecompilePrompt,
  );

  const handleValueChange = useCallback(
    (value: string): void => {
      if (value === source) return;
      setSource(value);
      sessionRef.current?.terminate();
      sessionRef.current = null;
      runTokenRef.current = null;
      setIsRunning(false);
      setWaitingForInput(null);
      pendingTailRef.current = "";
      setTerminalTail("");
      setTerminalInput("");
      setRunError(null);
      setShowRecompilePrompt(true);
    },
    [
      source,
      setIsRunning,
      setRunError,
      setShowRecompilePrompt,
      setSource,
      setTerminalInput,
      setTerminalTail,
      setWaitingForInput,
    ],
  );

  return { highlight, handleValueChange };
};

export const CodeEditor = ({
  className,
}: {
  className?: string;
}): JSX.Element => {
  const { editorRef } = useEditorNavigation();
  const source = useCompilerStore((state) => state.source);
  const { highlight, handleValueChange } = useCodeEditorInteractions();

  const lines = (source.match(/\n/g) ?? []).length + 2;
  const lineNos = [...Array(lines).keys()].slice(1).join("\x0a");

  return (
    <div className={cn("border border-white/30 p-0 text-sm", className)}>
      <div ref={editorRef} className="relative h-full overflow-auto">
        <div data-content={lineNos} id="line-numbers">
          <Editor
            value={source}
            onValueChange={handleValueChange}
            highlight={highlight}
            padding={16}
            autoCapitalize="off"
            tabSize={4}
            textareaId="code"
            className="ml-8! w-max min-w-[calc(100%-2rem)] font-mono text-sm leading-6"
            textareaClassName="outline-none"
            preClassName="focus:outline-none"
            style={{ background: "transparent" }}
          />
        </div>
      </div>
    </div>
  );
};
