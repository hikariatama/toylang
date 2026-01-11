"use client";

import { ASTPanel as ASTPanelBase } from "@/lib/components/AST";
import { CodeEditor } from "@/lib/components/CodeEditor";
import { Diagnostics } from "@/lib/components/Diagnostics";
import { Optimizations } from "@/lib/components/Optimizations";
import { Terminal } from "@/lib/components/Terminal";
import { TokensPanel } from "@/lib/components/TokensPanel";
import { motion } from "framer-motion";
import { Eye, EyeOff } from "lucide-react";
import { useEffect, useMemo, useState, type JSX } from "react";
import RGL, { WidthProvider, type Layout } from "react-grid-layout";
import "react-grid-layout/css/styles.css";
import "react-resizable/css/styles.css";

const ReactGridLayout = WidthProvider(RGL);

const ASTPanel = ({ className }: { className?: string }) => {
  return <ASTPanelBase className={className} title="ast" />;
};

const ASTPanelOptimized = ({ className }: { className?: string }) => {
  return (
    <ASTPanelBase
      className={className}
      title="optimized ast"
      variant="optimized"
      emptyMessage="No Optimized AST"
    />
  );
};

const PANES = [
  {
    id: "editor",
    name: "Code Editor",
    component: CodeEditor,
  },
  {
    id: "terminal",
    name: "Terminal",
    component: Terminal,
  },
  {
    id: "diagnostics",
    name: "Diagnostics",
    component: Diagnostics,
  },
  {
    id: "tokens",
    name: "Tokens",
    component: TokensPanel,
  },
  {
    id: "ast",
    name: "AST",
    component: ASTPanel,
  },
  {
    id: "optimizedAst",
    name: "Optimized AST",
    component: ASTPanelOptimized,
  },
  {
    id: "optimizations",
    name: "Optimizations",
    component: Optimizations,
  },
] as const;

const COLS = 4;
const MAX_ROWS = 4;
const GRID_CAPACITY = COLS * MAX_ROWS;

const MIN_SIZES: Record<string, { w: number; h: number }> = {
  editor: { w: 3, h: 2 },
  terminal: { w: 2, h: 1 },
  diagnostics: { w: 2, h: 1 },
  tokens: { w: 1, h: 1 },
  ast: { w: 1, h: 2 },
  optimizedAst: { w: 1, h: 2 },
  optimizations: { w: 1, h: 2 },
};

const INITIAL_LAYOUT: Layout[] = [
  {
    i: "editor",
    x: 0,
    y: 0,
    w: 2,
    h: 4,
  },
  {
    i: "terminal",
    x: 2,
    y: 0,
    w: 2,
    h: 2,
  },
  {
    i: "diagnostics",
    x: 2,
    y: 2,
    w: 2,
    h: 2,
  },
  {
    i: "tokens",
    x: 0,
    y: 3,
    w: 2,
    h: 1,
  },
  {
    i: "ast",
    x: 2,
    y: 3,
    w: 2,
    h: 1,
  },
  {
    i: "optimizedAst",
    x: 0,
    y: 4,
    w: 2,
    h: 1,
  },
  {
    i: "optimizations",
    x: 2,
    y: 4,
    w: 2,
    h: 1,
  },
];

type SizedLayout = Layout & { minW: number; minH: number };
type PaneSize = { w: number; h: number };

const getMinSizeForId = (id: string): PaneSize => {
  const min = MIN_SIZES[id];
  if (min) {
    return min;
  }
  return { w: 1, h: 1 };
};

const createInitialPaneSizes = (): Record<string, PaneSize> => {
  const sizes: Record<string, PaneSize> = {};
  for (const item of INITIAL_LAYOUT) {
    sizes[item.i] = { w: item.w, h: item.h };
  }
  for (const pane of PANES) {
    if (!sizes[pane.id]) {
      const min = getMinSizeForId(pane.id);
      sizes[pane.id] = { w: min.w, h: min.h };
    }
  }
  return sizes;
};

const INITIAL_PANE_SIZES = createInitialPaneSizes();

const toSizedLayouts = (items: Layout[]): SizedLayout[] => {
  return items.map((item) => {
    const min = getMinSizeForId(item.i);
    const baseW = Math.max(1, Math.min(item.w, COLS));
    const baseH = Math.max(1, Math.min(item.h, MAX_ROWS));
    return {
      ...item,
      w: baseW,
      h: baseH,
      minW: min.w,
      minH: min.h,
    };
  });
};

