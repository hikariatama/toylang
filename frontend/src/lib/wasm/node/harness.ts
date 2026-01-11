import { Worker } from "node:worker_threads";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import {
  createSessionCore,
  validateMessage,
  type GenericWorker,
} from "../shared/sessionCore.ts";

const spawn = () => {
  const workerPath = join(
    dirname(fileURLToPath(import.meta.url)),
    "./worker.ts",
  );
  const w = new Worker(workerPath);
  return {
    postMessage: (m, transfer) => {
      if (transfer?.some((t) => t instanceof SharedArrayBuffer)) {
        throw new Error(
          "SharedArrayBuffer transfer is not supported in Node.js workers",
        );
      }
      if (transfer?.some((t) => t instanceof MessagePort)) {
        throw new Error(
          "MessagePort transfer is not supported in Node.js workers",
        );
      }
      return w.postMessage(m, transfer as ArrayBuffer[] | undefined);
    },
    terminate: () => void w.terminate(),
    onMessage: (h) =>
      w.on("message", (m: unknown) => {
        if (validateMessage(m)) h(m);
      }),
    onError: (h) => w.on("error", (e) => h(e)),
  } as GenericWorker;
};

export const instantiateWithIoNode = createSessionCore(spawn);
export type { InstantiateOptions } from "../shared/sessionCore";
