#!/usr/bin/env node
import { Command } from "commander";
import { HoneError } from "./errors.js";
import { runInit } from "./init.js";

const program = new Command();
program.name("hone").description("Unity UI Toolkit 向けの UI コンポーネント集の CLI");

program
  .command("init")
  .description("hone.json を作り、Core と theme を出力先にコピーする")
  .option("--registry <URL|パス>", "registry のベース（リポジトリのルートを指す URL かローカルパス）")
  .action(async (opts: { registry?: string }) => {
    await runInit({ cwd: process.cwd(), registry: opts.registry });
  });

try {
  await program.parseAsync();
} catch (e) {
  if (!(e instanceof HoneError)) throw e;
  console.error(`エラー: ${e.message}`);
  process.exitCode = 1;
}
