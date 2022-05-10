$url = 'https://devblogs.microsoft.com/landingpage/'

$number = (gh issue list -s open --json number | convertfrom-json).number
$comments = (gh issue view $number -c --json comments | convertfrom-json).comments
$lastupdate = [datetime]($comments | Sort-Object $_.createdAt -Bottom 1 | Select-Object createdAt).createdAt

$feed =[xml](invoke-webrequest -Uri $url -UseBasicParsing)
$notraymond = $feed.rss.channel.item | Where-Object {$_.creator -ne 'Raymond Chen' -and [datetime]$_.pubdate -gt $lastupdate}

foreach ($item in $notraymond) {
    $title = $item.title
    $link = $item.link 
    $comment = "[$title]($link)"

    gh issue comment $number -b $comment
}