const resizeItemsToFit = (items: Layout[]): Layout[] => {
  const sized = toSizedLayouts(items);
  let totalArea = sized.reduce((sum, item) => sum + item.w * item.h, 0);

  if (totalArea <= GRID_CAPACITY) {
    return sized.map((item) => {
      // eslint-disable-next-line @typescript-eslint/no-unused-vars
      const { minW, minH, ...rest } = item;
      return rest;
    });
  }

  const canShrinkToMin = (): boolean => {
    return sized.some((item) => {
      const minW = Math.max(1, item.minW);
      const minH = Math.max(1, item.minH);
      return item.w > minW || item.h > minH;
    });
  };

  while (totalArea > GRID_CAPACITY && canShrinkToMin()) {
    const candidate = sized.reduce<SizedLayout | undefined>((max, item) => {
      const minW = Math.max(1, item.minW);
      const minH = Math.max(1, item.minH);
      const shrinkable = item.w > minW || item.h > minH;
      if (!shrinkable) {
        return max;
      }
      if (!max) {
        return item;
      }
      const area = item.w * item.h;
      const maxArea = max.w * max.h;
      return area > maxArea ? item : max;
    }, undefined);

    if (!candidate) {
      break;
    }

    const minW = Math.max(1, candidate.minW);
    const minH = Math.max(1, candidate.minH);

    if (candidate.h > minH) {
      candidate.h -= 1;
      totalArea -= candidate.w;
    } else if (candidate.w > minW) {
      candidate.w -= 1;
      totalArea -= candidate.h;
    } else {
      break;
    }
  }

  while (totalArea > GRID_CAPACITY) {
    const candidate = sized.reduce<SizedLayout | undefined>((max, item) => {
      const shrinkable = item.w > 1 || item.h > 1;
      if (!shrinkable) {
        return max;
      }
      if (!max) {
        return item;
      }
      const area = item.w * item.h;
      const maxArea = max.w * max.h;
      return area > maxArea ? item : max;
    }, undefined);

    if (!candidate) {
      break;
    }

    if (candidate.h > 1) {
      candidate.h -= 1;
      totalArea -= candidate.w;
    } else if (candidate.w > 1) {
      candidate.w -= 1;
      totalArea -= candidate.h;
    } else {
      break;
    }
  }

  return sized.map((item) => {
    // eslint-disable-next-line @typescript-eslint/no-unused-vars
    const { minW, minH, ...rest } = item;
    return rest;
  });
};

const packLayout = (items: Layout[]): Layout[] => {
  const occupied: boolean[][] = Array.from({ length: MAX_ROWS }, () =>
    Array<boolean>(COLS).fill(false),
  );
  const result: Layout[] = [];

  for (const item of items) {
    const w = Math.max(1, Math.min(item.w, COLS));
    const h = Math.max(1, Math.min(item.h, MAX_ROWS));
    let placed = false;

    for (let y = 0; y < MAX_ROWS && !placed; y += 1) {
      for (let x = 0; x < COLS && !placed; x += 1) {
        if (x + w > COLS || y + h > MAX_ROWS) {
          continue;
        }

        let fits = true;
        for (let yy = y; yy < y + h && fits; yy += 1) {
          for (let xx = x; xx < x + w; xx += 1) {
            if (occupied[yy]![xx]) {
              fits = false;
              break;
            }
          }
        }

        if (!fits) {
          continue;
        }

        for (let yy = y; yy < y + h; yy += 1) {
          for (let xx = x; xx < x + w; xx += 1) {
            occupied[yy]![xx] = true;
          }
        }

        result.push({
          ...item,
          x,
          y,
          w,
          h,
        });
        placed = true;
      }
    }

    if (!placed) {
      outer: for (let y = 0; y < MAX_ROWS; y += 1) {
        for (let x = 0; x < COLS; x += 1) {
          if (!occupied[y]![x]) {
            occupied[y]![x] = true;
            result.push({
              ...item,
              x,
              y,
              w: 1,
              h: 1,
            });
            break outer;
          }
        }
      }
    }
  }

  return result;
};

const computeReflowedLayout = (
  activeIds: string[],
  currentLayout: Layout[],
  paneSizes: Record<string, PaneSize>,
): Layout[] => {
  const baseActive: Layout[] = activeIds.map((id) => {
    const preferred = paneSizes[id] ?? getMinSizeForId(id);
    const w = Math.max(1, Math.min(preferred.w, COLS));
    const h = Math.max(1, Math.min(preferred.h, MAX_ROWS));
    return {
      i: id,
      x: 0,
      y: 0,
      w,
      h,
    };
  });

  const resized = resizeItemsToFit(baseActive);
  const packedActive = packLayout(resized);
  const inactive = currentLayout.filter((item) => !activeIds.includes(item.i));

  return [...inactive, ...packedActive];
};

