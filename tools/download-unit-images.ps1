<#
  Download Unit Pictures - fetches the unit pictures Army Forge shows (army-forge.onepagerules.com)
  and saves them where the game looks for them:

    UnitImages\<army>\Portraits\<unit>.png   - the round 2D token portrait (Army Forge's avatar picture)
    UnitImages\<army>\<unit>.png             - the 3D standee picture (Army Forge's first unit picture), optional

  <army> is the Army Forge book name, which is the "faction" the game's books and army files carry, so
  UnitImageLibrary finds them with no renaming. File names are the unit names; the game ignores case,
  spaces and punctuation when matching.

  Rules:
  - A picture that is already there is NEVER replaced, so your own pictures always win. Delete a file to
    have it fetched again.
  - Every picture this tool saves is listed in a .gitignore next to it. The pictures are OPR's artwork:
    they stay on this PC and are never pushed to GitHub. Your own pictures still sync as before.
  - Pictures are shrunk on the way in (portraits to 256px, standees to 768px - the game never draws them
    bigger), which keeps a whole game system to roughly a hundred MB instead of several GB.

  Run it from "Download Unit Pictures.bat" in the game folder. Windows PowerShell 5.1 or newer.
#>

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # PowerShell 5.1's download progress bar makes every download many times slower
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.Drawing

$Api = 'https://army-forge.onepagerules.com/api'
$Cdn = 'https://army-forge.opr-cdn.com/'
$PortraitMax = 256
$StandeeMax = 768

$Root = Split-Path -Parent $PSScriptRoot
$ImagesRoot = Join-Path $Root 'UnitImages'

# Each menu choice is one or more Army Forge game systems: (id, slug the book index is keyed by).
# Quest games are two ids each - the heroes and the enemies have separate books (see CLAUDE.md).
$Systems = [ordered]@{
    '1' = @{ Name = 'Grimdark Future';           Ids = @(@(2,  'grimdark-future')) }
    '2' = @{ Name = 'GF: Firefight';             Ids = @(@(3,  'grimdark-future-firefight')) }
    '3' = @{ Name = 'Age of Fantasy';            Ids = @(@(4,  'age-of-fantasy')) }
    '4' = @{ Name = 'AoF: Skirmish';             Ids = @(@(5,  'age-of-fantasy-skirmish')) }
    '5' = @{ Name = 'GF: Star Quest (heroes + enemies)'; Ids = @(@(9, 'grimdark-future-star-quest'), @(10, 'grimdark-future-star-quest')) }
    '6' = @{ Name = 'AoF: Quest (heroes + enemies)';     Ids = @(@(7, 'age-of-fantasy-quest'), @(8, 'age-of-fantasy-quest')) }
}

function Get-SafeName([string]$name) {
    $bad = [IO.Path]::GetInvalidFileNameChars()
    $clean = -join ($name.Trim().ToCharArray() | Where-Object { $bad -notcontains $_ })
    return $clean.Trim().TrimEnd('.')
}

function Normalise([string]$name) {
    return (-join ($name.ToCharArray() | Where-Object { [char]::IsLetterOrDigit($_) })).ToLowerInvariant()
}

# The game matches a unit to a picture by its normalised name, in any of these formats. A picture that
# already exists under ANY spelling or extension counts as "already there".
$Extensions = @('.png', '.jpg', '.jpeg', '.bmp', '.tga')
function Test-HasPicture([string]$folder, [string]$unitName) {
    if (-not (Test-Path -LiteralPath $folder)) { return $false }
    $want = Normalise $unitName
    foreach ($f in Get-ChildItem -LiteralPath $folder -File) {
        if ($Extensions -contains $f.Extension.ToLowerInvariant() -and (Normalise $f.BaseName) -eq $want) { return $true }
    }
    return $false
}

# Finds the army's folder the way the game does (an existing folder whose name normalises the same),
# else makes one named after the book.
function Get-ArmyFolder([string]$bookName) {
    New-Item -ItemType Directory -Force -Path $ImagesRoot | Out-Null
    $want = Normalise $bookName
    $existing = Get-ChildItem -LiteralPath $ImagesRoot -Directory | Where-Object { (Normalise $_.Name) -eq $want } | Select-Object -First 1
    if ($existing) { return $existing.FullName }
    $path = Join-Path $ImagesRoot (Get-SafeName $bookName)
    New-Item -ItemType Directory -Force -Path $path | Out-Null
    return $path
}

# Adds one file name to the .gitignore in its folder, so the downloaded picture is never committed.
function Add-GitIgnore([string]$folder, [string]$fileName) {
    $gi = Join-Path $folder '.gitignore'
    $escaped = '/' + ($fileName -replace '([\[\]\*\?\\!#])', '\$1')
    if (Test-Path -LiteralPath $gi) {
        if ((Get-Content -LiteralPath $gi) -contains $escaped) { return }
    } else {
        Set-Content -LiteralPath $gi -Encoding ASCII -Value '# Pictures downloaded from Army Forge by tools/download-unit-images.ps1 - OPR artwork, kept on this PC only.'
    }
    Add-Content -LiteralPath $gi -Encoding UTF8 -Value $escaped
}

# Downloads a picture, shrinks it to fit maxSide (keeping transparency) and saves it as PNG.
function Save-Picture([string]$url, [string]$target, [int]$maxSide) {
    $tmp = [IO.Path]::GetTempFileName()
    try {
        Invoke-WebRequest -Uri $url -OutFile $tmp -UseBasicParsing -TimeoutSec 60
        $src = [Drawing.Image]::FromFile($tmp)
        try {
            $scale = [Math]::Min(1.0, $maxSide / [double][Math]::Max($src.Width, $src.Height))
            $w = [Math]::Max(1, [int]($src.Width * $scale))
            $h = [Math]::Max(1, [int]($src.Height * $scale))
            $bmp = New-Object Drawing.Bitmap $w, $h, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
            try {
                $g = [Drawing.Graphics]::FromImage($bmp)
                $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
                $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $g.DrawImage($src, 0, 0, $w, $h)
                $g.Dispose()
                $bmp.Save($target, [Drawing.Imaging.ImageFormat]::Png)
            } finally { $bmp.Dispose() }
        } finally { $src.Dispose() }
    } finally {
        Remove-Item -LiteralPath $tmp -ErrorAction SilentlyContinue
    }
}

function Get-Json([string]$url) {
    for ($try = 1; $try -le 3; $try++) {
        try { return Invoke-RestMethod -Uri $url -UseBasicParsing -TimeoutSec 60 }
        catch { if ($try -eq 3) { throw }; Start-Sleep -Seconds (2 * $try) }
    }
}

# ---------------------------------------------------------------------------------------------- menu

Write-Host ''
Write-Host 'Download Unit Pictures from Army Forge' -ForegroundColor Cyan
Write-Host "Pictures go into: $ImagesRoot"
Write-Host 'Pictures you already have are never replaced.'
Write-Host ''
foreach ($k in $Systems.Keys) { Write-Host "  $k) $($Systems[$k].Name)" }
Write-Host '  A) All of them'
Write-Host ''
$pick = (Read-Host 'Which game? (number, several like 1,3, or A)').Trim().ToUpperInvariant()
$keys = if ($pick -eq 'A') { @($Systems.Keys) } else { @($pick -split '[,\s]+' | Where-Object { $Systems.Contains($_) }) }
if ($keys.Count -eq 0) { Write-Host 'Nothing picked.'; return }

$filter = (Read-Host 'Only one army? Type part of its name, or just press Enter for every army').Trim()
$standees = (Read-Host 'Also get the 3D standee pictures? (Y/N, Enter = Y)').Trim().ToUpperInvariant() -ne 'N'

# ---------------------------------------------------------------------------------------------- work

$saved = 0; $skipped = 0; $missing = 0; $failed = 0
$failures = New-Object System.Collections.Generic.List[string]

foreach ($k in $keys) {
    foreach ($sys in $Systems[$k].Ids) {
        $id = $sys[0]; $slug = $sys[1]
        Write-Host ''
        Write-Host "== $($Systems[$k].Name) (Army Forge system $id) ==" -ForegroundColor Cyan
        try { $index = Get-Json "$Api/army-books?filters=official&gameSystemSlug=$slug" }
        catch { Write-Host "  Could not reach Army Forge: $($_.Exception.Message)" -ForegroundColor Red; continue }
        $books = @($index | Where-Object { -not $filter -or $_.name -like "*$filter*" } | Sort-Object name)
        if ($books.Count -eq 0) { Write-Host '  No army matches that name here.'; continue }

        foreach ($b in $books) {
            try { $book = Get-Json "$Api/army-books/$($b.uid)?gameSystem=$id" }
            catch { $failed++; $failures.Add("$($b.name): book did not load"); continue }

            $armyFolder = Get-ArmyFolder $book.name
            $portraitFolder = Join-Path $armyFolder 'Portraits'
            New-Item -ItemType Directory -Force -Path $portraitFolder | Out-Null
            $got = 0

            foreach ($u in $book.units) {
                $file = (Get-SafeName $u.name) + '.png'
                if (-not $file -or $file -eq '.png') { continue }
                $p = $u.product

                # Portrait (avatar)
                if (-not $p -or -not $p.avatarImageUrl) { $missing++ }
                elseif (Test-HasPicture $portraitFolder $u.name) { $skipped++ }
                else {
                    try {
                        Save-Picture ($Cdn + $p.avatarImageUrl) (Join-Path $portraitFolder $file) $PortraitMax
                        Add-GitIgnore $portraitFolder $file
                        $saved++; $got++
                    } catch { $failed++; $failures.Add("$($book.name) / $($u.name) portrait: $($_.Exception.Message)") }
                }

                # 3D standee (first unit picture)
                if ($standees -and $p -and $p.imageUrls -and @($p.imageUrls).Count -gt 0) {
                    if (Test-HasPicture $armyFolder $u.name) { $skipped++ }
                    else {
                        try {
                            Save-Picture ($Cdn + @($p.imageUrls)[0]) (Join-Path $armyFolder $file) $StandeeMax
                            Add-GitIgnore $armyFolder $file
                            $saved++; $got++
                        } catch { $failed++; $failures.Add("$($book.name) / $($u.name) standee: $($_.Exception.Message)") }
                    }
                }
            }
            Write-Host ("  {0,-34} {1} new" -f $book.name, $got)
        }
    }
}

Write-Host ''
Write-Host "Done. $saved new pictures, $skipped already there, $missing units have no picture on Army Forge." -ForegroundColor Green
if ($failed -gt 0) {
    Write-Host "$failed could not be downloaded (run it again to retry):" -ForegroundColor Yellow
    $failures | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" }
}
