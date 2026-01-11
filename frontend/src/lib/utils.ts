import type {
  AstTree,
  Diagnostic,
  JSONValue,
  PipelineOutput,
} from "@/server/api/routers/analyzer";
import { type ClassValue, clsx } from "clsx";
import { twMerge } from "tailwind-merge";
import type { Range, SyntaxToken, TreeNode } from "./types";
import { BUILTINS, KEYWORDS, TYPES } from "./consts";

export class IdSeq {
  private n = 0;
  next(): string {
    this.n += 1;
    return `n_${this.n}`;
  }
}

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

export const isObject = (v: JSONValue): v is Record<string, JSONValue> =>
  typeof v === "object" && v !== null && !Array.isArray(v);

export const isArray = (v: JSONValue): v is JSONValue[] => Array.isArray(v);

export const toText = (v: JSONValue): string => {
  if (isObject(v) || isArray(v)) return "";
  return String(v);
};

export const readLineIfAny = (v: JSONValue): number | null => {
  if (!isObject(v)) return null;
  const ln = v.Line;
  if (typeof ln === "number" && Number.isFinite(ln)) return ln;
  return null;
};

export const labelForObject = (
  key: string,
  obj: Record<string, JSONValue>,
): string => {
  const name = (obj.Name ?? obj.name) as JSONValue;
  const kind = (obj.kind ?? obj.Kind) as JSONValue;
  if (typeof name === "string" && name.length > 0) return `${key}: ${name}`;
  if (typeof kind === "string" && kind.length > 0) return `${key}: ${kind}`;
  return key;
};

export const mergeRange = (a: Range, b: Range): Range => {
  if (!a) return b;
  if (!b) return a;

  const startOffsetA = a.startOffset ?? Number.POSITIVE_INFINITY;
  const startOffsetB = b.startOffset ?? Number.POSITIVE_INFINITY;
  const startLineA = a.startLine ?? Number.POSITIVE_INFINITY;
  const startLineB = b.startLine ?? Number.POSITIVE_INFINITY;
  const startColumnA = a.startColumn ?? Number.POSITIVE_INFINITY;
  const startColumnB = b.startColumn ?? Number.POSITIVE_INFINITY;

  const useAAsStart =
    startOffsetA < startOffsetB ||
    (startOffsetA === startOffsetB &&
      (startLineA < startLineB ||
        (startLineA === startLineB && startColumnA <= startColumnB)));

  const endOffsetA = a.endOffset ?? Number.NEGATIVE_INFINITY;
  const endOffsetB = b.endOffset ?? Number.NEGATIVE_INFINITY;
  const endLineA = a.endLine ?? Number.NEGATIVE_INFINITY;
  const endLineB = b.endLine ?? Number.NEGATIVE_INFINITY;
  const endColumnA = a.endColumn ?? Number.NEGATIVE_INFINITY;
  const endColumnB = b.endColumn ?? Number.NEGATIVE_INFINITY;

  const useAAsEnd =
    endOffsetA > endOffsetB ||
    (endOffsetA === endOffsetB &&
      (endLineA > endLineB ||
        (endLineA === endLineB && endColumnA >= endColumnB)));

  return {
    startLine: useAAsStart ? a.startLine : b.startLine,
    endLine: useAAsEnd ? a.endLine : b.endLine,
    startColumn: useAAsStart ? a.startColumn : b.startColumn,
    endColumn: useAAsEnd ? a.endColumn : b.endColumn,
    startOffset: useAAsStart ? a.startOffset : b.startOffset,
    endOffset: useAAsEnd ? a.endOffset : b.endOffset,
  };
};

const rangeFromLine = (
  line: number | null,
  startColumn?: number | null,
  endColumn?: number | null,
): Range => {
  if (typeof line !== "number" || !Number.isFinite(line) || line <= 0)
    return null;
  const normalizedStartColumn =
    typeof startColumn === "number" && startColumn > 0 ? startColumn : null;
  const normalizedEndColumn =
    typeof endColumn === "number" && endColumn > 0
      ? endColumn
      : normalizedStartColumn;
  return {
    startLine: line,
    endLine: line,
    startColumn: normalizedStartColumn,
    endColumn: normalizedEndColumn,
    startOffset: null,
    endOffset: null,
  };
};

export const readColumnPair = (
  v: JSONValue,
): { start: number | null; end: number | null } => {
  if (!isObject(v)) return { start: null, end: null };
  const rawStart = v.ColumnStart ?? v.Column ?? null;
  const rawEnd = v.ColumnEnd ?? v.Column ?? null;
  const start =
    typeof rawStart === "number" && Number.isFinite(rawStart) ? rawStart : null;
  const endCandidate =
    typeof rawEnd === "number" && Number.isFinite(rawEnd) ? rawEnd : null;
  const end = endCandidate ?? start;
  return { start, end };
};

