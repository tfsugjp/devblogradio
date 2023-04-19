function Get-SummarywithOpenAI(
    [string]$blogurl
)
{
    $Token = $ENV:OPEN_API_TOKEN
    $Uri   = $ENV:OPENAI_API_URL
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

$url = 'https://devblogs.microsoft.com/landingpage/'
$number = (gh issue list -s open --json number | convertfrom-json).number
# createdAt returns UTC, issue comments returns local time, why?
$createdAt = [datetime](gh issue view $number --json createdAt | convertfrom-json).createdAt

$comments = (gh issue view $number -c --json comments | convertfrom-json).comments
if($comments.Count -gt 1){
    $lastupdate = [datetime]($comments | Sort-Object $_.createdAt -Bottom 1 | Select-Object createdAt).createdAt
    #gh issue returns local timezone
    $currenttimediff = (Get-TimeZone).baseutcoffset.hours
    $lastupdate = $lastupdate.addhours(-$currenttimediff.hours)
}else{
    $lastupdate = $createdAt
}

$feed =[xml](invoke-webrequest -Uri $url -UseBasicParsing)
foreach ($item in $feed.rss.channel.item) {
    $pubDate = [datetime]$item.pubDate
    if($item.creator -ne 'Raymond Chen' -and $pubDate -gt $lastupdate) {
        $title = $item.title
        $link = $item.link 
        $summary = Get-SummarywithOpenAI $link
        $comment = "[$title]($link)  " + $summary
        gh issue comment $number -b $comment
        # avoid OpenAI API's rate limit(12 times per minitue in GPT-4)
        start-sleep -Seconds 5
    }
}
