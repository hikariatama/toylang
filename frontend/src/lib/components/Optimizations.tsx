import { useEditorNavigation } from "@/lib/hooks/useEditorNavigation";
import { useSourceRanges } from "@/lib/hooks/useSourceRanges";
import { useCompilerStore } from "@/lib/stores/useCompiler";
import { isEqual } from "lodash";
import { Link as LinkIcon } from "lucide-react";
import type { JSX } from "react";
import { useStoreWithEqualityFn } from "zustand/traditional";
import { cn } from "../utils";

export const Optimizations = ({
  className,
}: {
  className?: string;
}): JSX.Element => {
  const optimizations = useStoreWithEqualityFn(
    useCompilerStore,
    (state) => state.optimizations,
    isEqual,
  );
  const setHoverRange = useCompilerStore((state) => state.setHoverRange);
  const { createSourceRange } = useSourceRanges();
  const { onJump } = useEditorNavigation();

  return (
    <div
      className={cn(
        "overflow-auto border border-white/30 font-mono",
        className,
      )}
    >
      <div className="font-title drag-handle sticky top-0 z-50 cursor-grab border-b border-white/30 bg-[#111] px-2 py-1 text-[10px] text-slate-300 select-none active:cursor-grabbing">
        optimizations
      </div>
      {optimizations && optimizations.length > 0 ? (
        <ul className="space-y-2 p-2">
          {optimizations.map((optimization, idx) => {
            const range = createSourceRange(
              optimization.Start ?? null,
              optimization.End ?? null,
              optimization.Line ?? null,
            );
            const derivedLine = range?.startLine ?? optimization.Line ?? null;
            const handleJump = (): void => {
              if (typeof derivedLine !== "number" || derivedLine <= 0) return;
              onJump(derivedLine, range?.startColumn, range?.endColumn);
            };
            return (
              <li
                key={`opt_${idx}`}
                className="border border-white/30"
                onMouseEnter={() => setHoverRange(range)}
                onMouseLeave={() => setHoverRange(null)}
              >
                <div className="flex items-center gap-2 border-b border-white/10 px-2 py-1 text-[10px]">
                  <span className="opacity-80">{optimization.Message}</span>
                  {typeof derivedLine === "number" && derivedLine > 0 && (
                    <button
                      type="button"
                      onClick={handleJump}
                      className="ml-auto inline-flex cursor-pointer items-center gap-1 rounded px-1.5 py-0.5 text-[10px] whitespace-pre text-slate-300 transition-colors duration-100 ease-in-out hover:bg-white/10"
                    >
                      <LinkIcon className="h-2 w-2" />
                      <span>line {derivedLine}</span>
                    </button>
                  )}
                </div>
                <div className="grid grid-cols-1 gap-2 p-2 md:grid-cols-2">
                  <div>
                    <div className="border-b border-white/10 px-2 py-1 text-[10px] text-slate-300">
                      Before
                    </div>
                    <pre className="max-h-72 overflow-auto p-2 text-[11px] whitespace-pre-wrap text-slate-200">
                      {optimization.Before ?? ""}
                    </pre>
                  </div>
                  <div>
                    <div className="border-b border-white/10 px-2 py-1 text-[10px] text-slate-300">
                      After
                    </div>
                    <pre className="max-h-72 overflow-auto p-2 text-[11px] whitespace-pre-wrap text-slate-200">
                      {optimization.After ?? ""}
                    </pre>
                  </div>
                </div>
              </li>
            );
          })}
        </ul>
      ) : (
        <div className="grid h-full place-items-center text-xs text-slate-400">
          No optimizations applied.
        </div>
      )}
    </div>
  );
};