export const makeRangeFromSpan = (
  start: number | null | undefined,
  end: number | null | undefined,
  fallbackLine: number | null | undefined,
  text: string,
  lineStarts: number[],
): Range => {
  const hasStart = typeof start === "number" && Number.isFinite(start);
  const hasEnd = typeof end === "number" && Number.isFinite(end);

  if (
    typeof fallbackLine !== "number" ||
    !Number.isFinite(fallbackLine) ||
    fallbackLine <= 0
  ) {
    return null;
  }

  if (!hasStart || !hasEnd) {
    return rangeFromLine(fallbackLine, null, null);
  }

  const lineIndex = fallbackLine - 1;
  if (lineIndex < 0 || lineIndex >= lineStarts.length) {
    return rangeFromLine(fallbackLine, null, null);
  }

  const lineStartOffset = lineStarts[lineIndex]!;
  const startCol = start ?? 1;
  const endCol = end ?? startCol;
  const absStart = lineStartOffset + (startCol - 1);
  const absEnd = lineStartOffset + (endCol - 1);
  const clampedAbsStart = Math.max(0, Math.min(absStart, text.length));

  return {
    startLine: fallbackLine,
    endLine: fallbackLine,
    startColumn: startCol,
    endColumn: endCol,
    startOffset: clampedAbsStart,
    endOffset: Math.max(clampedAbsStart, Math.min(absEnd, text.length)),
  };
};

export const readSpanRange = (
  value: JSONValue,
  text: string,
  lineStarts: number[],
  fallbackLine: number | null,
  fallbackStartColumn: number | null,
  fallbackEndColumn: number | null,
): Range => {
  if (!isObject(value))
    return rangeFromLine(fallbackLine, fallbackStartColumn, fallbackEndColumn);
  const rawStart = value.Start ?? value.start ?? null;
  const rawEnd = value.End ?? value.end ?? null;
  const start =
    typeof rawStart === "number" && Number.isFinite(rawStart) ? rawStart : null;
  const end =
    typeof rawEnd === "number" && Number.isFinite(rawEnd) ? rawEnd : null;
  const spanRange = makeRangeFromSpan(
    start,
    end,
    fallbackLine,
    text,
    lineStarts,
  );
  if (spanRange) return spanRange;
  return rangeFromLine(fallbackLine, fallbackStartColumn, fallbackEndColumn);
};

export const tokenizeLine = (line: string): SyntaxToken[] => {
  const tokens: SyntaxToken[] = [];
  const pattern =
    /\/\/.*$|"(?:[^"\\]|\\.)*"|\b\d+(?:\.\d+)?\b|:=|<=|>=|<>|[+\-*/=<>]|[A-Za-z_][A-Za-z0-9_]*|[()[\]{},.:;]|\s+|./g;
  let match: RegExpExecArray | null;
  while ((match = pattern.exec(line)) !== null) {
    const token = match[0];
    if (token.startsWith("//")) {
      tokens.push({ text: token, className: "tok-comment" });
      break;
    }
    if (/^\s+$/.test(token)) {
      tokens.push({ text: token });
      continue;
    }
    if (token.startsWith('"')) {
      tokens.push({ text: token, className: "tok-string" });
      continue;
    }
    if (/^[+\-]?\d+(?:\.\d+)?$/.test(token)) {
      tokens.push({ text: token, className: "tok-number" });
      continue;
    }
    if (KEYWORDS.has(token.toLowerCase())) {
      tokens.push({ text: token, className: "tok-keyword" });
      continue;
    }
    if (TYPES.has(token)) {
      tokens.push({ text: token, className: "tok-type" });
      continue;
    }
    if (BUILTINS.has(token)) {
      tokens.push({ text: token, className: "tok-builtin" });
      continue;
    }
    if (/^:=|<=|>=|<>|[+\-*/=<>]$/.test(token)) {
      tokens.push({ text: token, className: "tok-operator" });
      continue;
    }
    if (/^[()[\]{},.:;]$/.test(token)) {
      tokens.push({ text: token, className: "tok-punctuation" });
      continue;
    }
    tokens.push({ text: token, className: "tok-identifier" });
  }
  return tokens;
};

export const readDiagnostics = (payload: PipelineOutput): Diagnostic[] => {
  const allDiags: Diagnostic[] = [
    ...(payload.Semantic?.Errors ?? []),
    ...(payload.Semantic?.Warnings ?? []),
  ];
  if (payload.StageError) allDiags.push(payload.StageError);
  allDiags.sort((a, b) => a.Line - b.Line);
  return allDiags;
};

