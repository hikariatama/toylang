import { useEditorNavigation } from "@/lib/hooks/useEditorNavigation";
import { useSourceRanges } from "@/lib/hooks/useSourceRanges";
import { useVirtualizer } from "@tanstack/react-virtual";
import { isEqual } from "lodash";
import { useRef, type JSX } from "react";
import { useStoreWithEqualityFn } from "zustand/traditional";
import { useCompilerStore } from "../stores/useCompiler";
import { cn } from "../utils";

export const TokensPanel = ({
  className,
}: {
  className?: string;
}): JSX.Element => {
  const tokens = useStoreWithEqualityFn(
    useCompilerStore,
    (state) => state.tokens,
    isEqual,
  );
  const setHoverRange = useCompilerStore((state) => state.setHoverRange);
  const { createSourceRange } = useSourceRanges();
  const { onJump } = useEditorNavigation();

  const containerRef = useRef<HTMLDivElement | null>(null);

  const rowVirtualizer = useVirtualizer({
    count: tokens.length,
    getScrollElement: () => containerRef.current,
    estimateSize: () => 18,
    overscan: 10,
  });

  const virtualItems = rowVirtualizer.getVirtualItems();
  const totalSize = rowVirtualizer.getTotalSize();

  return (
    <div
      ref={containerRef}
      className={cn("overflow-auto border border-white/30", className)}
    >
      <div className="font-title drag-handle sticky top-0 z-50 cursor-grab border-b border-white/30 bg-[#111] px-2 py-1 text-[10px] text-slate-300 select-none active:cursor-grabbing">
        tokens
      </div>
      <div className="mt-1 w-full font-mono text-[9px]">
        <div
          style={{
            display: "block",
            position: "relative",
            height: totalSize,
          }}
        >
          {virtualItems.map((virtualRow) => {
            const token = tokens[virtualRow.index]!;

            const range = createSourceRange(
              token.Start ?? null,
              token.End ?? null,
              token.Line ?? null,
            );
            const lineLabel = range?.startLine ?? token.Line ?? null;
            const spanLabel =
              range?.startColumn !== null && range?.endColumn !== null
                ? `${range?.startColumn}-${range?.endColumn}`
                : `${token.Start ?? "?"}-${token.End ?? "?"}`;

            const handleJump = (): void => {
              if (typeof lineLabel !== "number" || lineLabel <= 0) return;
              onJump(lineLabel, range?.startColumn, range?.endColumn);
            };

            return (
              <div
                key={`tok_${virtualRow.index}`}
                className="flex w-full cursor-pointer px-2 hover:bg-white/10"
                onMouseEnter={() => setHoverRange(range)}
                onMouseLeave={() => setHoverRange(null)}
                onClick={handleJump}
                ref={rowVirtualizer.measureElement}
                style={{
                  position: "absolute",
                  top: 0,
                  left: 0,
                  right: 0,
                  transform: `translateY(${virtualRow.start}px)`,
                }}
              >
                <div className="w-24 p-0.5 text-left">{token.Type}</div>
                <div className="flex-auto p-0.5 text-center">
                  {token.Lexeme ?? "\u00A0"}
                </div>
                <div className="w-24 p-0.5 text-right">
                  {lineLabel ? `${lineLabel}:${spanLabel}` : ""}
                </div>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
};
