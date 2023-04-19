if([string]::isnullorempty($env:OPEN_API_TOKEN) -eq $true){
  write-host 'API Token is null'
}

if([string]::isnullorempty($env:OPEN_API_URL) -eq $true){
  write-host 'API endpoint is null'
}
