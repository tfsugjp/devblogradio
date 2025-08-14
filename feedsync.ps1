function Get-SummarywithOpenAI(
    [string]$blogurl
)
{
    $Token = $ENV:OPENAI_API_TOKEN
    $Uri   = "https://$($env:OPENAI_API_BASE).openai.azure.com/openai/deployments/$($env:OPENAI_API_DEPLOY)/chat/completions?api-version=2025-04-01-preview"
    $PostBody = @{
        max_tokens = 800
        temperature = 0.7
        top_p = 0.95
        frequency_penalty = 0
        presence_penalty = 0
        stop = @('##')
    }

    $Header =@{
      "api-key" = $Token
      "Content-Type" ="application/json"
    }

    $PostBody.messages = @(
        @{
            role = 'user'
            content = '以下のURLを要約してください。本文が日本語以外である場合、日本語で200文字以内に要約してください。'+$blogurl
        }
    )

    try {
        $response = Invoke-RestMethod -Method Post -Uri $Uri `
            -Headers $Header `
            -Body ([System.Text.Encoding]::UTF8.GetBytes(($PostBody | ConvertTo-Json -Compress)))

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
            $summary = Get-SummarywithOpenAI $link
            $comment = "[$title]($link)  " + $summary
            gh issue comment $number -b $comment
            # avoid OpenAI API's rate limit(12 times per minitue in GPT-4)
            start-sleep -Seconds 5
        }
    }
}

