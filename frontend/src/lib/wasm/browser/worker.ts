/// <reference lib="webworker" />
import { createWorkerCore, type StartMessage } from "../shared/workerCore";

const ctx = self as DedicatedWorkerGlobalScope;
const wrap =
  // eslint-disable-next-line @typescript-eslint/no-unsafe-function-type
  (fn: Function, name: string) =>
    (...args: unknown[]) => {
      // eslint-disable-next-line @typescript-eslint/no-unsafe-assignment, @typescript-eslint/no-unsafe-call
      const r = fn(...args);
      console.log(`IO.${name} called with args:`, args);
      // eslint-disable-next-line @typescript-eslint/no-unsafe-return
      return r;
    };
const { start } = createWorkerCore((m) => ctx.postMessage(m), wrap);

ctx.onmessage = (e: MessageEvent<StartMessage>) => {
  if (e.data?.type === "start") void start(e.data);
};

export {};
