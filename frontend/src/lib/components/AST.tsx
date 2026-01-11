import type { Range, TreeNode } from "@/lib/types";
import { cn } from "@/lib/utils";
import { ChevronDown, ChevronRight, Link as LinkIcon } from "lucide-react";
import React, { useMemo, useRef, useState, type JSX } from "react";
import { useCompilerStore } from "@/lib/stores/useCompiler";
import { useEditorNavigation } from "@/lib/hooks/useEditorNavigation";
import { useStoreWithEqualityFn } from "zustand/traditional";
import { isEqual } from "lodash";
import { useVirtualizer } from "@tanstack/react-virtual";

type FlatNode = {
  node: TreeNode;
  depth: number;
};

type NodeRowProps = {
  node: TreeNode;
  depth: number;
  isExpanded: boolean;
  hasChildren: boolean;
  onToggle: () => void;
  onJump: (
    line: number,
    columnStart?: number | null,
    columnEnd?: number | null,
  ) => void;
  onHoverRange: (range: Range) => void;
};

const NodeRow = ({
  node,
  depth,
  isExpanded,
  hasChildren,
  onToggle,
  onJump,
  onHoverRange,
}: NodeRowProps): JSX.Element => {
  return (
    <div
      className={cn(
        "group flex items-center gap-2 rounded px-2 py-0.5 text-[9px] text-slate-200 hover:bg-white/5",
      )}
      style={{ paddingLeft: depth * 14 + 8 }}
      onMouseEnter={() => onHoverRange(node.range)}
      onMouseLeave={() => onHoverRange(null)}
    >
      <button
        type="button"
        onClick={hasChildren ? onToggle : undefined}
        className={cn(
          "grid h-3 w-3 place-items-center rounded hover:bg-white/10",
          !hasChildren && "opacity-0",
        )}
        aria-label={isExpanded ? "Collapse" : "Expand"}
      >
        {isExpanded ? (
          <ChevronDown className="h-2 w-2" />
        ) : (
          <ChevronRight className="h-2 w-2" />
        )}
      </button>
      <span className="truncate font-medium">{node.label}</span>
      {typeof node.line === "number" && (
        <button
          type="button"
          onClick={() => onJump(node.line!, node.columnStart, node.columnEnd)}
          className="ml-auto inline-flex items-center gap-1 rounded bg-white/10 px-1.5 py-0.5 text-[8px] text-slate-300 hover:bg-white/20"
        >
          <LinkIcon className="h-2 w-2" />
          <span>line {node.line}</span>
        </button>
      )}
    </div>
  );
};

export const ASTPanel = ({
  title,
  emptyMessage = "No AST",
  variant = "ast",
  className,
}: {
  title: string;
  emptyMessage?: string;
  variant?: "ast" | "optimized";
  className?: string;
}): JSX.Element => {
  const tree = useStoreWithEqualityFn(
    useCompilerStore,
    (state) => (variant === "optimized" ? state.optTree : state.tree),
    isEqual,
  );
  const { onJump } = useEditorNavigation();
  const setHoverRange = useCompilerStore((state) => state.setHoverRange);

  const [expanded, setExpanded] = useState<Record<string, boolean>>({});

  const flatNodes: FlatNode[] = useMemo(() => {
    if (!tree) return [];
    const result: FlatNode[] = [];

    const traverse = (node: TreeNode, depth: number): void => {
      result.push({ node, depth });
      const isExpanded = expanded[node.id] ?? true;
      if (!isExpanded) return;
      for (const child of node.children) {
        traverse(child, depth + 1);
      }
    };

    traverse(tree, 0);
    return result;
  }, [tree, expanded]);

  const containerRef = useRef<HTMLDivElement | null>(null);

  const rowVirtualizer = useVirtualizer({
    count: flatNodes.length,
    getScrollElement: () => containerRef.current,
    estimateSize: () => 18,
    overscan: 10,
  });

  const virtualItems = rowVirtualizer.getVirtualItems();
  const totalSize = rowVirtualizer.getTotalSize();

  const handleToggle = (id: string): void => {
    setExpanded((prev) => {
      const current = prev[id] ?? true;
      return { ...prev, [id]: !current };
    });
  };

  return (
    <div
      ref={containerRef}
      className={cn(
        "overflow-auto border border-white/30 font-mono",
        className,
      )}
    >
      <div className="font-title drag-handle sticky top-0 z-50 cursor-grab border-b border-white/30 bg-[#111] px-2 py-1 text-[10px] text-slate-300 select-none active:cursor-grabbing">
        {title}
      </div>
      {tree ? (
        <ul className="relative py-2" style={{ height: totalSize }}>
          {virtualItems.map((virtualRow) => {
            const { node, depth } = flatNodes[virtualRow.index]!;
            const hasChildren = node.children.length > 0;
            const isExpanded = expanded[node.id] ?? true;

            return (
              <li
                key={node.id}
                className="relative"
                style={{
                  position: "absolute",
                  top: 0,
                  left: 0,
                  width: "100%",
                  transform: `translateY(${virtualRow.start}px)`,
                }}
                ref={rowVirtualizer.measureElement}
              >
                <NodeRow
                  node={node}
                  depth={depth}
                  isExpanded={isExpanded}
                  hasChildren={hasChildren}
                  onToggle={() => handleToggle(node.id)}
                  onJump={onJump}
                  onHoverRange={setHoverRange}
                />
              </li>
            );
          })}
        </ul>
      ) : (
        <div className="grid h-full place-items-center text-xs text-slate-400">
          {emptyMessage}
        </div>
      )}
    </div>
  );
};
