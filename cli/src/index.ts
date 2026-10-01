#!/usr/bin/env node
import { Command } from "commander";
import { HoneError } from "./errors.js";
import { runInit } from "./init.js";

const program = new Command();
program.name("hone").description("Unity UI Toolkit 向けの UI コンポーネント集の CLI");

program
  .command("init")
  .description("hone.json と hone.manifest.json を作り、Core と theme を出力先にコピーする（既存のファイルは上書きしない）")
  .option("--registry <URL|パス>", "registry のベース（リポジトリのルートを指す URL かローカルパス）。hone.json が既にあればその registry を使う")
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
