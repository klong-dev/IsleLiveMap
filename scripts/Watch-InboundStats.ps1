param([Parameter(Mandatory)][string]$SessionPath,[Parameter(Mandatory)][int]$LauncherPid,[int]$Seconds=600)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$started=[DateTimeOffset]::UtcNow
$end=$started.AddSeconds($Seconds)
$log=Join-Path $SessionPath 'map-diagnostics.jsonl'
$output=[IO.StreamWriter]::new([IO.FileStream]::new((Join-Path $SessionPath 'stats-watch.jsonl'),[IO.FileMode]::CreateNew)); $output.AutoFlush=$true
$ui=[IO.StreamWriter]::new([IO.FileStream]::new((Join-Path $SessionPath 'ui-watch.jsonl'),[IO.FileMode]::CreateNew)); $ui.AutoFlush=$true
$stream=$null; $reader=$null; $buffer=''; $lastKey=''; $prior=$null; $frames=0; $changes=0; $jumps=0; $uiSamples=0; $lastUi=$started.AddSeconds(-10); $lastProgress=$started
function PickValue($fresh,$held,[string]$name) { if ($null -ne $fresh -and $null -ne $fresh.$name) { return $fresh.$name }; if ($null -ne $held) { return $held.$name }; return $null }
try {
 Write-Output ("WATCH_STARTED {0:o} END={1:o}" -f $started,$end)
 while ([DateTimeOffset]::UtcNow -lt $end) {
  if ($null -eq $reader -and (Test-Path -LiteralPath $log)) { $stream=[IO.File]::Open($log,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite); $reader=[IO.StreamReader]::new($stream) }
  if ($null -ne $reader) {
   $buffer += $reader.ReadToEnd()
   $lastNewline=$buffer.LastIndexOf([char]10)
   if ($lastNewline -ge 0) {
    $complete=$buffer.Substring(0,$lastNewline); $buffer=$buffer.Substring($lastNewline+1)
    foreach ($line in $complete.Split([char]10)) {
     if ([string]::IsNullOrWhiteSpace($line)) { continue }
     try { $f=ConvertFrom-Json -InputObject $line -ErrorAction Stop } catch { $output.WriteLine((@{Type='ParseError';At=[DateTimeOffset]::UtcNow;Message=$_.Exception.Message} | ConvertTo-Json -Compress)); continue }
     if ($f.stage -ne 'render-end') {continue}; $frames++
     $p=$f.PlayerIdentity; $fresh=$p.ExactVitals; $held=$p.InboundStatsLastKnown
     $v=[ordered]@{}
     foreach ($name in @('Growth','Health','MaxHealth','Stamina','MaxStamina','Hunger','MaxHunger','Thirst','MaxThirst')) { $v[$name]=PickValue $fresh $held $name }
     foreach ($name in @('Health','Stamina','Hunger','Thirst')) {
      $max=$v['Max'+$name]; $current=$v[$name]; $v[$name+'Percent']=if ($null -ne $current -and $max -gt 0) { [Math]::Clamp(($current/$max*100),0,100) } else { $null }
     }
     $key=(@{Owner=$p.InboundStatsOwnerHandle;Server=$f.ServerEndpoint;Values=$v} | ConvertTo-Json -Compress -Depth 5)
     if ($key -ne $lastKey) {
      $changes++; $event=[ordered]@{Type='Change';At=$f.ReceivedAt;Owner=$p.InboundStatsOwnerHandle;Server=$f.ServerEndpoint;Values=$v;FieldTimes=$p.InboundStatsFieldTimes;Fresh=$fresh;Held=$held;JumpFields=@()}
      if ($prior -and $prior.Owner -eq $event.Owner -and $prior.Server -eq $event.Server) {
       foreach ($field in @('HealthPercent','StaminaPercent')) {
        if ($null -ne $v[$field] -and $null -ne $prior.Values[$field] -and [Math]::Abs($v[$field]-$prior.Values[$field]) -ge 20) { $event.JumpFields += $field }
       }
      }
      if ($event.JumpFields.Count) { $jumps++; Write-Output ("JUMP {0} owner={1} hp={2}/{3} stamina={4}/{5}" -f $event.At,$event.Owner,$v.Health,$v.MaxHealth,$v.Stamina,$v.MaxStamina) }
      $output.WriteLine(($event | ConvertTo-Json -Compress -Depth 8)); $prior=$event; $lastKey=$key
     }
    }
   }
  }
  $now=[DateTimeOffset]::UtcNow
  if (($now-$lastUi).TotalSeconds -ge 10) {
   $lastUi=$now; $labels=[ordered]@{}
   try {
    $process=Get-Process -Id $LauncherPid -ErrorAction Stop; $root=[System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    foreach ($id in @('GrowthLabel','HealthValue','StaminaValue','HungerValue','WaterValue','ConnectionLabel','HealthBar','StaminaBar')) {
     $condition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$id)
     $element=$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
     if ($element) { $labels[$id]=$element.Current.Name; if ($id -like '*Bar') { try { $labels[$id]=([System.Windows.Automation.RangeValuePattern]$element.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)).Current.Value } catch {} } }
    }
    $uiSamples++; $ui.WriteLine((@{At=$now;Labels=$labels} | ConvertTo-Json -Compress -Depth 4))
   } catch { $ui.WriteLine((@{At=$now;Error=$_.Exception.Message} | ConvertTo-Json -Compress)) }
  }
  if (($now-$lastProgress).TotalSeconds -ge 30) { $lastProgress=$now; Write-Output ("PROGRESS elapsed={0:N0}s frames={1} changes={2} jumps={3} ui={4}" -f ($now-$started).TotalSeconds,$frames,$changes,$jumps,$uiSamples) }
  Start-Sleep -Milliseconds 200
 }
} finally {
 if ($reader) {$reader.Dispose()}; if ($stream) {$stream.Dispose()}; $output.Dispose(); $ui.Dispose()
 $summary=[ordered]@{StartedUtc=$started;FinishedUtc=[DateTimeOffset]::UtcNow;RequestedSeconds=$Seconds;Frames=$frames;Changes=$changes;JumpEvents=$jumps;UiSamples=$uiSamples;UnfinishedLineBytes=$buffer.Length;Status='OBSERVATION_ONLY'}
 $summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $SessionPath 'watch-summary.json') -Encoding utf8
 Write-Output ($summary | ConvertTo-Json -Compress)
}