const INITIAL_ACTIVE_IDS: string[] = ["editor", "terminal", "diagnostics"];

const LayoutMgr = ({ rowHeight }: { rowHeight: number }): JSX.Element => {
  const [activePaneIds, setActivePaneIds] =
    useState<string[]>(INITIAL_ACTIVE_IDS);
  const [paneSizes, setPaneSizes] = useState<Record<string, PaneSize>>(
    () => INITIAL_PANE_SIZES,
  );
  const [layout, setLayout] = useState<Layout[]>(() =>
    computeReflowedLayout(
      INITIAL_ACTIVE_IDS,
      INITIAL_LAYOUT,
      INITIAL_PANE_SIZES,
    ),
  );

  const handleLayoutChange = (nextLayout: Layout[]): void => {
    setLayout((previousLayout) => {
      const updatedIds = nextLayout.map((item) => item.i);
      const unchanged = previousLayout.filter(
        (item) => !updatedIds.includes(item.i),
      );
      return [...unchanged, ...nextLayout];
    });
  };

  const handleResizeStop = (
    _layout: Layout[],
    _oldItem: Layout,
    newItem: Layout,
  ): void => {
    setPaneSizes((previous) => ({
      ...previous,
      [newItem.i]: { w: newItem.w, h: newItem.h },
    }));
  };

  const handleTogglePane = (id: string): void => {
    setActivePaneIds((previous) => {
      const isActive = previous.includes(id);
      const nextActive = isActive
        ? previous.filter((paneId) => paneId !== id)
        : [...previous, id];
      setLayout((current) =>
        computeReflowedLayout(nextActive, current, paneSizes),
      );
      return nextActive;
    });
  };

  const activeLayout = useMemo(
    () => layout.filter((item) => activePaneIds.includes(item.i)),
    [layout, activePaneIds],
  );

  return (
    <>
      <div className="h-full w-full flex-1">
        <ReactGridLayout
          layout={activeLayout}
          cols={COLS}
          maxRows={MAX_ROWS}
          rowHeight={rowHeight}
          margin={[8, 8]}
          containerPadding={[0, 0]}
          isResizable
          isDraggable
          onLayoutChange={handleLayoutChange}
          onResizeStop={handleResizeStop}
          autoSize
          draggableHandle=".drag-handle"
        >
          {PANES.filter((pane) => activePaneIds.includes(pane.id)).map(
            (pane) => {
              const PaneComponent = pane.component;
              return (
                <div key={pane.id}>
                  <PaneComponent className="h-full w-full" />
                </div>
              );
            },
          )}
        </ReactGridLayout>
      </div>
      <div className="flex items-center justify-center gap-2 overflow-x-auto border border-white/30 p-2">
        {PANES.map((pane) => (
          <button
            key={pane.id}
            type="button"
            onClick={() => handleTogglePane(pane.id)}
            className="flex cursor-pointer items-center gap-1 border border-white/30 px-2 py-0.5 text-[10px] font-light tracking-widest text-[#D4D4D4] uppercase transition-colors hover:bg-[#D4D4D4]/10 focus:outline-none focus-visible:ring-1 focus-visible:ring-[#D4D4D4]"
          >
            {activePaneIds.includes(pane.id) ? (
              <Eye className="size-3" />
            ) : (
              <EyeOff className="size-3" />
            )}
            {pane.name}
          </button>
        ))}
      </div>
    </>
  );
};

export default function Page(): JSX.Element {
  const [rowHeight, setRowHeight] = useState<number>(140);
  const [loading, setLoading] = useState<boolean>(true);

  useEffect(() => {
    const updateRowHeight = (): void => {
      const height = window.innerHeight - 79;
      const calculatedRowHeight = Math.max(Math.floor(height / 4) - 6, 100);
      setRowHeight(calculatedRowHeight);
      setTimeout(() => setLoading(false), 500);
    };

    updateRowHeight();
    window.addEventListener("resize", updateRowHeight);
    return () => {
      window.removeEventListener("resize", updateRowHeight);
    };
  }, []);

  return (
    <div className="flex h-screen w-screen flex-col gap-2 bg-[#111] p-4 text-[#D4D4D4]">
      <LayoutMgr rowHeight={rowHeight} />
      {loading && (
        <motion.div className="fixed inset-0 z-50 flex items-center justify-center bg-[#111]">
          <div className="braille-loader"></div>
        </motion.div>
      )}
    </div>
  );
}
