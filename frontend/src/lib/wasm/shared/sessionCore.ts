export type MessageType =
  | {
      type: "stdout";
      chunk: string;
    }
  | {
      type: "wait";
      kind: InputRequestKind | null;
    }
  | {
      type: "exit";
      result: number | null;
    }
  | {
      type: "error";
      message: string;
    };

export function validateMessage(m: unknown): m is MessageType {
  if (
    typeof m !== "object" ||
    m === null ||
    !("type" in m) ||
    typeof m.type !== "string"
  ) {
    return false;
  }
  switch (m.type) {
    case "stdout":
      return "chunk" in m && typeof m.chunk === "string";
    case "wait":
      return (
        "kind" in m &&
        (m.kind === null ||
          (typeof m.kind === "string" &&
            (m.kind === "char" || m.kind === "line")))
      );
    case "exit":
      return (
        "result" in m && (m.result === null || typeof m.result === "number")
      );
    case "error":
      return "message" in m && typeof m.message === "string";
    default:
      return false;
  }
}

export type InputRequestKind = "char" | "line";
export type Callbacks = {
  onOutput: (chunk: string) => void;
  onWaiting: (kind: InputRequestKind | null) => void;
  onExit: (code: number | null) => void;
  onError: (msg: string) => void;
};

export type ScreenDimensions = {
  width: number;
  height: number;
};

export type InstantiateOptions = {
  screen?: ScreenDimensions;
};

export type GenericWorker = {
  postMessage: (
    m: unknown,
    transfer?: Array<ArrayBuffer | SharedArrayBuffer | MessagePort>,
  ) => void;
  terminate: () => void;
  onMessage: (h: (m: MessageType) => void) => void;
  onError: (h: (e: Error) => void) => void;
};

const WAIT_NONE = 0,
  WAIT_CHAR = 1,
  WAIT_LINE = 2;
const CONTROL_SLOTS = 4;
const LINE_BUFFER_SIZE = 4096;
const enc = new TextEncoder();

export function createSessionCore(spawn: () => GenericWorker) {
  return async function instantiate(
    bytes: Uint8Array,
    cb: Callbacks,
    options?: InstantiateOptions,
  ) {
    const worker = spawn();

    let active = true;
    let waiting: InputRequestKind | null = null;
    let inputClosed = false;
    let inputBuffer = "";

    const controlBuffer = new SharedArrayBuffer(
      Int32Array.BYTES_PER_ELEMENT * CONTROL_SLOTS,
    );
    const controlView = new Int32Array(controlBuffer);
    const lineBuffer = new SharedArrayBuffer(LINE_BUFFER_SIZE);
    const lineView = new Uint8Array(lineBuffer);

    const notify = (k: InputRequestKind | null) => {
      if (waiting !== k) {
        waiting = k;
        cb.onWaiting(k);
      }
    };

    const complete = (): boolean => {
      if (!waiting || !active) return false;
      if (waiting === "char") {
        if (Atomics.load(controlView, 0) !== WAIT_CHAR) return false;
        if (inputBuffer.length === 0) {
          if (!inputClosed) return false;
          Atomics.store(controlView, 1, -1);
        } else {
          const cp = inputBuffer.codePointAt(0)!;
          const adv = cp > 0xffff ? 2 : 1;
          inputBuffer = inputBuffer.slice(adv);
          Atomics.store(controlView, 1, cp);
        }
        Atomics.store(controlView, 0, WAIT_NONE);
        Atomics.notify(controlView, 0, 1);
        notify(null);
        return true;
      }
      if (waiting === "line") {
        if (Atomics.load(controlView, 0) !== WAIT_LINE) return false;
        const max = Atomics.load(controlView, 2);
        let line: string | null = null;
        if (inputBuffer.length > 0) {
          const i = inputBuffer.indexOf("\n");
          if (i >= 0) {
            line = inputBuffer.slice(0, i);
            inputBuffer = inputBuffer.slice(i + 1);
          } else if (inputClosed) {
            line = inputBuffer;
            inputBuffer = "";
          }
        } else if (inputClosed) line = "";
        if (line === null) return false;

        const encoded = enc.encode(line);
        const safeMax = Math.max(0, Math.min(max, lineView.length - 1));
        const limit = Math.max(0, Math.min(encoded.length, safeMax));
        if (limit > 0) lineView.set(encoded.subarray(0, limit), 0);
        if (limit < lineView.length) lineView[limit] = 0;

        Atomics.store(controlView, 1, limit);
        Atomics.store(controlView, 0, WAIT_NONE);
        Atomics.notify(controlView, 0, 1);
        notify(null);
        return true;
      }
      return false;
    };

    worker.onMessage((data: MessageType) => {
      switch (data.type) {
        case "stdout":
          cb.onOutput(data.chunk);
          break;
        case "wait":
          notify(data.kind);
          complete();
          break;
        case "exit":
          active = false;
          notify(null);
          cb.onExit(data.result);
          break;
        case "error":
          active = false;
          notify(null);
          cb.onError(data.message);
          break;
      }
    });

    worker.onError((e: Error) => {
      active = false;
      notify(null);
      cb.onError(e.message ?? "worker error");
    });

    worker.postMessage(
      {
        type: "start",
        buffer: bytes.buffer,
        byteOffset: bytes.byteOffset,
        byteLength: bytes.byteLength,
        control: controlBuffer,
        lineBuffer,
        env: options,
      },
      [bytes.buffer],
    );

    const sendText = (t: string) => {
      if (!active || inputClosed) return;
      inputBuffer += t;
      complete();
    };
    const sendLine = (l: string) => sendText(`${l.replace(/\r\n?/g, "\n")}\n`);
    const signalEof = () => {
      inputClosed = true;
      complete();
    };
    const terminate = () => {
      active = false;
      worker.terminate();
    };

    return { sendLine, sendText, signalEof, terminate };
  };
}
