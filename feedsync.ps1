function Get-BlogContent(
    [string]$blogurl
)
{
    $result = node src/fetch-content.js $blogurl 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($result)) {
        return $null
    }
    try {
        $parsed = $result | ConvertFrom-Json
        if (-not [string]::IsNullOrWhiteSpace($parsed.error)) {
            Write-Host "fetch-content error: $($parsed.error)"
            return $null
        }
        return $parsed
    }
    catch {
        Write-Host "JSON parse error: $_"
        return $null
    }
}

function Get-SummarywithOpenAI(
    [string]$blogurl,
    [string]$blogtitle,
    [string]$blogcontent
)
{
    $Token = $ENV:OPENAI_API_TOKEN
    $Uri   = "https://$($env:OPENAI_API_BASE).cognitiveservices.azure.com/openai/v1/chat/completions"

    # Truncate content to avoid token limits (approx 8000 chars)
    $maxContentLength = 8000
    if ($blogcontent.Length -gt $maxContentLength) {
        $blogcontent = $blogcontent.Substring(0, $maxContentLength) + "`n...(以下省略)"
    }

    $PostBody = @{
        max_tokens = 800
        temperature = 0.9
        top_p = 0.95
        frequency_penalty = 0
        presence_penalty = 0
        stop = @('##')
        model = $env:OPENAI_API_DEPLOY
    }

    $Header =@{
      "api-key" = $Token
      "Content-Type" ="application/json"
    }

    $prompt = @"
以下のブログ記事を要約してください。本文が日本語以外である場合、日本語で200文字以内に要約してください。本文が英語で1000words以上ある場合は最初と最後の段落を忠実に日本語翻訳し、段落として記載してください。重要と思われる部分の概要をまとめてください。

タイトル: $blogtitle
URL: $blogurl

本文:
$blogcontent
"@

    $PostBody.messages = @(
      @{
        role = 'user'
        content = $prompt
      }
    )

    try {
        $response = Invoke-RestMethod -Method Post -Uri $Uri `
            -Headers $Header `
            -Body ([System.Text.Encoding]::UTF8.GetBytes(($PostBody | ConvertTo-Json -Compress -Depth 10)))

       $Answer = $response.choices[0].message.content
   }
    catch {
        $Answer = '要約に失敗しました。'
    }

    return $Answer
}

$urls = Get-Content -Path .\feedsource.txt

$number = (gh issue list -s open --json number | convertfrom-json).number
$createdAt = [datetime](gh issue view $number --json createdAt | convertfrom-json).createdAt
$comments = (gh issue view $number -c --json comments | convertfrom-json).comments
# createdAt returns UTC, issue comments returns local time, why?

if($comments.Count -gt 1){
    $lastupdate = [datetime]($comments | Sort-Object $_.createdAt -Bottom 1 | Select-Object createdAt).createdAt
    #gh issue returns local timezone
    $currenttimediff = (Get-TimeZone).BaseUtcOffset.TotalHours
    $lastupdate = $lastupdate.AddHours(-$currenttimediff)
}else{
    $lastupdate = $createdAt
}

# Keep track of URLs we've already processed across all feeds in this run
$seenLinks = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

foreach($url in $urls) {
    write-host $url
    if ([string]::IsNullOrWhiteSpace($url) -eq $true) {
        continue
    }
    $feed =[xml](invoke-webrequest -Uri $url -UseBasicParsing)
    foreach ($item in $feed.rss.channel.item) {
        $link = [string]$item.link
        if ([string]::IsNullOrWhiteSpace($link)) {
            continue
        }

        # Deduplicate across feeds: if we've already seen this link, skip it
        if (-not $seenLinks.Add($link)) {
            continue
        }

        $pubDate = [datetime]$item.pubDate
        if($pubDate -gt $lastupdate) {
            $title = $item.title
            $summary = '要約に失敗しました。'
            try {
                Write-Host "Fetching content: $link"
                $blogData = Get-BlogContent $link
                if ($null -ne $blogData -and -not [string]::IsNullOrWhiteSpace($blogData.content)) {
                    $fetchedTitle = if ([string]::IsNullOrWhiteSpace($blogData.title)) { $title } else { $blogData.title }
                    $summary = Get-SummarywithOpenAI -blogurl $link -blogtitle $fetchedTitle -blogcontent $blogData.content
                    if ([string]::IsNullOrWhiteSpace($summary)) {
                        $summary = '要約に失敗しました。'
                    }
                } else {
                    Write-Host "Failed to fetch content for $link, skipping summarization."
                    $summary = '本文の取得に失敗しました。'
                }
            }
            catch {
                Write-Host "Summarization pipeline error for ${link}: $_"
                $summary = '要約処理中にエラーが発生しました。'
            }
            finally {
                # Always register URL to issue even if summarization fails
                $comment = "[$title]($link)  " + $summary
                gh issue comment $number -b $comment
                # avoid OpenAI API's rate limit(12 times per minitue in GPT-4)
                start-sleep -Seconds 5
            }
        }
    }
}





