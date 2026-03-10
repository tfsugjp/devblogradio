# devblogradio
DevOps Radio's announcement repository

## Event Page

- [connpass](https://devblog.connpass.com/)
- [YouTube Channel](https://www.youtube.com/channel/UChAmETJuRAnJoNRJDFeUzYw)

## Event Summary

- 原則二週間に一度金曜日 22:00-23:00(終了は多少ずれます)
- YouTube Liveで放送、録画は特別な場合を除いて公開
- ゲストが来ることもあります

## Feed Sync

- 日次の feed 同期は [src/scrapecsharp/scrapsummary/scraper/Program.cs](src/scrapecsharp/scrapsummary/scraper/Program.cs) の .NET コンソールアプリで実行します。
- 必要な環境変数は `GH_TOKEN`、`OPENAI_API_TOKEN`、`OPENAI_API_DEPLOY`、`OPENAI_API_BASE` です。
- ローカル実行前に `dotnet build ./src/scrapecsharp/scrapsummary/scraper/scraper.csproj` を行い、その後 `pwsh ./src/scrapecsharp/scrapsummary/scraper/bin/Debug/net8.0/playwright.ps1 install chromium` で Playwright browser を準備してください。
