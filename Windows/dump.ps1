Add-Type -AssemblyName System.Drawing
$files = @(Get-ChildItem artifacts/ui-previews -Recurse -Filter 'card-*@100.png' | Sort-Object FullName)
$codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object MimeType -eq 'image/jpeg'
foreach ($f in $files) {
  foreach ($q in 85, 70, 55, 40) {
    $img = [System.Drawing.Image]::FromFile($f.FullName)
    $ms = New-Object IO.MemoryStream
    $p = New-Object System.Drawing.Imaging.EncoderParameters 1
    $p.Param[0] = [System.Drawing.Imaging.EncoderParameter]::new([System.Drawing.Imaging.Encoder]::Quality, [long]$q)
    $img.Save($ms, $codec, $p); $img.Dispose()
    $b = [Convert]::ToBase64String($ms.ToArray())
    if ($b.Length -le 120000) { break }
  }
  $summary = $b.Substring(0, [Math]::Min(60000, $b.Length))
  $text = if ($b.Length -gt 60000) { $b.Substring(60000) } else { '-' }
  $body = @{ name = "preview $($f.Directory.Name)/$($f.Name)"; head_sha = $env:GITHUB_SHA; status = 'completed'; conclusion = 'neutral';
    output = @{ title = 'preview'; summary = $summary; text = $text } } | ConvertTo-Json -Depth 4
  $body | gh api "repos/$env:GH_REPO/check-runs" --input - --silent
}
