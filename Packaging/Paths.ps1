function Assert-PlainPath([string]$Path) {
    $current=[IO.Path]::GetFullPath($Path)
    while($current) {
        if(Test-Path -LiteralPath $current) {
            if((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw 'An application path is redirected. Setup stopped to protect files outside the application folder.'
            }
        }
        $parent=[IO.Path]::GetDirectoryName($current)
        if($parent -eq $current){break}
        $current=$parent
    }
}

function Assert-PlainTree([string]$Root) {
    Assert-PlainPath $Root
    if(Test-Path -LiteralPath $Root) {
        $redirected=Get-ChildItem -LiteralPath $Root -Recurse -Force | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint} | Select-Object -First 1
        if($redirected){throw 'The application folder contains a redirected path. Setup stopped.'}
    }
}
