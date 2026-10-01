// 利用者に見せるエラー。index.ts がメッセージだけを表示して exit code 1 で終わる
export class HoneError extends Error {}

export function errorCode(e: unknown): string | undefined {
  return (e as NodeJS.ErrnoException | undefined)?.code;
}

// 失敗の理由として表示する短い文字列（errno の code があれば code、無ければ message）
export function reason(e: unknown): string {
  return errorCode(e) ?? (e instanceof Error ? e.message : String(e));
}
