import { cn } from "@/lib/utils";
import type { CSSProperties, JSX } from "react";
import React, { useMemo } from "react";
import { useTerminalRuntime } from "@/lib/hooks/useTerminalRuntime";
import { Spinner } from "./ui/Spinner";

type SGRState = {
  color?: string;
  backgroundColor?: string;
  fontWeight?: CSSProperties["fontWeight"];
  fontStyle?: CSSProperties["fontStyle"];
  textDecorationLine?: CSSProperties["textDecorationLine"];
  filterInvert?: boolean;
};

const palette: Record<number, string> = {
  30: "var(--ansi-black)",
  31: "var(--ansi-red)",
  32: "var(--ansi-green)",
  33: "var(--ansi-yellow)",
  34: "var(--ansi-blue)",
  35: "var(--ansi-magenta)",
  36: "var(--ansi-cyan)",
  37: "var(--ansi-white)",
  90: "var(--ansi-bright-black)",
  91: "var(--ansi-bright-red)",
  92: "var(--ansi-bright-green)",
  93: "var(--ansi-bright-yellow)",
  94: "var(--ansi-bright-blue)",
  95: "var(--ansi-bright-magenta)",
  96: "var(--ansi-bright-cyan)",
  97: "var(--ansi-bright-white)",
};

const bgPalette: Record<number, string> = {
  40: "var(--ansi-black)",
  41: "var(--ansi-red)",
  42: "var(--ansi-green)",
  43: "var(--ansi-yellow)",
  44: "var(--ansi-blue)",
  45: "var(--ansi-magenta)",
  46: "var(--ansi-cyan)",
  47: "var(--ansi-white)",
  100: "var(--ansi-bright-black)",
  101: "var(--ansi-bright-red)",
  102: "var(--ansi-bright-green)",
  103: "var(--ansi-bright-yellow)",
  104: "var(--ansi-bright-blue)",
  105: "var(--ansi-bright-magenta)",
  106: "var(--ansi-bright-cyan)",
  107: "var(--ansi-bright-white)",
};

const xterm256 = (n: number): string => {
  if (n < 0) n = 0;
  if (n > 255) n = 255;
  if (n < 16) {
    const base = [
      "#000000",
      "#800000",
      "#008000",
      "#808000",
      "#000080",
      "#800080",
      "#008080",
      "#c0c0c0",
      "#808080",
      "#ff0000",
      "#00ff00",
      "#ffff00",
      "#0000ff",
      "#ff00ff",
      "#00ffff",
      "#ffffff",
    ];
    return base[n]!;
  }
  if (n >= 232) {
    const c = 8 + (n - 232) * 10;
    const v = c.toString(16).padStart(2, "0");
    return `#${v}${v}${v}`;
  }
  const idx = n - 16;
  const r = Math.floor(idx / 36) % 6;
  const g = Math.floor(idx / 6) % 6;
  const b = idx % 6;
  const step = [0, 95, 135, 175, 215, 255];
  const rr = step[r]!.toString(16).padStart(2, "0");
  const gg = step[g]!.toString(16).padStart(2, "0");
  const bb = step[b]!.toString(16).padStart(2, "0");
  return `#${rr}${gg}${bb}`;
};

