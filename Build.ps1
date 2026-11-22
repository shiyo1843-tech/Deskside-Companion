param([string]$UnityEditor="C:/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe")
$ErrorActionPreference="Stop"
$project=Join-Path $PSScriptRoot "UnityProject"
$delivery=Join-Path $PSScriptRoot "Windows"
$examples=Join-Path $delivery "示例模型"
New-Item -ItemType Directory -Force $delivery,$examples | Out-Null
Copy-Item -Path (Join-Path $project 'Assets/StreamingAssets/*.glb') -Destination $examples
$manual=Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $PSScriptRoot 'README.md')
$manual=$manual -replace '(?m)^<img [^\r\n]*>\r?\n',''
$manual=$manual.Replace('UnityProject/NativeHost/Brand/NOTICE.md','第三方许可/Lime-icon.md')
[IO.File]::WriteAllText((Join-Path $delivery '使用说明.md'),$manual)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '使用说明.txt') -Destination $delivery -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '桌宠角色导出工具.unitypackage') -Destination $delivery -Force
$licenses=New-Item -ItemType Directory -Force (Join-Path $delivery '第三方许可')
Copy-Item (Join-Path $PSScriptRoot 'LICENSE') -Destination (Join-Path $delivery 'LICENSE') -Force
Copy-Item (Join-Path $project 'NativeHost/Icons/LICENSE-BlackStartx.txt') (Join-Path $licenses.FullName 'GestureManager-Icons.txt') -Force
Copy-Item (Join-Path $project 'ExternalTools/Notices/GameMotion.txt') -Destination $licenses.FullName -Force
Copy-Item (Join-Path $project 'NativeHost/Brand/NOTICE.md') -Destination (Join-Path $licenses.FullName 'Lime-icon.md') -Force
Copy-Item (Join-Path $project 'Packages/com.vrmc.gltf/LICENSE.md') (Join-Path $licenses.FullName 'UniGLTF.md') -Force
Copy-Item (Join-Path $project 'Packages/com.vrmc.vrm/LICENSE.txt') (Join-Path $licenses.FullName 'UniVRM.txt') -Force
$log=Join-Path ([IO.Path]::GetTempPath()) ("DeskPet-Unity-"+[guid]::NewGuid().ToString("N")+".log")
$player=Join-Path $delivery 'Renderer/Portrait.exe'
$editor=Start-Process -FilePath $UnityEditor -WindowStyle Hidden -ArgumentList @("-batchmode","-quit","-buildTarget","StandaloneWindows64","-projectPath",('"{0}"' -f $project),"-executeMethod","PetBuild.Windows","-petBuildPath",('"{0}"' -f $player),"-logFile",('"{0}"' -f $log)) -PassThru
$editor.WaitForExit()
if($editor.ExitCode -ne 0 -or !(Test-Path -LiteralPath $player)){throw "Unity 构建失败，日志位于 $log"}
dotnet publish (Join-Path $project "NativeHost/DeskPet.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o $delivery
if($LASTEXITCODE -ne 0){throw "桌面界面构建失败"}
Write-Output (Join-Path $delivery "桌边伙伴.exe")
