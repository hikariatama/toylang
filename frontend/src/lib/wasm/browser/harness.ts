import {
  createSessionCore,
  validateMessage,
  type InstantiateOptions,
} from "../shared/sessionCore";

const instantiate = createSessionCore(() => {
  const w = new Worker(new URL("./worker.ts", import.meta.url), {
    type: "module",
  });
  return {
    postMessage: (m, transfer) => w.postMessage(m, { transfer }),
    terminate: () => w.terminate(),
    onMessage: (h) => {
      w.onmessage = (e) => {
        const m = e.data as unknown;
        if (validateMessage(m)) h(m);
      };
    },
    onError: (h) => {
      w.onerror = (e) => h(new Error(e.message));
    },
  };
});

export type { InputRequestKind } from "../shared/sessionCore";
export type WasmSessionCallbacks = Parameters<typeof instantiate>[1];
export type WasmSession = Awaited<
  ReturnType<ReturnType<typeof createSessionCore>>
>;
export type { InstantiateOptions } from "../shared/sessionCore";

export async function instantiateWithIo(
  bytes: Uint8Array,
  cb: WasmSessionCallbacks,
  options?: InstantiateOptions,
) {
  return instantiate(bytes, cb, options);
}
