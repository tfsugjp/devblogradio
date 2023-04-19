$apitoken = $ENV:OPENAI_API_TOKEN
if([string]::isnullorempty($ENV:OPENAI_API_TOKEN) -eq $true){
  write-host 'API Token is null'
}

$apiendpoint = $ENV:OPENAI_API_URL
if([string]::isnullorempty$ENV:OPENAI_API_URL) -eq $true){
  write-host 'API endpoint is null'
}else{
  write-host $apiendpoint
}
