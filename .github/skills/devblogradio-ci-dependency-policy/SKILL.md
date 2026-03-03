---
name: devblogradio-ci-dependency-policy
description: devblogradio の daily-fetchrss workflow と npm 依存関係の運用ルール。Node バージョン、CI 手順、脆弱性ゼロ基準を適用する。
---

# Devblogradio CI / Dependency Policy

## 適用対象

- `.github/workflows/daily-fetchrss.yml`
- `package.json`
- `package-lock.json`
- `.gitignore`（npm マニフェスト追跡設定）

## 必須ルール

1. Node バージョン
   - 既定は **24**。
   - 24 で互換性問題が出た場合のみ **22** に変更する。

2. CI 手順（daily-fetchrss）
   - `actions/setup-node` で Node を指定する。
   - npm 依存は `npm ci` でインストールする。
   - `cache: 'npm'` を利用する（lockfile 前提）。
   - `npm audit` は **CI 内では実行しない**。

3. Workflow Actions のバージョン固定
   - workflow で利用する `uses:` は、必ず **コミットハッシュ（フル SHA）** を指定する。
   - 例: `actions/checkout@34e114876b0b11c390a56381ad16ebd13914f8d5`
   - タグ（`@v4` など）のみ指定は禁止。

4. 依存関係のセキュリティ基準
   - `package.json` に記載する依存バージョンは、既知脆弱性が 0 のもののみ採用する。
   - 依存更新時は lockfile を必ず更新し、`package-lock.json` をコミット対象に含める。

5. 追跡ルール（.gitignore）
   - `package.json` と `package-lock.json` を ignore してはならない。
   - `*.json` を ignore している場合は、例外 (`!package.json`, `!package-lock.json`) を追加する。

6. 作業完了前の確認（ローカル）
   - `npm ci`
   - `npm audit --audit-level=low`
   - 期待結果: `found 0 vulnerabilities`

7. 変更範囲
   - 要件達成に必要な最小差分のみ。
   - 無関係なファイルや設定を変更しない。

## 推奨実施フロー

1. 依存を固定バージョンで更新
   - 例: `npm install <package>@<version> --save-exact`
2. lockfile 更新後に `npm ci` を実行
3. `npm audit --audit-level=low` で 0 件を確認
4. workflow と manifest/lockfile の整合性を確認

## Done 条件

- `daily-fetchrss` が Node 24（必要時のみ 22）で実行される。
- CI に `npm audit` ステップが存在しない。
- workflow の `uses:` がすべてコミットハッシュ固定になっている。
- `package.json` / `package-lock.json` が追跡対象になっている。
- ローカル監査結果が 0 vulnerabilities である。
