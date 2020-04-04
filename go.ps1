param(
    [string] [Parameter(Position = 0)] $command,
    [string[]] [Parameter(Position = 1, ValueFromRemainingArguments)] $arguments = @())

. .\go-helpers

$project_name = "AspNetCoreTypescriptGenerator"

function clean {
    info "Cleaning build artifacts"
    exec {dotnet clean src -v minimal /nologo}
}

function build {
    info "Building solution"
    exec {dotnet build src /nologo}
}

function rebuild {
    clean
    build
}

function test {
    info "Running tests using .NET Core"
    exec {dotnet run --project src/BinaryFactor.$project_name.Tests}
}

function watch-test {
    exec {dotnet watch --project src/BinaryFactor.$project_name.Tests run}
}

function pack-nuget {
    info "Deleting 'publish' folder"
    remove-folder "./publish"

    info "Packing NuGet package"
    exec {dotnet pack src -c Release -o publish /nologo}
}

function push-to-nuget {
    pack-nuget

    info "Publishing NuGet package"
    $nuget_package = Get-ChildItem -Path publish -Filter *.nupkg -File
    exec {dotnet nuget push publish\$nuget_package --source nuget.org}
}

function unlist-from-nuget {
    info "Unlisting NuGet package"
    exec {dotnet nuget delete BinaryFactor.$project_name $($arguments[0]) --non-interactive --source nuget.org}
}

function go {
    rebuild
    test
}

main {
    if ($command) {
        & $command
    } else {
        info  @"
Available commands:
    go

    clean
    build
    rebuild

    test
    watch-test

    pack-nuget
    push-to-nuget
    unlist-from-nuget
"@
    }
}