class AnsiParser {
  private re = /(\x1b\[[0-9;]*m)/g;

  parse(text: string): { key: number; style: CSSProperties; text: string }[][] {
    let state: SGRState = {};
    const out: Array<{ key: number; style: CSSProperties; text: string }> = [];
    const parts = text.split(this.re);
    let key = 0;

    const applySGR = (codes: number[]) => {
      let i = 0;
      if (codes.length === 0) codes = [0];
      while (i < codes.length) {
        const c = codes[i++]!;
        if (c === 0) {
          state = {};
          continue;
        }
        if (c === 1) state.fontWeight = "bold";
        else if (c === 3) state.fontStyle = "italic";
        else if (c === 4)
          state.textDecorationLine = mergeDecor(
            state.textDecorationLine,
            "underline",
          );
        else if (c === 9)
          state.textDecorationLine = mergeDecor(
            state.textDecorationLine,
            "line-through",
          );
        else if (c === 22) state.fontWeight = "normal";
        else if (c === 23) state.fontStyle = "normal";
        else if (c === 24)
          state.textDecorationLine = removeDecor(
            state.textDecorationLine,
            "underline",
          );
        else if (c === 29)
          state.textDecorationLine = removeDecor(
            state.textDecorationLine,
            "line-through",
          );
        else if (c === 7) state.filterInvert = true;
        else if (c === 27) state.filterInvert = false;
        else if (c === 39) delete state.color;
        else if (c === 49) delete state.backgroundColor;
        else if ((c >= 30 && c <= 37) || (c >= 90 && c <= 97))
          state.color = palette[c];
        else if ((c >= 40 && c <= 47) || (c >= 100 && c <= 107))
          state.backgroundColor = bgPalette[c];
        else if (c === 38 || c === 48) {
          const isFg = c === 38;
          const mode = codes[i++];
          if (mode === 5) {
            const n = codes[i++];
            const hex = xterm256(typeof n === "number" ? n : 0);
            if (isFg) state.color = hex;
            else state.backgroundColor = hex;
          } else if (mode === 2) {
            const r = codes[i++]!,
              g = codes[i++]!,
              b = codes[i++]!;
            const clamp = (v: number) =>
              Math.max(0, Math.min(255, typeof v === "number" ? v : 0));
            const hex = `#${clamp(r).toString(16).padStart(2, "0")}${clamp(g).toString(16).padStart(2, "0")}${clamp(b).toString(16).padStart(2, "0")}`;
            if (isFg) state.color = hex;
            else state.backgroundColor = hex;
          }
        }
      }
    };

    const stateToStyle = (): CSSProperties => {
      const s: CSSProperties = {};
      if (state.color) s.color = state.color;
      if (state.backgroundColor) s.backgroundColor = state.backgroundColor;
      if (state.fontWeight) s.fontWeight = state.fontWeight;
      if (state.fontStyle) s.fontStyle = state.fontStyle;
      if (state.textDecorationLine)
        s.textDecorationLine = state.textDecorationLine;
      if (state.filterInvert) s.filter = "invert(100%)";
      return s;
    };

    for (const part of parts) {
      if (!part) continue;
      if (part.startsWith("\x1b[")) {
        const raw = part.slice(2, -1);
        const codes = raw.length
          ? raw.split(";").map((c) => (c === "" ? 0 : Number(c)))
          : [0];
        applySGR(codes);
      } else {
        out.push({ key: key++, style: stateToStyle(), text: part });
      }
    }

    const lines: Array<{ key: number; style: CSSProperties; text: string }[]> =
      [];
    let currentLine: Array<{
      key: number;
      style: CSSProperties;
      text: string;
    }> = [];
    for (const segment of out) {
      const segmentLines = segment.text.split("\n");
      for (let i = 0; i < segmentLines.length; i++) {
        if (i > 0) {
          lines.push(currentLine);
          currentLine = [];
        }
        if (segmentLines[i] !== "") {
          currentLine.push({
            key: segment.key,
            style: segment.style,
            text: segmentLines[i]!,
          });
        }
      }
    }
    lines.push(currentLine);
    return lines;
  }
}

const mergeDecor = (
  curr?: CSSProperties["textDecorationLine"],
  add?: "underline" | "line-through",
) => {
  const set = new Set((curr ?? "").split(" ").filter(Boolean));
  if (add) set.add(add);
  return Array.from(set).join(" ") as CSSProperties["textDecorationLine"];
};
const removeDecor = (
  curr?: CSSProperties["textDecorationLine"],
  rem?: "underline" | "line-through",
) => {
  const set = new Set((curr ?? "").split(" ").filter(Boolean));
  if (rem) set.delete(rem);
  return Array.from(set).join(" ") as CSSProperties["textDecorationLine"];
};

export const Terminal = ({
  className,
}: {
  className?: string;
}): JSX.Element => {
  const {
    containerRef,
    inputRef,
    terminalLines,
    terminalTail,
    runError,
    showRecompilePrompt,
    terminalInput,
    isRunning,
    inputEnabled,
    handleTerminalSubmit,
    runRecompile,
    handleInputChange,
    restartExecution,
    canRestart,
  } = useTerminalRuntime();

  const fullOutput = useMemo(() => {
    if (
      terminalLines.length === 0 &&
      terminalTail.length === 0 &&
      !runError &&
      !inputEnabled
    )
      return "";
    const body = terminalLines.length ? terminalLines.join("\n") : "";
    const tail = terminalTail;
    return body + (body && (tail || inputEnabled) ? "\n" : "") + tail;
  }, [terminalLines, terminalTail, inputEnabled, runError]);

  const runs = useMemo(() => new AnsiParser().parse(fullOutput), [fullOutput]);

  return (
    <div
      className={cn("flex h-full flex-col border border-white/30", className)}
    >
      <div className="flex items-center justify-between border-b border-white/30 bg-[#111] px-2 py-1 text-[10px] text-slate-300">
        <div className="font-title drag-handle flex-auto cursor-grab select-none active:cursor-grabbing">
          terminal
        </div>
        <div className="flex items-center gap-2">
          {canRestart && (
            <button
              type="button"
              onClick={restartExecution}
              className="cursor-pointer border border-white/30 px-2 py-0.5 text-[9px] font-light tracking-widest text-[#D4D4D4] uppercase transition-colors hover:bg-[#D4D4D4]/10 focus:outline-none focus-visible:ring-1 focus-visible:ring-[#D4D4D4]"
            >
              Restart
            </button>
          )}
          {showRecompilePrompt && (
            <button
              type="button"
              onClick={runRecompile}
              className="cursor-pointer border border-white/30 px-2 py-0.5 text-[9px] font-light tracking-widest text-[#D4D4D4] uppercase transition-colors hover:bg-[#D4D4D4]/10 focus:outline-none focus-visible:ring-1 focus-visible:ring-[#D4D4D4]"
            >
              Recompile
            </button>
          )}
        </div>
      </div>
      <div
        ref={containerRef}
        onClick={() => {
          if (inputEnabled) inputRef.current?.focus();
        }}
        className={cn(
          "terminal-host flex-1 overflow-auto bg-[#111] p-2 font-mono text-[11px] whitespace-pre-wrap text-slate-200",
        )}
      >
        <pre className="m-0 whitespace-pre-wrap [&>span]:py-1">
          {runError && (
            <>
              <span className="mb-2 text-red-300">{runError}</span>
              {"\n"}
            </>
          )}
          {fullOutput ? (
            runs.slice(0, inputEnabled ? -1 : undefined).map((line, i) => (
              <span key={`${i}-${line.map((r) => r.key).join("-")}`}>
                {line.map((r) => (
                  <span key={r.key} style={r.style}>
                    {r.text}
                  </span>
                ))}
                {"\n"}
              </span>
            ))
          ) : (
            <>
              <span className="text-slate-500">
                <Spinner />{" "}
                {isRunning ? "Running..." : "Program output appears here."}
              </span>
              {"\n"}
            </>
          )}
          {showRecompilePrompt ? (
            <span className="text-indigo-300">
              {"\n"}
              Source changed. Recompile to update output.
            </span>
          ) : inputEnabled ? (
            <>
              <span className="-mt-1 inline-flex w-full min-w-0 items-baseline">
                <span>
                  {runs.length > 0
                    ? runs[runs.length - 1]!.map((r) => (
                        <span key={r.key} style={r.style}>
                          {r.text}
                        </span>
                      ))
                    : null}
                </span>
                <input
                  ref={inputRef}
                  type="text"
                  value={terminalInput}
                  onChange={(event) => handleInputChange(event.target.value)}
                  onKeyDown={(event) => {
                    if (event.key === "Enter") {
                      event.preventDefault();
                      handleTerminalSubmit();
                    }
                  }}
                  className={cn(
                    "w-full flex-1 border-none bg-transparent p-0 text-[11px] text-[#D4D4D4] caret-[#D4D4D4]",
                    "focus:outline-none",
                  )}
                  autoCorrect="off"
                  autoComplete="off"
                  spellCheck={false}
                  disabled={!inputEnabled}
                />
              </span>
            </>
          ) : null}
        </pre>
      </div>
    </div>
  );
};
