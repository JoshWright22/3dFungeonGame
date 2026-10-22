# Fetches the third-party content this project needs and is not allowed to commit:
#   - Quaternius packs (QAL v1.0: free for games, but no redistribution of the assets as assets)
#   - GodotSteam GDExtension (Steam lobbies + SteamMultiplayerPeer)
# Run from the repo root:  powershell -ExecutionPolicy Bypass -File tools/setup.ps1
# Downloads are cached in .cache/ so re-running only re-copies.

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$Root = Split-Path -Parent $PSScriptRoot
$Cache = Join-Path $Root '.cache\downloads'
$Assets = Join-Path $Root 'assets\quaternius'
New-Item -ItemType Directory -Force $Cache, $Assets | Out-Null

$GodotSteamUrl = 'https://codeberg.org/godotsteam/godotsteam/releases/download/v4.23.2-gde/godotsteam-4.23.2-gdextension-plugin-4.4.zip'

# slug on quaternius.itch.io -> list of (source folder pattern inside the zip, destination under assets/quaternius)
$Packs = [ordered]@{
	'medieval-village-megakit'          = @(, @('*\glTF', 'village'))
	'fantasy-props-megakit'             = @(, @('*\Exports\glTF', 'props'))
	'stylized-nature-megakit'           = @(, @('*\glTF', 'nature'))
	'bestiary-dungeon-monsters-kit'     = @(, @('*\GLB (Godot-Unreal)', 'bestiary'))
	'modular-character-outfits-fantasy' = @(@('*\glTF (Godot-Unreal)\Outfits', 'outfits'), @('*\glTF (Godot-Unreal)\Modular Parts', 'outfits\parts'))
	'universal-base-characters'         = @(@('*\Base Characters\Godot - UE', 'characters'), @('*\Rigged to Head Bone\glTF (Godot -Unreal)', 'characters\hair'))
	'universal-animation-library'       = @(, @('*\Universal Animation Library`[Standard`]\Unreal-Godot', 'animations\ual1'))
	'universal-animation-library-2'     = @(, @('*\Universal Animation Library 2`[Standard`]\Unreal-Godot', 'animations\ual2'))
}

function Get-ItchUploads([string]$slug, [string]$dest) {
	# itch.io "name your price" flow: page -> download_url (csrf) -> download page -> POST file/<upload id> -> CDN url.
	if (Get-ChildItem $dest -Filter *.zip -ErrorAction SilentlyContinue) { return }
	New-Item -ItemType Directory -Force $dest | Out-Null
	$base = "https://quaternius.itch.io/$slug"
	$page = Invoke-WebRequest $base -SessionVariable s -UseBasicParsing
	$csrf = [regex]::Match($page.Content, 'name="csrf_token" value="([^"]+)"').Groups[1].Value
	$r = Invoke-RestMethod "$base/download_url" -Method Post -Body @{ csrf_token = $csrf } -WebSession $s
	$dl = Invoke-WebRequest $r.url -WebSession $s -UseBasicParsing
	$csrf = [regex]::Match($dl.Content, 'csrf_token" value="([^"]+)"').Groups[1].Value
	$ids = [regex]::Matches($dl.Content, 'data-upload_id="(\d+)"') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique
	foreach ($id in $ids) {
		$f = Invoke-RestMethod "$base/file/${id}?source=game_download" -Method Post -Body @{ csrf_token = $csrf } -WebSession $s
		if (-not $f.url) { continue }   # paid tiers (Pro/Source) return an error; skip them
		Invoke-WebRequest $f.url -OutFile (Join-Path $dest "$id.zip") -UseBasicParsing
		Write-Host "  downloaded $slug upload $id"
	}
}

function Resolve-MissingTextures([string]$dir, [string]$packRoot) {
	# Some glTFs reference textures that live in a sibling Textures/ folder, or under a misspelt name
	# (e.g. T_Eye_Normal_png.png for T_Eye_Normal.png). Copy them next to the glTF under the referenced name.
	# Prefer the Godot-flavoured normal maps when the pack ships both conventions.
	$all = Get-ChildItem $packRoot -Recurse -File -Filter *.png | Sort-Object { $_.FullName -notlike '*Godot*' }
	foreach ($g in Get-ChildItem -LiteralPath $dir -Filter *.gltf) {
		$uris = [regex]::Matches((Get-Content -LiteralPath $g.FullName -Raw), '"uri"\s*:\s*"([^"]+\.png)"') | ForEach-Object { $_.Groups[1].Value }
		foreach ($u in $uris | Select-Object -Unique) {
			$target = Join-Path $dir $u
			if (Test-Path -LiteralPath $target) { continue }
			$name = [IO.Path]::GetFileName($u) -replace '_png\.png$', '.png'
			$hit = $all | Where-Object { $_.Name -eq $name } | Select-Object -First 1
			if ($hit) { Copy-Item -LiteralPath $hit.FullName -Destination $target -Force }
			else { Write-Warning "  $($g.Name) references missing $u" }
		}
	}
}

foreach ($slug in $Packs.Keys) {
	Write-Host "== $slug"
	$dl = Join-Path $Cache $slug
	Get-ItchUploads $slug $dl
	$x = Join-Path $dl 'x'
	if (-not (Test-Path $x)) {
		foreach ($z in Get-ChildItem $dl -Filter *.zip) { Expand-Archive -LiteralPath $z.FullName -DestinationPath $x -Force }
	}
	foreach ($map in $Packs[$slug]) {
		$src = Get-ChildItem $x -Recurse -Directory | Where-Object { $_.FullName -like "*$($map[0].TrimStart('*'))" } | Select-Object -First 1
		if (-not $src) { Write-Warning "  no folder like $($map[0]) in $slug"; continue }
		$to = Join-Path $Assets $map[1]
		New-Item -ItemType Directory -Force $to | Out-Null
		Get-ChildItem -LiteralPath $src.FullName -File | Copy-Item -Destination $to -Force
		Resolve-MissingTextures $to $x
		Write-Host "  -> assets/quaternius/$($map[1])"
	}
}

Write-Host '== GodotSteam'
$gsZip = Join-Path $Cache 'godotsteam.zip'
if (-not (Test-Path $gsZip)) { Invoke-WebRequest $GodotSteamUrl -OutFile $gsZip -UseBasicParsing }
Expand-Archive -LiteralPath $gsZip -DestinationPath $Root -Force   # zip root is addons/godotsteam
Write-Host 'Done. Open the project in Godot 4.7 (first import takes a few minutes).'
