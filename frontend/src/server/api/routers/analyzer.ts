import { z } from "zod";
import { exec } from "child_process";
import { mkdtemp, writeFile, rm } from "fs/promises";
import { tmpdir } from "os";
import { join } from "path";
import { promisify } from "util";
import { createTRPCRouter, publicProcedure } from "@/server/api/trpc";
import { TRPCError } from "@trpc/server";

const execPromise = promisify(exec);

type JSONPrimitive = string | number | boolean | null;
export type JSONValue =
  | JSONPrimitive
  | { [key: string]: JSONValue }
  | JSONValue[];
export type AstTree = Record<string, JSONValue>;

export type Diagnostic = {
  Stage: "Lex" | "Parse" | "Semantic" | "Optimize";
  Line: number;
  Message: string;
  Severity: "Info" | "Warning" | "Error";
  Start?: number | null;
  End?: number | null;
};

export type PipelineOutput = {
  Tokens: Array<{
    Type: string;
    Lexeme: string;
    Start: number;
    End: number;
    Line: number;
  }>;
  Ast: AstTree | null;
  Semantic: { Errors: Diagnostic[]; Warnings: Diagnostic[] };
  OptimizedAst: AstTree | null;
  StageError: Diagnostic | null;
  Optimizations: OptimizationStep[] | null;
  OptimizedSource?: string | null;
  WasmModuleBase64?: string | null;
};

export type OptimizationStep = {
  Kind:
    | "InlineFunction"
    | "ConstantFold"
    | "IfSimplify"
    | "WhileEliminate"
    | "RemoveUnusedVar"
    | "UnreachableElimination"
    | "ConstructorLiteralElide"
    | "Other";
  Message: string;
  Line: number;
  Before: string | null;
  After: string | null;
  Start?: number | null;
  End?: number | null;
};

export const analyzerRouter = createTRPCRouter({
  analyze: publicProcedure
    .input(z.object({ source: z.string() }))
    .mutation(async ({ input }) => {
      const tempDir = await mkdtemp(join(tmpdir(), "compiler-"));
      try {
        const tempFilePath = join(tempDir, "source.toy");
        await writeFile(tempFilePath, input.source);
        let stdout: string;
        try {
          const result = await execPromise(
            `${process.env.COMPILER_PATH} -i ${tempFilePath}`,
            { cwd: process.cwd(), maxBuffer: 1024 * 1024 * 10 },
          );
          stdout = result.stdout;
        } catch (e) {
          if (e instanceof Error && "stderr" in e) {
            throw new TRPCError({
              code: "BAD_REQUEST",
              message: (e as { stderr: string }).stderr,
            });
          }
          throw e;
        }
        const payload = JSON.parse(stdout) as PipelineOutput;
        return payload;
      } finally {
        await rm(tempDir, { recursive: true, force: true });
      }
    }),
});
