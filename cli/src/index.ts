#!/usr/bin/env node
import { Command } from "commander";
import { runAdd } from "./add.js";
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

program
  .command("add")
  .description("コンポーネントを出力先にコピーし、依存を解決して HoneTheme.tss に @import を挿入する（コピー済みのファイルは上書きしない）。`add font <lang...>` は Noto Sans を取得して Fonts/ に置く（対応 lang: ja, ko, zh-hans, zh-hant, ar, th）")
  .argument("[names...]", "追加するコンポーネント名（大文字小文字は区別しない）。省略すると追加できる一覧を表示する。`font <lang...>` はフォント（Noto Sans）を取得して配置する")
  .action(async (names: string[]) => {
    await runAdd({ cwd: process.cwd(), names });
  });

try {
  await program.parseAsync();
} catch (e) {
  if (!(e instanceof HoneError)) throw e;
  console.error(`エラー: ${e.message}`);
  process.exitCode = 1;
}
