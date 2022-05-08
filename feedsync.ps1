$url = 'https://devblogs.microsoft.com/landingpage/'

$number = (gh issue list -s open --json number | convertfrom-json).number

$feed =[xml](invoke-webrequest -Uri $url -UseBasicParsing)
foreach ($item in $feed.rss.channel.item) {
    $title = $item.title
    $link = $item.link 
    $comment = "[$title]($link)"

    gh issue comment -t $number -m $comment
}
