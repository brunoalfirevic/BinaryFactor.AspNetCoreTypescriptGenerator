function remove-folder($path) {
    remove-item $path -Recurse -Force -ErrorAction SilentlyContinue | out-null
}

function info($text) {
    Write-Host
    Write-Host $text -fore CYAN
    Write-Host
}

function error($text) {
    Write-Host
    Write-Host $text -fore RED
    Write-Host
}

function skip($array, $skip_count) {
    return $array | Select-Object -Skip $skip_count
}

function exec($command, $path) {
    exec-ignore-exit-code $command $path

    if ($lastexitcode -ne 0) {
        throw "Error executing command: '$($ExecutionContext.InvokeCommand.ExpandString($command))'"
    }
}

function exec-ignore-exit-code($command, $path) {
    with-location $path {
        $global:lastexitcode = 0

        Write-Host $ExecutionContext.InvokeCommand.ExpandString($command) -fore DarkGray

        & $command
    }
}

function with-location($location, $callback) {
    if ($null -ne $location) {
        Push-Location $location
    }

    try {
        & $callback
    }
    finally {
        if ($null -ne $location) {
            Pop-Location
        }
    }
}

function main($mainBlock) {
    & $mainBlock
}