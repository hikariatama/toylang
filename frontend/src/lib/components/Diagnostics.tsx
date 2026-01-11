import { useEditorNavigation } from "@/lib/hooks/useEditorNavigation";
import { useSourceRanges } from "@/lib/hooks/useSourceRanges";
import { cn } from "@/lib/utils";
import { isEqual } from "lodash";
import { Link as LinkIcon } from "lucide-react";
import type { JSX } from "react";
import { useStoreWithEqualityFn } from "zustand/traditional";
import { useCompilerStore } from "../stores/useCompiler";

export const Diagnostics = ({
  className,
}: {
  className?: string;
}): JSX.Element => {
  const diagnostics = useStoreWithEqualityFn(
    useCompilerStore,
    (state) => state.diagnostics,
    isEqual,
  );
  console.log(diagnostics);
  const setHoverRange = useCompilerStore((state) => state.setHoverRange);
  const { createSourceRange } = useSourceRanges();
  const { onJump } = useEditorNavigation();

  return (
    <div
      className={cn(
        "overflow-auto border border-white/30 font-mono text-xs",
        className,
      )}
    >
      <div className="font-title drag-handle sticky top-0 z-50 cursor-grab border-b border-white/30 bg-[#111] px-2 py-1 text-[10px] text-slate-300 select-none active:cursor-grabbing">
        diagnostics
      </div>
      {diagnostics.length > 0 ? (
        <ul className="space-y-2 p-2">
          {diagnostics.map((diagnostic, idx) => {
            const range = createSourceRange(
              diagnostic.Start ?? null,
              diagnostic.End ?? null,
              diagnostic.Line ?? null,
            );
            const derivedLine = range?.startLine ?? diagnostic.Line ?? null;
            const handleJump = (): void => {
              if (typeof derivedLine !== "number" || derivedLine <= 0) return;
              onJump(derivedLine, range?.startColumn, range?.endColumn);
            };
            return (
              <li
                key={`diag_${idx}`}
                className={cn(
                  "border-l-4 p-2",
                  diagnostic.Severity === "Error"
                    ? "border-red-500 bg-red-900/10"
                    : "border-yellow-500 bg-yellow-900/10",
                )}
                onMouseEnter={() => setHoverRange(range)}
                onMouseLeave={() => setHoverRange(null)}
              >
                <div className="flex items-center gap-2">
                  <span className="font-medium">{diagnostic.Severity}</span>
                  {typeof derivedLine === "number" && derivedLine > 0 && (
                    <button
                      type="button"
                      onClick={handleJump}
                      className="ml-auto inline-flex cursor-pointer items-center gap-1 rounded px-1.5 py-0.5 text-[10px] text-slate-300 transition-colors duration-100 ease-in-out hover:bg-white/10"
                    >
                      <LinkIcon className="h-2 w-2" />
                      <span>line {derivedLine}</span>
                    </button>
                  )}
                </div>
                <div className="mt-1">{diagnostic.Message}</div>
              </li>
            );
          })}
        </ul>
      ) : (
        <div className="grid h-full place-items-center text-slate-400">
          No diagnostics.
        </div>
      )}
    </div>
  );
};
