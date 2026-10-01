import { mkdtemp, mkdir, readFile, rm, stat, utimes, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { runAdd } from "../src/add.js";
import { HoneError } from "../src/errors.js";
import { runInit } from "../src/init.js";
import { listFiles, makeUnityProject as makeProject, repoRoot } from "./helpers.js";

// テスト名の (A)〜(F) は Issue #21 の受け入れ条件の記号

let tmp: string;

beforeEach(async () => {
  tmp = await mkdtemp(path.join(tmpdir(), "hone-cli-add-"));
});

afterEach(async () => {
  await rm(tmp, { recursive: true, force: true, maxRetries: 3 });
});

const silent = { log: () => {} };

// repoRoot の registry で init 済みの Unity プロジェクトを作る
async function makeInitializedProject(registry = repoRoot): Promise<string> {
  const root = await makeProject(tmp);
  await runInit({ cwd: root, registry, ...silent });
  return root;
}

interface FixtureItem {
  name: string;
  type?: string;
  description?: string;
  files: string[];
  registryDependencies?: string[];
  dependencies?: string[];
}

// Core と Default（init が使う）に items を足した、ローカル registry を作る
async function makeRegistry(
  items: FixtureItem[],
  { theme = '@import url("Tokens.uss");\n:root {}\n' } = {},
): Promise<string> {
  const base = path.join(tmp, "fixture-registry");
  const all: FixtureItem[] = [
    { name: "Core", type: "registry:lib", files: ["Core/Core.uss"] },
    { name: "Default", type: "registry:theme", files: ["Tokens.uss", "HoneTheme.tss"] },
    ...items,
  ];
  const json = {
    items: all.map((i) => ({
      ...i,
      type: i.type ?? "registry:ui",
      files: i.files.map((p) => ({ path: p, type: i.type ?? "registry:ui" })),
    })),
  };
  await mkdir(base, { recursive: true });
  await writeFile(path.join(base, "registry.json"), JSON.stringify(json));
  for (const item of all) {
    for (const p of item.files) {
      const dest = path.join(base, "registry", p);
      await mkdir(path.dirname(dest), { recursive: true });
      await writeFile(dest, p === "HoneTheme.tss" ? theme : `${item.name}:${p}`);
    }
  }
  return base;
}

const read = (root: string, file: string) => readFile(path.join(root, file), "utf8");
const readManifest = async (root: string) =>
  JSON.parse(await read(root, "Assets/Hone/hone.manifest.json"));

async function snapshot(root: string) {
  return Promise.all(
    (await listFiles(root)).map(async (f) => {
      const p = path.join(root, f);
      return [f, (await stat(p)).mtimeMs, (await readFile(p)).toString("base64")] as const;
    }),
  );
}

const BUTTON_FILES = [
  "Assets/Hone/UI/Button/Button.cs",
  "Assets/Hone/UI/Button/Button.uss",
  "Assets/Hone/UI/Button/Button.uxml",
  "Assets/Hone/UI/Button/README.md",
];

describe("hone add", () => {
  it("(A) button を追加すると UI/Button/ の 4 ファイルがコピーされ、最後の @import 行の直後に @import が入る", async () => {
    const root = await makeInitializedProject();
    const before = await read(root, "Assets/Hone/HoneTheme.tss");

    await runAdd({ cwd: root, names: ["button"], ...silent });

    const files = await listFiles(root);
    for (const f of BUTTON_FILES) expect(files).toContain(f);
    // registry のファイルがそのまま（バイト単位で）コピーされる
    expect(await readFile(path.join(root, "Assets/Hone/UI/Button/Button.uss"))).toEqual(
      await readFile(path.join(repoRoot, "registry/UI/Button/Button.uss")),
    );
    const eol = before.includes("\r\n") ? "\r\n" : "\n";
    const lastImport = '@import url("Core/Runtime/Core.uss");';
    expect(await read(root, "Assets/Hone/HoneTheme.tss")).toBe(
      before.replace(lastImport, `${lastImport}${eol}@import url("UI/Button/Button.uss");`),
    );
    expect((await readManifest(root)).components).toEqual(["Button"]);
  });

  it("(B) dialog を追加すると、依存の Button を先にコピーし、@import も Button が先に入る", async () => {
    const root = await makeInitializedProject();
    const lines: string[] = [];

    await runAdd({ cwd: root, names: ["dialog"], log: (m) => lines.push(m) });

    const created = lines.filter((l) => l.startsWith("作成: "));
    expect(created.findIndex((l) => l.includes("UI/Button/Button.cs"))).toBeGreaterThanOrEqual(0);
    expect(created.findIndex((l) => l.includes("UI/Button/Button.cs"))).toBeLessThan(
      created.findIndex((l) => l.includes("UI/Dialog/Dialog.cs")),
    );
    // init 済みの Core は何も表示せず飛ばす
    expect(lines.filter((l) => l.includes("Core/"))).toEqual([]);
    const theme = await read(root, "Assets/Hone/HoneTheme.tss");
    expect(theme.indexOf('@import url("UI/Button/Button.uss");')).toBeGreaterThan(
      theme.indexOf('@import url("Core/Runtime/Core.uss");'),
    );
    expect(theme.indexOf('@import url("UI/Dialog/Dialog.uss");')).toBeGreaterThan(
      theme.indexOf('@import url("UI/Button/Button.uss");'),
    );
    expect((await readManifest(root)).components).toEqual(["Button", "Dialog"]);
  });

  it("(C) 同じ項目を 2 回 add しても、内容も更新時刻も変わらず、@import も重複しない", async () => {
    const root = await makeInitializedProject();
    await runAdd({ cwd: root, names: ["button", "dialog"], ...silent });
    // 時計の粒度に依存しないよう、過去の時刻に固定してから 2 回目を実行する
    const past = new Date("2000-01-01T00:00:00Z");
    for (const f of await listFiles(root)) await utimes(path.join(root, f), past, past);
    const before = await snapshot(root);
    const lines: string[] = [];

    await runAdd({ cwd: root, names: ["button", "dialog"], log: (m) => lines.push(m) });

    expect(await snapshot(root)).toEqual(before);
    expect(lines.some((l) => l.startsWith("既に存在するためスキップ: "))).toBe(true);
    const theme = await read(root, "Assets/Hone/HoneTheme.tss");
    expect(theme.match(/UI\/Button\/Button\.uss/g)).toHaveLength(1);
    expect(theme.match(/UI\/Dialog\/Dialog\.uss/g)).toHaveLength(1);
  });

  it("(D) 編集した Button.uss は add button で上書きされない", async () => {
    const root = await makeInitializedProject();
    await runAdd({ cwd: root, names: ["button"], ...silent });
    await writeFile(path.join(root, "Assets/Hone/UI/Button/Button.uss"), "/* 利用者が編集 */\n");

    await runAdd({ cwd: root, names: ["button"], ...silent });

    expect(await read(root, "Assets/Hone/UI/Button/Button.uss")).toBe("/* 利用者が編集 */\n");
  });

  it("(D) Button を編集した後に dialog を追加しても、依存の Button は上書きされない", async () => {
    const root = await makeInitializedProject();
    await runAdd({ cwd: root, names: ["button"], ...silent });
    await writeFile(path.join(root, "Assets/Hone/UI/Button/Button.cs"), "// 編集\n");

    await runAdd({ cwd: root, names: ["dialog"], ...silent });

    expect(await read(root, "Assets/Hone/UI/Button/Button.cs")).toBe("// 編集\n");
  });

  it("(E) 存在しない名前ならエラー終了し、他の名前も含め何もコピーしない", async () => {
    const root = await makeInitializedProject();
    const before = await snapshot(root);

    const result = runAdd({ cwd: root, names: ["button", "nope"], ...silent });

    await expect(result).rejects.toBeInstanceOf(HoneError);
    await expect(result).rejects.toThrow("nope");

    expect(await snapshot(root)).toEqual(before);
  });

  it("(F) 引数なしなら ui と block の一覧を表示し（lib と theme は出さない）、何も書かない", async () => {
    const registry = await makeRegistry([
      { name: "Button", type: "registry:ui", description: "ボタン", files: ["UI/Button/Button.cs"] },
      { name: "PauseMenu", type: "registry:block", description: "ポーズ画面", files: ["Blocks/PauseMenu/PauseMenu.cs"] },
    ]);
    const root = await makeInitializedProject(registry);
    const before = await snapshot(root);
    const lines: string[] = [];

    await runAdd({ cwd: root, names: [], log: (m) => lines.push(m) });

    expect(lines).toEqual(["Button (ui) ボタン", "PauseMenu (block) ポーズ画面"]);
    expect(await snapshot(root)).toEqual(before);
  });

  it("(F) 実際の registry の一覧にも Button と Dialog が出る", async () => {
    const root = await makeInitializedProject();
    const lines: string[] = [];

    await runAdd({ cwd: root, names: [], log: (m) => lines.push(m) });

    expect(lines).toEqual(
      expect.arrayContaining([expect.stringMatching(/^Button \(ui\) /), expect.stringMatching(/^Dialog \(ui\) /)]),
    );
  });

  it("registry:block の項目は manifest の components に載る", async () => {
    const registry = await makeRegistry([
      { name: "Dep", files: ["UI/Dep/Dep.cs"], registryDependencies: ["Core"] },
      { name: "PauseMenu", type: "registry:block", files: ["Blocks/PauseMenu/PauseMenu.cs"], registryDependencies: ["Core", "Dep"] },
    ]);
    const root = await makeInitializedProject(registry);

    await runAdd({ cwd: root, names: ["pausemenu"], ...silent });

    expect((await readManifest(root)).components).toEqual(["Dep", "PauseMenu"]);
  });

  it("完了時に Unity Editor に戻る案内を表示する", async () => {
    const root = await makeInitializedProject();
    const lines: string[] = [];

    await runAdd({ cwd: root, names: ["Button"], log: (m) => lines.push(m) });

    expect(lines.at(-1)).toContain("コンパイル");
  });

  it("hone.json が無ければ、先に init を実行するよう案内してエラー終了する", async () => {
    const root = await makeProject(tmp);

    const result = runAdd({ cwd: root, names: ["button"], ...silent });

    await expect(result).rejects.toBeInstanceOf(HoneError);
    await expect(result).rejects.toThrow("hone init");
    expect(await listFiles(root)).toEqual([]);
  });

  it.each(["Assets/Hone/HoneTheme.tss", "Assets/Hone/hone.manifest.json"])(
    "%s が無ければ（init が済んでいない）、先に init を実行するよう案内し、何もコピーしない",
    async (missing) => {
      const root = await makeInitializedProject();
      await rm(path.join(root, missing));
      const before = await snapshot(root);

      await expect(runAdd({ cwd: root, names: ["button"], ...silent })).rejects.toThrow("hone init");

      expect(await snapshot(root)).toEqual(before);
    },
  );

  it("hone.json の output が既定でなくても、その出力先にコピーし、@import は theme からの相対で書く", async () => {
    const root = await makeProject(tmp);
    await writeFile(
      path.join(root, "hone.json"),
      JSON.stringify({ registry: repoRoot, output: "Assets/Vendor/Hone" }),
    );
    await runInit({ cwd: root, ...silent });

    await runAdd({ cwd: root, names: ["button"], ...silent });

    expect(await listFiles(root)).toContain("Assets/Vendor/Hone/UI/Button/Button.cs");
    expect(await read(root, "Assets/Vendor/Hone/HoneTheme.tss")).toContain(
      '@import url("UI/Button/Button.uss");',
    );
  });

  it("URL の registry は fetch で取得する", async () => {
    const root = await makeProject(tmp);
    const fetchMock = async (url: string | URL): Promise<Response> => {
      const rel = String(url).replace("https://example.test/hone/", "");
      try {
        return new Response(await readFile(path.join(repoRoot, rel)));
      } catch {
        return new Response("not found", { status: 404 });
      }
    };
    await runInit({ cwd: root, registry: "https://example.test/hone", fetch: fetchMock, ...silent });

    await runAdd({ cwd: root, names: ["button"], fetch: fetchMock, ...silent });

    expect(await listFiles(root)).toContain("Assets/Hone/UI/Button/Button.cs");
  });

  it("途中のファイルの取得に失敗したら、何もコピーせず theme も manifest も書き換えない", async () => {
    const root = await makeProject(tmp);
    const fetchMock = async (url: string | URL): Promise<Response> => {
      const rel = String(url).replace("https://example.test/hone/", "");
      if (rel.endsWith("Dialog.uss")) return new Response("not found", { status: 404 });
      return new Response(await readFile(path.join(repoRoot, rel)));
    };
    await runInit({ cwd: root, registry: "https://example.test/hone/", fetch: fetchMock, ...silent });
    const before = await snapshot(root);

    await expect(
      runAdd({ cwd: root, names: ["dialog"], fetch: fetchMock, ...silent }),
    ).rejects.toThrow("404");

    expect(await snapshot(root)).toEqual(before);
  });

  it("書き込み先がディレクトリなら、スキップせず HoneError でエラー終了する", async () => {
    const root = await makeInitializedProject();
    await mkdir(path.join(root, "Assets/Hone/UI/Button/Button.cs"), { recursive: true });

    const before = await snapshot(root);

    const result = runAdd({ cwd: root, names: ["button"], ...silent });

    await expect(result).rejects.toBeInstanceOf(HoneError);
    await expect(result).rejects.toThrow("ディレクトリ");
    // 他のファイルを書く前に検出する
    expect(await snapshot(root)).toEqual(before);
  });

  it("前回の add が theme と manifest の更新の前に止まっていても、再実行で依存先の @import と components が補われる", async () => {
    const root = await makeInitializedProject();
    // 止まった状態: Button のファイルは全部あるが、theme の @import も manifest の components も無い
    for (const f of BUTTON_FILES) {
      const dest = path.join(root, f);
      await mkdir(path.dirname(dest), { recursive: true });
      await writeFile(dest, await readFile(path.join(repoRoot, f.replace("Assets/Hone", "registry"))));
    }

    await runAdd({ cwd: root, names: ["dialog"], ...silent });

    const theme = await read(root, "Assets/Hone/HoneTheme.tss");
    expect(theme).toContain('@import url("UI/Button/Button.uss");');
    expect(theme).toContain('@import url("UI/Dialog/Dialog.uss");');
    expect((await readManifest(root)).components).toEqual(["Button", "Dialog"]);
  });
});

describe("hone add の registry の解釈", () => {
  it("registryDependencies を再帰的に解決し、依存を先に並べる。循環しても止まる", async () => {
    const registry = await makeRegistry([
      { name: "A", files: ["UI/A/A.uss"], registryDependencies: ["Core", "B"] },
      { name: "B", files: ["UI/B/B.uss"], registryDependencies: ["C"] },
      { name: "C", files: ["UI/C/C.uss"], registryDependencies: ["B"] },
    ]);
    const root = await makeInitializedProject(registry);
    const lines: string[] = [];

    await runAdd({ cwd: root, names: ["A"], log: (m) => lines.push(m) });

    expect(lines.filter((l) => l.startsWith("作成: "))).toEqual([
      "作成: Assets/Hone/UI/C/C.uss",
      "作成: Assets/Hone/UI/B/B.uss",
      "作成: Assets/Hone/UI/A/A.uss",
    ]);
    expect(await read(root, "Assets/Hone/HoneTheme.tss")).toBe(
      [
        '@import url("Tokens.uss");',
        '@import url("UI/C/C.uss");',
        '@import url("UI/B/B.uss");',
        '@import url("UI/A/A.uss");',
        ":root {}",
        "",
      ].join("\n"),
    );
  });

  it("registryDependencies に registry に無い名前があれば、何もコピーしない", async () => {
    const registry = await makeRegistry([
      { name: "A", files: ["UI/A/A.cs"], registryDependencies: ["Core", "Missing"] },
    ]);
    const root = await makeInitializedProject(registry);
    const before = await snapshot(root);

    await expect(runAdd({ cwd: root, names: ["A"], ...silent })).rejects.toThrow("Missing");

    expect(await snapshot(root)).toEqual(before);
  });

  it("依頼されていない依存先は、ファイルが全部あれば表示せず飛ばす。一部欠けていれば欠けたファイルだけコピーする", async () => {
    const registry = await makeRegistry([
      { name: "Dep", files: ["UI/Dep/Dep.cs", "UI/Dep/Dep.uss"] },
      { name: "A", files: ["UI/A/A.cs"], registryDependencies: ["Core", "Dep"] },
    ]);
    const root = await makeInitializedProject(registry);
    await runAdd({ cwd: root, names: ["Dep"], ...silent });
    const lines: string[] = [];

    await runAdd({ cwd: root, names: ["A"], log: (m) => lines.push(m) });

    expect(lines.filter((l) => l.includes("Dep/"))).toEqual([]);

    await rm(path.join(root, "Assets/Hone/UI/Dep/Dep.cs"));
    lines.length = 0;
    await runAdd({ cwd: root, names: ["A"], log: (m) => lines.push(m) });

    expect(lines.filter((l) => l.startsWith("作成: "))).toEqual(["作成: Assets/Hone/UI/Dep/Dep.cs"]);
  });

  it("dependencies の UPM パッケージが Packages/manifest.json に無ければ警告し、コピーは行う", async () => {
    const registry = await makeRegistry([
      { name: "A", files: ["UI/A/A.cs"], registryDependencies: ["Core"], dependencies: ["com.example.pkg"] },
    ]);
    const root = await makeInitializedProject(registry);
    await mkdir(path.join(root, "Packages"), { recursive: true });
    await writeFile(path.join(root, "Packages/manifest.json"), JSON.stringify({ dependencies: {} }));
    const lines: string[] = [];

    await runAdd({ cwd: root, names: ["A"], log: (m) => lines.push(m) });

    expect(lines.filter((l) => l.startsWith("警告: "))).toEqual([
      expect.stringContaining("com.example.pkg"),
    ]);
    expect(await listFiles(root)).toContain("Assets/Hone/UI/A/A.cs");
    // インストールはしない
    expect(JSON.parse(await read(root, "Packages/manifest.json"))).toEqual({ dependencies: {} });
  });

  it("依存先の項目の dependencies も確認し、同じパッケージを使う項目は 1 行にまとめる。dependencies キーが無ければ空として扱う", async () => {
    const registry = await makeRegistry([
      { name: "B", files: ["UI/B/B.cs"], registryDependencies: ["Core"], dependencies: ["com.example.pkg"] },
      { name: "A", files: ["UI/A/A.cs"], registryDependencies: ["Core", "B"], dependencies: ["com.example.pkg"] },
    ]);
    const root = await makeInitializedProject(registry);
    await mkdir(path.join(root, "Packages"), { recursive: true });
    await writeFile(path.join(root, "Packages/manifest.json"), "{}");
    const lines: string[] = [];

    await runAdd({ cwd: root, names: ["A"], log: (m) => lines.push(m) });

    expect(lines.filter((l) => l.startsWith("警告: "))).toEqual([
      expect.stringMatching(/B, A .*com\.example\.pkg/),
    ]);
  });

  it("dependencies のパッケージが入っていれば警告しない", async () => {
    const registry = await makeRegistry([
      { name: "A", files: ["UI/A/A.cs"], registryDependencies: ["Core"], dependencies: ["com.example.pkg"] },
    ]);
    const root = await makeInitializedProject(registry);
    await mkdir(path.join(root, "Packages"), { recursive: true });
    await writeFile(
      path.join(root, "Packages/manifest.json"),
      JSON.stringify({ dependencies: { "com.example.pkg": "1.0.0" } }),
    );
    const lines: string[] = [];

    await runAdd({ cwd: root, names: ["A"], log: (m) => lines.push(m) });

    expect(lines.filter((l) => l.startsWith("警告: "))).toEqual([]);
  });

  it("Packages/manifest.json が読めなければ、確認できない旨を警告し、コピーは行う", async () => {
    const registry = await makeRegistry([
      { name: "A", files: ["UI/A/A.cs"], registryDependencies: ["Core"], dependencies: ["com.example.pkg"] },
    ]);
    const root = await makeInitializedProject(registry);
    const lines: string[] = [];

    await runAdd({ cwd: root, names: ["A"], log: (m) => lines.push(m) });

    expect(lines.filter((l) => l.startsWith("警告: "))).toEqual([
      expect.stringMatching(/ENOENT.*確認できません/),
    ]);
    expect(await listFiles(root)).toContain("Assets/Hone/UI/A/A.cs");
  });

  it.each(["../escaped.cs", "/abs.cs", "C:x.cs", "a/../../b.cs"])(
    "registry の files[].path が外へ出る・絶対・不正なら拒否し、何もコピーしない: %s",
    async (badPath) => {
      const registry = await makeRegistry([
        { name: "Good", files: ["UI/Good/Good.cs"], registryDependencies: ["Core"] },
        { name: "Evil", files: [badPath], registryDependencies: ["Core"] },
      ]);
      const root = await makeInitializedProject(registry);
      const before = await snapshot(root);

      await expect(runAdd({ cwd: root, names: ["Good", "Evil"], ...silent })).rejects.toThrow(
        "files[].path",
      );

      expect(await snapshot(root)).toEqual(before);
    },
  );

  it("registryDependencies が文字列の配列でなければ registry.json の形式エラーにする", async () => {
    const registry = await makeRegistry([]);
    const json = JSON.parse(await readFile(path.join(registry, "registry.json"), "utf8"));
    json.items[0].registryDependencies = "Core";
    await writeFile(path.join(registry, "registry.json"), JSON.stringify(json));
    const root = await makeProject(tmp);
    await writeFile(
      path.join(root, "hone.json"),
      JSON.stringify({ registry, output: "Assets/Hone" }),
    );

    await expect(runAdd({ cwd: root, names: [], ...silent })).rejects.toThrow("registry.json");
  });

  it("ui と block 以外（Core / Default）を依頼しても、manifest の components には載せない", async () => {
    const root = await makeInitializedProject();

    await runAdd({ cwd: root, names: ["core", "default"], ...silent });

    const components: string[] = (await readManifest(root)).components ?? [];
    expect(components).not.toContain("Core");
    expect(components).not.toContain("Default");
  });
});

describe("hone add の HoneTheme.tss への挿入", () => {
  it("CRLF の theme には CRLF で挿入する", async () => {
    const registry = await makeRegistry([{ name: "A", files: ["UI/A/A.uss"], registryDependencies: ["Core"] }], {
      theme: '@import url("Tokens.uss");\r\n:root {}\r\n',
    });
    const root = await makeInitializedProject(registry);

    await runAdd({ cwd: root, names: ["A"], ...silent });

    expect(await read(root, "Assets/Hone/HoneTheme.tss")).toBe(
      '@import url("Tokens.uss");\r\n@import url("UI/A/A.uss");\r\n:root {}\r\n',
    );
  });

  it("末尾の改行が無い theme でも、最後の @import の直後に挿入する", async () => {
    const registry = await makeRegistry([{ name: "A", files: ["UI/A/A.uss"], registryDependencies: ["Core"] }], {
      theme: '@import url("Tokens.uss");',
    });
    const root = await makeInitializedProject(registry);

    await runAdd({ cwd: root, names: ["A"], ...silent });

    expect(await read(root, "Assets/Hone/HoneTheme.tss")).toBe(
      '@import url("Tokens.uss");\n@import url("UI/A/A.uss");',
    );
  });

  it("コメントの中の @import は挿入位置にも重複判定にも使わない。既存の行は書き換えない", async () => {
    const theme = [
      '@import url("Tokens.uss");',
      "/* コメント",
      '@import url("UI/A/A.uss");',
      "*/",
      ':root { --x: 1; }',
      "",
    ].join("\n");
    const registry = await makeRegistry([{ name: "A", files: ["UI/A/A.uss"], registryDependencies: ["Core"] }], {
      theme,
    });
    const root = await makeInitializedProject(registry);

    await runAdd({ cwd: root, names: ["A"], ...silent });

    expect(await read(root, "Assets/Hone/HoneTheme.tss")).toBe(
      [
        '@import url("Tokens.uss");',
        '@import url("UI/A/A.uss");',
        "/* コメント",
        '@import url("UI/A/A.uss");',
        "*/",
        ':root { --x: 1; }',
        "",
      ].join("\n"),
    );
  });

  it.each([
    ["シングルクォート", "@import url('UI/A/A.uss');"],
    ["括弧の内側の空白", '@import url( "UI/A/A.uss" );'],
    ["引用符なし", "@import url(UI/A/A.uss);"],
  ])("同じ USS を指す既存の @import（%s）があれば挿入しない", async (_label, line) => {
    const theme = `@import url("Tokens.uss");\n${line}\n:root {}\n`;
    const registry = await makeRegistry([{ name: "A", files: ["UI/A/A.uss"], registryDependencies: ["Core"] }], { theme });
    const root = await makeInitializedProject(registry);

    await runAdd({ cwd: root, names: ["A"], ...silent });

    expect(await read(root, "Assets/Hone/HoneTheme.tss")).toBe(theme);
  });

  it("最後の @import の行末から複数行のコメントが始まっていても、コメントの外に挿入する", async () => {
    const theme = '@import url("Tokens.uss"); /* メモ\n続き */\n:root {}\n';
    const registry = await makeRegistry([{ name: "A", files: ["UI/A/A.uss"], registryDependencies: ["Core"] }], { theme });
    const root = await makeInitializedProject(registry);

    await runAdd({ cwd: root, names: ["A"], ...silent });

    expect(await read(root, "Assets/Hone/HoneTheme.tss")).toBe(
      '@import url("Tokens.uss"); /* メモ\n続き */\n@import url("UI/A/A.uss");\n:root {}\n',
    );
  });

  it("USS を持たない項目は theme を書き換えない", async () => {
    const registry = await makeRegistry([{ name: "A", files: ["UI/A/A.cs"], registryDependencies: ["Core"] }], {
      theme: ":root {}\n",
    });
    const root = await makeInitializedProject(registry);

    await runAdd({ cwd: root, names: ["A"], ...silent });

    expect(await read(root, "Assets/Hone/HoneTheme.tss")).toBe(":root {}\n");
  });

  it("挿入が要るのに theme に @import 行が無ければ、何も書かずエラー終了する", async () => {
    const registry = await makeRegistry([{ name: "A", files: ["UI/A/A.uss"], registryDependencies: ["Core"] }], {
      theme: ":root {}\n",
    });
    const root = await makeInitializedProject(registry);
    const before = await snapshot(root);

    await expect(runAdd({ cwd: root, names: ["A"], ...silent })).rejects.toThrow("@import");

    expect(await snapshot(root)).toEqual(before);
  });
});

describe("hone add の hone.manifest.json", () => {
  it("version・theme・fonts と他のキーを保ち、components を追記する。重複は足さない", async () => {
    const root = await makeInitializedProject();
    const manifestPath = path.join(root, "Assets/Hone/hone.manifest.json");
    await writeFile(
      manifestPath,
      JSON.stringify({ version: 1, theme: "Assets/Hone/HoneTheme.tss", fonts: ["ja"], components: ["Button"], extra: 1 }),
    );

    await runAdd({ cwd: root, names: ["dialog", "button"], ...silent });

    expect(await readManifest(root)).toEqual({
      version: 1,
      theme: "Assets/Hone/HoneTheme.tss",
      fonts: ["ja"],
      components: ["Button", "Dialog"],
      extra: 1,
    });
  });

  it.each([
    ["壊れた JSON", "{"],
    ["null", "null"],
    ["components が配列でない", JSON.stringify({ components: "Button" })],
    ["components に文字列以外がある", JSON.stringify({ components: [1] })],
  ])("hone.manifest.json が不正（%s）ならエラー終了し、何もコピーしない", async (_label, body) => {
    const root = await makeInitializedProject();
    await writeFile(path.join(root, "Assets/Hone/hone.manifest.json"), body);
    const before = await snapshot(root);

    await expect(runAdd({ cwd: root, names: ["button"], ...silent })).rejects.toThrow(
      "hone.manifest.json",
    );

    expect(await snapshot(root)).toEqual(before);
  });
});
