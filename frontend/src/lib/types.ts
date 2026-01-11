export type Range = {
  startLine: number | null;
  endLine: number | null;
  startColumn: number | null;
  endColumn: number | null;
  startOffset: number | null;
  endOffset: number | null;
} | null;

export type TreeNode = {
  id: string;
  label: string;
  line: number | null;
  columnStart: number | null;
  columnEnd: number | null;
  range: Range;
  children: TreeNode[];
};

export type SyntaxToken = { text: string; className?: string };
