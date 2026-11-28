param(
    [string] $AppPath = (Join-Path $PSScriptRoot '../src/SnipNation/bin/Release/net10.0-windows10.0.22000.0/SnipNation.exe')
)

$ErrorActionPreference = 'Stop'
$testRoot = [IO.Directory]::CreateTempSubdirectory('snipnation-mcp-test-').FullName
$settings = 'HKCU:\Software\SnipNation'
$previousPreferences = Get-ItemPropertyValue -LiteralPath $settings -Name Preferences -ErrorAction SilentlyContinue
$changedFolder = $false
$child = $null
$requestId = 0

function Assert-Result([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Send-Message($Message) {
    $child.StandardInput.WriteLine((ConvertTo-Json -InputObject $Message -Compress -Depth 24))
    $child.StandardInput.Flush()
}

function Invoke-Request([string] $Method, [hashtable] $Params = @{}) {
    $script:requestId++
    Send-Message @{ jsonrpc = '2.0'; id = $script:requestId; method = $Method; params = $Params }
    do {
        $line = $child.StandardOutput.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(20)).GetAwaiter().GetResult()
        if ($null -eq $line) {
            throw "The MCP server exited while handling $Method. $($stderr.GetAwaiter().GetResult())"
        }
        $message = ConvertFrom-Json -InputObject $line -AsHashtable
    } while ($message.method -and $null -eq $message.id)
    Assert-Result ($message.id -eq $script:requestId) "Unexpected MCP response for $Method."
    if ($message.error) { throw "MCP request $Method failed. $($message.error.message)" }
    return $message.result
}

function Invoke-Tool([string] $Name, [hashtable] $Arguments = @{}, [switch] $ExpectError) {
    $result = Invoke-Request 'tools/call' @{ name = $Name; arguments = $Arguments }
    if ($ExpectError) {
        Assert-Result ($result.isError -eq $true) "Expected an error from $Name."
    }
    elseif ($result.isError) {
        throw "MCP tool $Name failed. $($result.content[0].text)"
    }
    return $result
}

function Test-Preview([hashtable] $Result, [int] $Width, [int] $Height) {
    $preview = $Result.structuredContent.preview
    Assert-Result ($preview.width -eq $Width -and $preview.height -eq $Height) 'Unexpected preview dimensions.'
    $image = @($Result.content | Where-Object type -eq 'image')
    Assert-Result ($image.Count -eq 1 -and $image[0].mimeType -eq 'image/png') 'Expected an MCP PNG image.'
    $bytes = [Convert]::FromBase64String($image[0].data)
    Assert-Result ([Convert]::ToHexString($bytes[0..7]) -eq '89504E470D0A1A0A') 'Invalid PNG preview.'
}

try {
    $startInfo = [Diagnostics.ProcessStartInfo]::new((Resolve-Path -LiteralPath $AppPath).Path)
    $startInfo.ArgumentList.Add('--mcp')
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $child = [Diagnostics.Process]::Start($startInfo)
    $stderr = $child.StandardError.ReadToEndAsync()

    $server = Invoke-Request 'initialize' @{
        protocolVersion = '2025-11-25'; capabilities = @{}; clientInfo = @{ name = 'SnipNation test'; version = '1.0' }
    }
    Assert-Result ($server.serverInfo.name -eq 'SnipNation' -and $server.protocolVersion -eq '2025-11-25') 'MCP initialization failed.'
    Send-Message @{ jsonrpc = '2.0'; method = 'notifications/initialized' }

    $tools = (Invoke-Request 'tools/list').tools.name
    foreach ($name in @('list_capture_targets', 'capture_screenshot', 'get_screenshot', 'list_screenshots', 'edit_screenshot',
            'copy_screenshot', 'export_screenshot', 'list_presets', 'save_preset', 'get_workspace', 'set_capture_folder', 'open_editor')) {
        Assert-Result ($tools -contains $name) "Missing MCP tool $name."
    }
    $prompt = Invoke-Request 'prompts/get' @{
        name = 'create_guide'; arguments = @{ goal = 'Save a sample document'; outputFolder = $testRoot }
    }
    Assert-Result ($prompt.messages[0].content.text -like '*Save a sample document*') 'Guide prompt was not returned.'

    $folder = Join-Path $testRoot 'Captures'
    $changedFolder = $true
    $null = Invoke-Tool 'set_capture_folder' @{ path = $folder }
    Assert-Result ((Invoke-Tool 'get_workspace').structuredContent.captureFolder -eq $folder) 'The workspace was not saved.'
    $targets = (Invoke-Tool 'list_capture_targets').structuredContent
    $display = $targets.displays | Where-Object primary | Select-Object -First 1
    Assert-Result ($display.bounds.width -ge 320 -and $display.bounds.height -ge 200) 'A usable display is required.'
    $region = @{ x = $display.bounds.x; y = $display.bounds.y; width = 320; height = 200 }

    $capture = Invoke-Tool 'capture_screenshot' @{ target = 'region'; region = $region }
    Test-Preview $capture 320 200
    $id = $capture.structuredContent.screenshot.id
    $initialRevision = $capture.structuredContent.screenshot.revision
    $originalPath = Join-Path (Join-Path $folder $id) 'original.png'
    $originalHash = (Get-FileHash -LiteralPath $originalPath).Hash
    Assert-Result ((Invoke-Tool 'list_screenshots').structuredContent.screenshots[0].id -eq $id) 'The capture is missing from the gallery.'
    Assert-Result ((Invoke-Tool 'list_presets' @{ kind = 'frame' }).structuredContent.presets.Count -eq 6) 'The frame presets are unavailable.'

    $operations = @(
        @{ action = 'crop'; region = @{ x = 10; y = 10; width = 280; height = 170 } }
        @{ action = 'add'; annotation = @{ kind = 'frame'; preset = 2; points = @(@{ x = 50; y = 42 }, @{ x = 185; y = 105 }); style = @{ color = '#4D6CEE' } } }
        @{ action = 'add'; annotation = @{ kind = 'number'; points = @(@{ x = 24; y = 40 }, @{ x = 53; y = 50 }) } }
        @{ action = 'add'; annotation = @{ kind = 'arrow'; points = @(@{ x = 210; y = 76 }, @{ x = 190; y = 76 }); style = @{ arrowhead = 'curved' } } }
        @{ action = 'add'; annotation = @{ kind = 'line'; points = @(@{ x = 50; y = 110 }, @{ x = 185; y = 110 }) } }
        @{ action = 'add'; annotation = @{ kind = 'highlight'; points = @(@{ x = 53; y = 60 }, @{ x = 170; y = 90 }) } }
        @{ action = 'add'; annotation = @{ kind = 'freehand'; points = @(@{ x = 205; y = 95 }, @{ x = 232; y = 107 }, @{ x = 244; y = 86 }) } }
        @{ action = 'add'; annotation = @{ kind = 'text'; text = 'Save the document'; points = @(@{ x = 30; y = 130 }); style = @{ size = 18; outline = $true } } }
    )
    $edited = Invoke-Tool 'edit_screenshot' @{ id = $id; revision = $initialRevision; operations = $operations }
    Test-Preview $edited 280 170
    $document = $edited.structuredContent.screenshot
    Assert-Result ($document.annotations.Count -eq 7 -and $document.nextNumber -eq 2) 'The annotations were not saved.'
    Assert-Result ($document.annotations[0].style.color -eq '#FF4D6CEE') 'The custom color was not applied.'

    $changes = @(
        @{ action = 'update'; index = 0; annotation = @{ style = @{ frame = 'ellipse'; size = 6 } } }
        @{ action = 'remove'; index = 4 }
    )
    $edited = Invoke-Tool 'edit_screenshot' @{ id = $id; revision = $document.revision; operations = $changes; includePreview = $false }
    $document = $edited.structuredContent.screenshot
    Assert-Result ($document.annotations.Count -eq 6 -and $document.annotations[0].style.frame -eq 'ellipse') 'An update or removal failed.'
    $reset = @(@{ action = 'reset' })
    $preview = Invoke-Tool 'edit_screenshot' @{ id = $id; revision = $document.revision; operations = $reset; save = $false }
    Test-Preview $preview 320 200
    $null = Invoke-Tool 'edit_screenshot' @{ id = $id; revision = $initialRevision; operations = $reset } -ExpectError
    $invalid = @(@{ action = 'remove'; index = 2 }, @{ action = 'crop'; region = @{ x = 0; y = 0; width = 400; height = 300 } })
    $null = Invoke-Tool 'edit_screenshot' @{ id = $id; revision = $document.revision; operations = $invalid } -ExpectError
    $reopened = Invoke-Tool 'get_screenshot' @{ id = $id; original = $true }
    Test-Preview $reopened 320 200
    Assert-Result ($reopened.structuredContent.screenshot.revision -eq $document.revision -and
        $reopened.structuredContent.screenshot.annotations.Count -eq 6) 'A preview or failed edit changed the screenshot.'
    Assert-Result ((Get-FileHash -LiteralPath $originalPath).Hash -eq $originalHash) 'The original screenshot changed.'

    $null = Invoke-Tool 'copy_screenshot' @{ id = $id }
    $png = Join-Path $testRoot 'Guide/01-save.png'
    $jpg = Join-Path $testRoot 'Guide/01-save.jpg'
    $result = Invoke-Tool 'export_screenshot' @{ id = $id; path = $png }
    Assert-Result ($result.structuredContent.width -eq 280 -and (Get-Item -LiteralPath $png).Length -gt 8) 'PNG export failed.'
    $null = Invoke-Tool 'export_screenshot' @{ id = $id; path = $png } -ExpectError
    $null = Invoke-Tool 'export_screenshot' @{ id = $id; path = $jpg }
    Assert-Result ([Convert]::ToHexString([IO.File]::ReadAllBytes($jpg)[0..2]) -eq 'FFD8FF') 'JPEG export failed.'
    $null = Invoke-Tool 'open_editor' @{ id = $id }

    $child.StandardInput.Close()
    Assert-Result ($child.WaitForExit(10000)) 'The MCP server did not exit after stdin closed.'
    Assert-Result ($child.ExitCode -eq 0) "The MCP server exited with an error. $($stderr.GetAwaiter().GetResult())"
    Write-Output 'Verified MCP initialization, capture, image previews, all annotation kinds, edit conflicts, clipboard, export, and editor handoff.'
}
finally {
    if ($child) {
        if (-not $child.HasExited) { $child.Kill() }
        $child.Dispose()
    }
    if ($changedFolder) {
        if ($null -eq $previousPreferences) {
            Remove-ItemProperty -LiteralPath $settings -Name Preferences -ErrorAction SilentlyContinue
        }
        else {
            Set-ItemProperty -LiteralPath $settings -Name Preferences -Value $previousPreferences
        }
    }
    Remove-Item -LiteralPath $testRoot -Recurse -Force
}
