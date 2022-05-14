$url = 'https://devblogs.microsoft.com/landingpage/'
$number = (gh issue list -s open --json number | convertfrom-json).number
# updatedAt returns UTC, issue comments returns local time, why?
$updatedAt = [datetime](gh issue view $number --json updatedAt | convertfrom-json).updatedAt

$comments = (gh issue view $number -c --json comments | convertfrom-json).comments
if($comments.Count -gt 1){
    $lastupdate = [datetime]($comments | Sort-Object $_.createdAt -Bottom 1 | Select-Object createdAt).createdAt
    #gh issue returns local timezone
    $currenttimediff = (Get-TimeZone).baseutcoffset.hours
    $lastupdate = $lastupdate.addhours(-$currenttimediff.hours)
}else{
    $lastupdate = $updatedAt
}

$feed =[xml](invoke-webrequest -Uri $url -UseBasicParsing)
foreach ($item in $feed.rss.channel.item) {
    $pubDate = [datetime]$item.pubDate
    if($item.creator -ne 'Raymond Chen' -and $pubDate -gt $lastupdate) {
        $title = $item.title
        $link = $item.link 
        $comment = "[$title]($link)"
        gh issue comment $number -b $comment
    }
}
