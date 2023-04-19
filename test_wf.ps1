param(
    [String] [Parameter (Mandatory=$true)] $apitoken,
    [String] [Parameter (Mandatory=$true)] $apiUrl
)

if([string]::isnullorempty($apitoken) -eq $true){
  write-host 'API Token is null'
}

if([string]::isnullorempty($apiUrl) -eq $true){
  write-host 'API endpoint is null'
}else{
  write-host $apiendpoint
}
