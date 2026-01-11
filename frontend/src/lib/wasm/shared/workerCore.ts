export type Post = (msg: Record<string, unknown>) => void;

export type StartMessage = {
  type: "start";
  buffer: ArrayBuffer;
  byteOffset: number;
  byteLength: number;
  control: SharedArrayBuffer;
  lineBuffer: SharedArrayBuffer;
  env?: {
    screen?: {
      width: number;
      height: number;
    };
  };
};

export function createWorkerCore(
  post: Post,
  // eslint-disable-next-line @typescript-eslint/no-unsafe-function-type
  wrap: (fn: Function, name: string) => (...args: unknown[]) => unknown,
) {
  let controlView: Int32Array | null = null;
  let lineBufferView: Uint8Array | null = null;
  let instanceMemory: WebAssembly.Memory | null = null;
  let wasmInstance: WebAssembly.Instance | null = null;
  let heapPointer = 0;
  let screenWidthChars = 80;
  let screenHeightChars = 24;
  const sleepWaitView = new Int32Array(new SharedArrayBuffer(4));

  const textDecoder = new TextDecoder();

  const WAIT_NONE = 0,
    WAIT_CHAR = 1,
    WAIT_LINE = 2;

  const MAP_NODE_KEY_OFFSET = 0,
    MAP_NODE_VALUE_OFFSET = 8,
    MAP_NODE_NEXT_OFFSET = 16,
    MAP_NODE_TAG_OFFSET = 20,
    MAP_NODE_SIZE = 24;
  const VALUE_TAG_INTEGER = 0,
    VALUE_TAG_BOOLEAN = 1,
    VALUE_TAG_REAL = 2,
    VALUE_TAG_STRING = 3,
    VALUE_TAG_ARRAY = 4,
    VALUE_TAG_INSTANCE = 5,
    VALUE_TAG_LIST = 6,
    VALUE_TAG_MAP = 7;

  const textEncoder = new TextEncoder();

  const getMemory = (): WebAssembly.Memory => {
    if (instanceMemory && instanceMemory.buffer.byteLength > 0)
      return instanceMemory;
    if (wasmInstance) {
      const exported = (wasmInstance.exports as Record<string, unknown>).memory;
      if (exported instanceof WebAssembly.Memory) {
        instanceMemory = exported;
        return instanceMemory;
      }
    }
    throw new Error("WASM memory is not available");
  };

  const waitForChar = (): number => {
    if (!controlView) throw new Error("Control view not initialised");
    Atomics.store(controlView, 0, WAIT_CHAR);
    post({ type: "wait", kind: "char" });
    Atomics.wait(controlView, 0, WAIT_CHAR);
    const value = Atomics.load(controlView, 1);
    Atomics.store(controlView, 0, WAIT_NONE);
    return value;
  };

  const waitForLine = (maxLength: number): number => {
    if (!controlView) throw new Error("Control view not initialised");
    Atomics.store(controlView, 2, maxLength);
    Atomics.store(controlView, 0, WAIT_LINE);
    post({ type: "wait", kind: "line" });
    Atomics.wait(controlView, 0, WAIT_LINE);
    const length = Atomics.load(controlView, 1);
    Atomics.store(controlView, 0, WAIT_NONE);
    return length;
  };

  const align = (v: number, a: number) => (v + a - 1) & ~(a - 1);
  const ensureCapacity = (m: WebAssembly.Memory, req: number) => {
    const cur = m.buffer.byteLength;
    if (req <= cur) return;
    m.grow(Math.ceil((req - cur) / 65536));
  };

  const readString = (ptr: number): string => {
    if (ptr <= 0) return "";
    const mem = getMemory();
    if (ptr + 4 > mem.buffer.byteLength) return `""`;
    const view = new DataView(mem.buffer);
    const length = view.getUint32(ptr, true);
    const start = ptr + 4;
    const end = start + length;
    if (end > mem.buffer.byteLength) return `""`;
    const bytes = new Uint8Array(mem.buffer, start, length);
    return `"${new TextDecoder().decode(new Uint8Array(bytes)).replaceAll('"', '\\"')}"`;
  };

  const LIST_HEADER_SIZE = 12,
    LIST_CELL_SIZE = 16,
    CELL_TAG_OFFSET = 12;

  function formatList(ptr: number, depth = 0): string {
    if (ptr === 0) return "[]";
    if (depth > 64) return "[...]";
    const view = new DataView(getMemory().buffer);
    const count = view.getInt32(ptr + 4, true);
    const base = ptr + LIST_HEADER_SIZE;
    const n = count > 0 ? count : 0;
    const parts: string[] = [];
    for (let i = 0; i < n; i++) {
      const off = i * LIST_CELL_SIZE;
      const elemTag = view.getInt32(base + off + CELL_TAG_OFFSET, true);
      parts.push(formatTaggedValue(view, base, off, elemTag, depth + 1));
    }
    return `[${parts.join(", ")}]`;
  }

  const formatArray = (ptr: number): string => {
    if (ptr <= 0) return "[]";
    const mem = getMemory();
    if (ptr + 16 > mem.buffer.byteLength) return "[]";
    const view = new DataView(mem.buffer);
    const length = Math.max(0, view.getInt32(ptr, true));
    const tag = view.getInt32(ptr + 4, true);
    const dataPtr = view.getInt32(ptr + 8, true);
    const elementSize = Math.max(1, view.getInt32(ptr + 12, true));
    if (length === 0) return "[]";
    const parts: string[] = [];
    for (let i = 0; i < length; i++) {
      const addr = dataPtr + i * elementSize;
      if (addr < 0 || addr + elementSize > mem.buffer.byteLength) {
        parts.push("0");
        continue;
      }
      switch (tag) {
        case 1:
          parts.push(view.getInt32(addr, true) !== 0 ? "true" : "false");
          break;
        case 2:
          parts.push(
            elementSize >= 8 ? view.getFloat64(addr, true).toString() : "0",
          );
          break;
        case 3:
          parts.push(readString(view.getInt32(addr, true)));
          break;
        case 4:
          parts.push(formatArray(view.getInt32(addr, true)));
          break;
        default:
          parts.push(view.getInt32(addr, true).toString(10));
      }
    }
    return `[${parts.join(", ")}]`;
  };

  const looksLikeStringPointer = (view: DataView, ptr: number) => {
    if (ptr <= 0) return false;
    if (ptr + 4 > view.byteLength) return false;
    const len = view.getUint32(ptr, true);
    if (len < 0 || len > 1_048_576) return false;
    return ptr + 4 + len <= view.byteLength;
  };

  function formatTaggedValue(
    view: DataView,
    basePtr: number,
    offset: number,
    tag: number,
    depth: number,
  ): string {
    const address = basePtr + offset;
    const within = (size: number) =>
      address >= 0 && address + size <= view.byteLength;
    switch (tag) {
      case VALUE_TAG_BOOLEAN:
        return within(4) && view.getInt32(address, true) !== 0
          ? "true"
          : "false";
      case VALUE_TAG_REAL:
        return within(8) ? view.getFloat64(address, true).toString() : "0";
      case VALUE_TAG_STRING:
        return within(4) ? readString(view.getInt32(address, true)) : "";
      case VALUE_TAG_ARRAY:
        return within(4) ? formatArray(view.getInt32(address, true)) : "[]";
      case VALUE_TAG_LIST:
        return within(4)
          ? formatList(view.getInt32(address, true), depth + 1)
          : "[]";
      case VALUE_TAG_MAP:
        return within(4)
          ? formatMap(view.getInt32(address, true), depth + 1)
          : "{}";
      case VALUE_TAG_INSTANCE:
        return within(4)
          ? `#<instance ${view.getInt32(address, true).toString(16)}>`
          : "<instance>";
      default: {
        if (!within(4)) return "0";
        const raw = view.getInt32(address, true);
        if (tag !== VALUE_TAG_INTEGER) {
          if (looksLikeStringPointer(view, raw)) return readString(raw);
          return raw.toString(10);
        }
        return raw.toString(10);
      }
    }
  }

  function formatMap(ptr: number, depth = 0): string {
    if (ptr === 0) return "{}";
    if (depth > 64) return "{...}";
    const mem = getMemory();
    const buf = mem.buffer;
    const view = new DataView(buf);
    const parts: string[] = [];
    let cur = ptr;
    let guard = 0;
    while (cur !== 0 && guard < 4096) {
      if (cur < 0 || cur + MAP_NODE_SIZE > buf.byteLength) {
        parts.push("?:?");
        break;
      }
      const tagInfo = view.getInt32(cur + MAP_NODE_TAG_OFFSET, true);
      let kt = tagInfo & 0xffff;
      let vt = (tagInfo >>> 16) & 0xffff;
      if (kt === VALUE_TAG_INTEGER) {
        const c = view.getInt32(cur + MAP_NODE_KEY_OFFSET, true);
        if (looksLikeStringPointer(view, c)) kt = VALUE_TAG_STRING;
      }
      if (vt === VALUE_TAG_INTEGER) {
        const c = view.getInt32(cur + MAP_NODE_VALUE_OFFSET, true);
        if (looksLikeStringPointer(view, c)) vt = VALUE_TAG_STRING;
      }
      const k = formatTaggedValue(view, cur, MAP_NODE_KEY_OFFSET, kt, depth);
      const val = formatTaggedValue(
        view,
        cur,
        MAP_NODE_VALUE_OFFSET,
        vt,
        depth,
      );
      parts.push(`${k}: ${val}`);
      cur = view.getInt32(cur + MAP_NODE_NEXT_OFFSET, true);
      guard += 1;
    }
    if (cur !== 0) parts.push("...");
    return `{${parts.join(", ")}}`;
  }

  const writeString = (text: string): number => {
    const mem = getMemory();
    const dv = new DataView(mem.buffer);
    const trackedTop = dv.getUint32(0, true);
    if (trackedTop > heapPointer) heapPointer = trackedTop;

    const bytes = textEncoder.encode(text);
    const total = 4 + bytes.length + 1;
    const base = align(heapPointer, 4);
    ensureCapacity(mem, base + total);

    dv.setUint32(base, bytes.length, true);
    const all = new Uint8Array(mem.buffer);
    if (bytes.length > 0) all.set(bytes, base + 4);
    all[base + 4 + bytes.length] = 0;

    heapPointer = base + total;
    dv.setUint32(0, heapPointer, true);
    return base;
  };

  const io = {
    PrintInteger: (v: number) =>
      post({ type: "stdout", chunk: v.toString(10) }),
    PrintReal: (v: number) => post({ type: "stdout", chunk: v.toString() }),
    PrintBool: (v: number) =>
      post({ type: "stdout", chunk: v !== 0 ? "true" : "false" }),
    PrintString: (ptr: number, len: number) => {
      if (len <= 0) return;
      const mem = getMemory();
      if (ptr < 0 || ptr + len > mem.buffer.byteLength) return;
      const view = new Uint8Array(mem.buffer, ptr, len);
      post({
        type: "stdout",
        chunk: new TextDecoder().decode(new Uint8Array(view)),
      });
    },
    PrintLine: (t?: string) =>
      post({ type: "stdout", chunk: t ? t + "\n" : "\n" }),
    Read: () => waitForChar(),
    ReadLine: () => {
      if (!lineBufferView) throw new Error("Line buffer not initialised");
      const maxLength = lineBufferView.length;
      if (maxLength <= 0) return writeString("");
      const len = waitForLine(maxLength);
      if (len <= 0) return writeString("");
      const copy = new Uint8Array(len);
      copy.set(lineBufferView.subarray(0, len));
      const text = textDecoder.decode(copy);
      return writeString(text);
    },
    ReadInteger: () => {
      const len = waitForLine(32);
      if (len <= 0) throw new Error("Failed to read integer: no input");
      if (!lineBufferView) throw new Error("Line buffer not initialised");
      const input = new TextDecoder()
        .decode(new Uint8Array(lineBufferView.subarray(0, len)))
        .trim();
      const value = parseInt(input, 10);
      if (Number.isNaN(value))
        throw new Error(`Failed to read integer: invalid input "${input}"`);
      return value;
    },
    ReadReal: () => {
      const len = waitForLine(64);
      if (len <= 0) throw new Error("Failed to read real: no input");
      if (!lineBufferView) throw new Error("Line buffer not initialised");
      const input = new TextDecoder()
        .decode(new Uint8Array(lineBufferView.subarray(0, len)))
        .trim();
      const value = parseFloat(input);
      if (Number.isNaN(value))
        throw new Error(`Failed to read real: invalid input "${input}"`);
      return value;
    },
    ReadBool: () => {
      const len = waitForLine(8);
      if (len <= 0) throw new Error("Failed to read boolean: no input");
      if (!lineBufferView) throw new Error("Line buffer not initialised");
      const input = new TextDecoder()
        .decode(new Uint8Array(lineBufferView.subarray(0, len)))
        .trim()
        .toLowerCase();
      if (input === "true") return 1;
      if (input === "false") return 0;
      throw new Error(`Failed to read boolean: invalid input "${input}"`);
    },
    FormatInteger: (v: number) => writeString(v.toString(10)),
    FormatReal: (v: number) => writeString(v.toString()),
    FormatBool: (v: number) => writeString(v !== 0 ? "true" : "false"),
    PrintArray: (p: number) => post({ type: "stdout", chunk: formatArray(p) }),
    PrintList: (p: number) => post({ type: "stdout", chunk: formatList(p) }),
    PrintMap: (p: number) => post({ type: "stdout", chunk: formatMap(p) }),
  };

  // eslint-disable-next-line @typescript-eslint/no-unsafe-function-type
  const ioImportsWrapped: Record<string, Function> = {};
  for (const [k, f] of Object.entries(io)) ioImportsWrapped[k] = wrap(f, k);

  const math = {
    Cos: (v: number) => Math.cos(v),
    Sin: (v: number) => Math.sin(v),
    Tan: (v: number) => Math.tan(v),
    Acos: (v: number) => Math.acos(v),
    Asin: (v: number) => Math.asin(v),
    Atan: (v: number) => Math.atan(v),
    Atan2: (y: number, x: number) => Math.atan2(y, x),
    Exp: (v: number) => Math.exp(v),
    Log: (v: number) => Math.log(v),
    Sqrt: (v: number) => Math.sqrt(v),
    Pow: (base: number, exponent: number) => Math.pow(base, exponent),
    Random: () => Math.random(),
  } as const;

  // eslint-disable-next-line @typescript-eslint/no-unsafe-function-type
  const mathImportsWrapped: Record<string, Function> = {};
  for (const [k, f] of Object.entries(math)) mathImportsWrapped[k] = wrap(f, k);

  const monotonicSeconds = () =>
    typeof performance !== "undefined" && typeof performance.now === "function"
      ? performance.now() / 1000
      : Date.now() / 1000;

  const time = {
    Sleep: (seconds: number) => {
      if (!Number.isFinite(seconds)) return;
      const millis = Math.min(0x7fffffff, Math.max(0, seconds * 1000));
      if (millis <= 0) return;
      Atomics.wait(sleepWaitView, 0, 0, millis);
    },
    PerfCounter: () => monotonicSeconds(),
    Unix: () => Date.now() / 1000,
  } as const;

  // eslint-disable-next-line @typescript-eslint/no-unsafe-function-type
  const timeImportsWrapped: Record<string, Function> = {};
  for (const [k, f] of Object.entries(time)) timeImportsWrapped[k] = wrap(f, k);

  const start = async (msg: StartMessage) => {
    controlView = new Int32Array(msg.control);
    lineBufferView = new Uint8Array(msg.lineBuffer);
    screenWidthChars = msg.env?.screen?.width ?? 80;
    screenHeightChars = msg.env?.screen?.height ?? 24;
    const importObject: WebAssembly.Imports = {
      io: ioImportsWrapped,
      math: mathImportsWrapped,
      screen: {
        Width: wrap(() => screenWidthChars, "Screen.Width"),
        Height: wrap(() => screenHeightChars, "Screen.Height"),
      },
      time: timeImportsWrapped,
    };

    try {
      const slice = msg.buffer.slice(
        msg.byteOffset,
        msg.byteOffset + msg.byteLength,
      );
      const instantiation = await WebAssembly.instantiate(
        new Uint8Array(slice),
        importObject,
      );
      wasmInstance =
        instantiation instanceof WebAssembly.Instance
          ? instantiation
          : instantiation.instance;
      const exports = wasmInstance.exports as Record<string, unknown>;
      if (exports.memory instanceof WebAssembly.Memory) {
        instanceMemory = exports.memory;
      }
      const heapView = new DataView(getMemory().buffer);
      heapPointer = heapView.getUint32(0, true);
      const main = exports.Main;
      const result =
        typeof main === "function" ? (main as () => number | void)() : null;
      post({
        type: "exit",
        result: typeof result === "number" ? result : null,
      });
    } catch (err) {
      post({
        type: "error",
        message: err instanceof Error ? err.message : String(err),
      });
    }
  };

  return { start };
}