export const buildTree = (
  value: JSONValue,
  key: string,
  ids: IdSeq,
  text: string,
  lineStarts: number[],
): TreeNode => {
  if (isArray(value)) {
    const id = ids.next();
    const children = value.map((child, i) =>
      buildTree(child, `${i}`, ids, text, lineStarts),
    );
    const childRange = children.reduce<Range>(
      (acc, c) => mergeRange(acc, c.range),
      null,
    );
    return {
      id,
      label: `${key}[${value.length}]`,
      line: null,
      columnStart: null,
      columnEnd: null,
      range: childRange,
      children,
    };
  }
  if (isObject(value)) {
    const id = ids.next();
    const entries = Object.entries(value);
    const children = entries.map(([k, v]) =>
      buildTree(v, k, ids, text, lineStarts),
    );
    const selfLine = readLineIfAny(value);
    const { start: selfColumnStart, end: selfColumnEnd } =
      readColumnPair(value);
    const childRange = children.reduce<Range>(
      (acc, c) => mergeRange(acc, c.range),
      null,
    );
    const ownRange = readSpanRange(
      value,
      text,
      lineStarts,
      selfLine,
      selfColumnStart,
      selfColumnEnd,
    );
    const fullRange = mergeRange(childRange, ownRange);
    const derivedLine = ownRange?.startLine ?? selfLine;
    const derivedColumnStart = ownRange?.startColumn ?? selfColumnStart;
    const derivedColumnEnd = ownRange?.endColumn ?? selfColumnEnd;
    return {
      id,
      label: labelForObject(key, value),
      line: derivedLine,
      columnStart: derivedColumnStart,
      columnEnd: derivedColumnEnd,
      range: fullRange,
      children,
    };
  }
  const id = ids.next();
  const stringValue = toText(value);
  return {
    id,
    label: `${key}: ${stringValue}`,
    line: null,
    columnStart: null,
    columnEnd: null,
    range: null,
    children: [],
  };
};

export const buildRoot = (
  ast: AstTree,
  text: string,
  lineStarts: number[],
): TreeNode => {
  const ids = new IdSeq();
  return buildTree(ast, "Root", ids, text, lineStarts);
};

export const flattenLines = (src: string): number[] => {
  const arr: number[] = [0];
  for (let i = 0; i < src.length; i += 1) if (src[i] === "\n") arr.push(i + 1);
  return arr;
};

export const scrollToLine = (
  line: number,
  source: string,
  editorRef: React.RefObject<HTMLDivElement | null>,
  columnStart?: number | null,
  columnEnd?: number | null,
): void => {
  if (line < 1) return;
  const lines = flattenLines(source);
  const idx = Math.min(line - 1, Math.max(0, lines.length - 1));
  const start = lines[idx]!;
  const end = idx + 1 < lines.length ? lines[idx + 1]! - 1 : source.length;
  const lineText = source.slice(start, end);
  const lineLength = lineText.length;
  const defaultEndCol = lineLength > 0 ? lineLength : 1;
  const hasExplicitStart =
    typeof columnStart === "number" &&
    Number.isFinite(columnStart) &&
    columnStart > 0;
  const startColumnRaw = hasExplicitStart ? columnStart : 1;
  const endColumnRawCandidate =
    typeof columnEnd === "number" && Number.isFinite(columnEnd) && columnEnd > 0
      ? columnEnd
      : undefined;
  const endColumnRaw =
    endColumnRawCandidate ??
    (hasExplicitStart ? startColumnRaw : defaultEndCol);
  const normalizedStartColumn = Math.max(
    1,
    Math.min(startColumnRaw, lineLength + 1),
  );
  const normalizedEndColumnInclusive = Math.max(
    normalizedStartColumn,
    Math.min(endColumnRaw, lineLength > 0 ? lineLength : normalizedStartColumn),
  );
  const selectionStart = start + (normalizedStartColumn - 1);
  const selectionEnd =
    lineLength === 0
      ? selectionStart
      : start + Math.min(lineLength, normalizedEndColumnInclusive);
  const ta = document.querySelector<HTMLTextAreaElement>("#code");
  if (ta) {
    ta.focus();
    ta.setSelectionRange(selectionStart, selectionEnd);
  }
  const scroller = editorRef.current;
  if (scroller) {
    const approxLineHeight = 24;
    scroller.scrollTo({
      top: Math.max(0, (idx - 4) * approxLineHeight),
      behavior: "smooth",
    });
  }
};
