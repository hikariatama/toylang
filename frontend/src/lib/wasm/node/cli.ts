import { readFileSync } from "node:fs";
import { instantiateWithIoNode } from "./harness.ts";

const file = process.argv[2];
if (!file) process.exit(2);
const bytes = new Uint8Array(readFileSync(file));

const getTerminalDimensions = () => {
  const columns =
    typeof process.stdout.columns === "number" && process.stdout.columns > 0
      ? process.stdout.columns
      : 80;
  const rows =
    typeof process.stdout.rows === "number" && process.stdout.rows > 0
      ? process.stdout.rows
      : 24;
  return { width: columns, height: rows } as const;
};

const session = await instantiateWithIoNode(
  bytes,
  {
    onOutput: (s: string) => process.stdout.write(s),
    onWaiting: () => null,
    onExit: (code: number | null) => process.exit(code ?? 0),
    onError: (m: string) => {
      process.stderr.write(m + "\n");
      process.exit(1);
    },
  },
  { screen: getTerminalDimensions() },
);

process.stdin.setEncoding("utf8");
process.stdin.on("data", (d: string | Buffer) =>
  session.sendText(d.toString()),
);
process.stdin.on("end", () => session.signalEof());
