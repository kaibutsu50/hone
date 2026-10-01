import { mkdtemp, mkdir, readFile, readdir, rm, stat, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { runInit } from "../src/init.js";

// リポジトリのルート（registry.json と registry/ がある階層）。ローカル registry として使う
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");

let tmp: string;

beforeEach(async () => {
  tmp = await mkdtemp(path.join(tmpdir(), "hone-cli-"));
});

afterEach(async () => {
  await rm(tmp, { recursive: true, force: true });
});

async function makeUnityProject(): Promise<string> {
  const root = path.join(tmp, "project");
  await mkdir(path.join(root, "Assets"), { recursive: true });
  await mkdir(path.join(root, "ProjectSettings"), { recursive: true });
  return root;
}

async function listFiles(dir: string): Promise<string[]> {
  const entries = await readdir(dir, { recursive: true, withFileTypes: true });
  return entries
    .filter((e) => e.isFile())
    .map((e) => path.relative(dir, path.join(e.parentPath, e.name)).split(path.sep).join("/"))
    .sort();
}

// registry.json と registry/ を持つ最小のローカル registry を作る
async function makeRegistry(
  name: string,
  items: { name: string; files: string[] }[],
): Promise<string> {
  const base = path.join(tmp, name);
  await mkdir(path.join(base, "registry"), { recursive: true });
  const json = {
    items: items.map((i) => ({
      name: i.name,
      type: "registry:lib",
      files: i.files.map((p) => ({ path: p, type: "registry:lib" })),
    })),
  };
  await writeFile(path.join(base, "registry.json"), JSON.stringify(json));
  for (const item of items) {
    for (const p of item.files) {
      const dest = path.join(base, "registry", p);
      // "../x" のような path も、読める実ファイルを置く（拒否が読めないことによる失敗でないようにする）
      await mkdir(path.dirname(dest), { recursive: true });
      await writeFile(dest, "x");
    }
  }
  return base;
}

const silent = { log: () => {} };

describe("hone init", () => {
  it("(A)(D) Unity プロジェクトで init すると hone.json・Core・theme・manifest が作られる", async () => {
    const root = await makeUnityProject();

    await runInit({ cwd: root, registry: repoRoot, ...silent });

    expect(await listFiles(root)).toEqual([
      "Assets/Hone/Core/Editor/Hone.Core.Editor.asmdef",
      "Assets/Hone/Core/Runtime/BackStack.cs",
      "Assets/Hone/Core/Runtime/Core.uss",
      "Assets/Hone/Core/Runtime/FocusScope.cs",
      "Assets/Hone/Core/Runtime/Hone.Core.asmdef",
      "Assets/Hone/Core/Runtime/IDismissable.cs",
      "Assets/Hone/HoneTheme.tss",
      "Assets/Hone/Tokens.uss",
      "Assets/Hone/hone.manifest.json",
      "hone.json",
    ]);
    expect(JSON.parse(await readFile(path.join(root, "hone.json"), "utf8"))).toEqual({
      registry: repoRoot,
      output: "Assets/Hone",
    });
    expect(
      JSON.parse(await readFile(path.join(root, "Assets/Hone/hone.manifest.json"), "utf8")),
    ).toEqual({ version: 1, theme: "Assets/Hone/HoneTheme.tss", fonts: [] });
    // registry のファイルがそのまま（バイト単位で）コピーされる
    expect(await readFile(path.join(root, "Assets/Hone/Tokens.uss"))).toEqual(
      await readFile(path.join(repoRoot, "registry/Tokens.uss")),
    );
  });

  it("(A) 完了時に Hone > Sync の案内を表示する", async () => {
    const root = await makeUnityProject();
    const lines: string[] = [];

    await runInit({ cwd: root, registry: repoRoot, log: (m) => lines.push(m) });

    expect(lines.at(-1)).toContain("Hone > Sync");
  });

  it("(B) Unity プロジェクトでなければエラー終了し、ファイルを作らない", async () => {
    const root = path.join(tmp, "not-unity");
    await mkdir(root, { recursive: true });

    await expect(runInit({ cwd: root, registry: repoRoot, ...silent })).rejects.toThrow(
      "Unity プロジェクトのルートで実行してください",
    );
    expect(await listFiles(root)).toEqual([]);
  });

  it("(B) Assets/ だけで ProjectSettings/ が無くてもエラー終了する", async () => {
    const root = path.join(tmp, "half");
    await mkdir(path.join(root, "Assets"), { recursive: true });

    await expect(runInit({ cwd: root, registry: repoRoot, ...silent })).rejects.toThrow(
      "Unity プロジェクトのルートで実行してください",
    );
    expect(await listFiles(root)).toEqual([]);
  });

  it("(C) 2 回目の init は内容も更新時刻も変えない", async () => {
    const root = await makeUnityProject();
    await runInit({ cwd: root, registry: repoRoot, ...silent });
    const snapshot = async () =>
      Promise.all(
        (await listFiles(root)).map(async (f) => {
          const p = path.join(root, f);
          return [f, (await stat(p)).mtimeMs, (await readFile(p)).toString("base64")] as const;
        }),
      );
    const before = await snapshot();

    await runInit({ cwd: root, registry: repoRoot, ...silent });

    expect(await snapshot()).toEqual(before);
  });

  it("(C) 既存のファイルは上書きしない", async () => {
    const root = await makeUnityProject();
    await mkdir(path.join(root, "Assets/Hone"), { recursive: true });
    await writeFile(path.join(root, "Assets/Hone/Tokens.uss"), "/* 利用者が書き換えた */\n");

    await runInit({ cwd: root, registry: repoRoot, ...silent });

    expect(await readFile(path.join(root, "Assets/Hone/Tokens.uss"), "utf8")).toBe(
      "/* 利用者が書き換えた */\n",
    );
  });

  it("既存の hone.json は書き換えず、その registry と output を使う", async () => {
    const root = await makeUnityProject();
    const honeJson = JSON.stringify({ registry: repoRoot, output: "Assets/Vendor/Hone" });
    await writeFile(path.join(root, "hone.json"), honeJson);

    await runInit({ cwd: root, registry: "https://example.invalid/", ...silent });

    expect(await readFile(path.join(root, "hone.json"), "utf8")).toBe(honeJson);
    expect(await listFiles(path.join(root, "Assets/Vendor/Hone"))).toContain("Tokens.uss");
    expect(
      JSON.parse(await readFile(path.join(root, "Assets/Vendor/Hone/hone.manifest.json"), "utf8"))
        .theme,
    ).toBe("Assets/Vendor/Hone/HoneTheme.tss");
  });

  it("file: URL のローカル registry も受け付ける", async () => {
    const root = await makeUnityProject();

    await runInit({ cwd: root, registry: pathToFileURL(repoRoot).href, ...silent });

    expect(await listFiles(root)).toContain("Assets/Hone/Tokens.uss");
  });

  it("URL の registry は <base>/registry.json と <base>/registry/<path> から fetch する", async () => {
    const root = await makeUnityProject();
    const requested: string[] = [];
    const fetchMock = async (url: string | URL): Promise<Response> => {
      const u = String(url);
      requested.push(u);
      const rel = u.replace("https://example.test/hone/", "");
      try {
        return new Response(await readFile(path.join(repoRoot, rel)));
      } catch {
        return new Response("not found", { status: 404 });
      }
    };

    await runInit({
      cwd: root,
      registry: "https://example.test/hone",
      fetch: fetchMock,
      ...silent,
    });

    expect(requested).toContain("https://example.test/hone/registry.json");
    expect(requested).toContain("https://example.test/hone/registry/Tokens.uss");
    expect(await listFiles(root)).toContain("Assets/Hone/Core/Runtime/Hone.Core.asmdef");
  });

  it("fetch が失敗したらエラー終了し、ファイルを作らない", async () => {
    const root = await makeUnityProject();
    const fetchMock = async (): Promise<Response> => new Response("nope", { status: 500 });

    await expect(
      runInit({ cwd: root, registry: "https://example.test/hone/", fetch: fetchMock, ...silent }),
    ).rejects.toThrow("500");
    expect(await listFiles(root)).toEqual([]);
  });

  it("項目名は大文字小文字を区別せずに照合する", async () => {
    const root = await makeUnityProject();
    const registry = await makeRegistry("lowercase-registry", [
      { name: "core", files: ["Core/Runtime/Hone.Core.asmdef"] },
      { name: "DEFAULT", files: ["Tokens.uss"] },
    ]);

    await runInit({ cwd: root, registry, ...silent });

    expect(await listFiles(root)).toContain("Assets/Hone/Core/Runtime/Hone.Core.asmdef");
    expect(await listFiles(root)).toContain("Assets/Hone/Tokens.uss");
  });

  it("registry に Core / Default が無ければエラー終了し、ファイルを作らない", async () => {
    const root = await makeUnityProject();
    const registry = await makeRegistry("empty-registry", []);

    await expect(runInit({ cwd: root, registry, ...silent })).rejects.toThrow("Core");
    expect(await listFiles(root)).toEqual([]);
  });

  it("output の外へ出る files[].path は拒否する", async () => {
    const root = await makeUnityProject();
    const registry = await makeRegistry("evil-registry", [
      { name: "Core", files: ["../escaped.txt"] },
      { name: "Default", files: [] },
    ]);

    await expect(runInit({ cwd: root, registry, ...silent })).rejects.toThrow("escaped.txt");
    expect(await listFiles(root)).toEqual([]);
  });
});
