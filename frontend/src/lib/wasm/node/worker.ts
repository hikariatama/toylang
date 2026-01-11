import { parentPort } from "node:worker_threads";
import { createWorkerCore, type StartMessage } from "../shared/workerCore.ts";

const wrap =
  // eslint-disable-next-line @typescript-eslint/no-unsafe-function-type
  (fn: Function) =>
    (...args: unknown[]) => {
      // eslint-disable-next-line @typescript-eslint/no-unsafe-return, @typescript-eslint/no-unsafe-call
      return fn(...args);
    };
const { start } = createWorkerCore((m) => parentPort!.postMessage(m), wrap);
parentPort!.on("message", (msg: StartMessage) => {
  if (msg.type === "start") void start(msg);
});
