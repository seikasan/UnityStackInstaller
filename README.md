# UnityStackInstaller

NuGetForUnity, VContainer, MessagePipe, UniTask, R3, ObservableCollections.R3, ZLinq を入れる Package。

## Package

Minimum Unity version: Unity 6 (`6000.0`)

`com.seikasan.unity-stack-installer` は Editor 専用の UPM パッケージです。

依存一覧は `stack.json` に固定バージョンで定義しています。UPM は `Packages/manifest.json`、NuGet は NuGetForUnity 用の `Assets/packages.config` に分けて管理します。

## 使い方

Unity Editor のメニューから実行します。

- `Tools > Unity Stack Installer > Install`
- `Tools > Unity Stack Installer > Verify`
- `Tools > Unity Stack Installer > Repair`

`Install` と `Repair` は不足している OpenUPM scope、UPM package、NuGet package だけを追加します。既に導入済みの依存は、`stack.json` とバージョンが違っていても上書きしません。バージョン差分は `Verify` の結果と Console に表示されます。

## 固定バージョン

`stack.json` の内容が唯一の定義元です。

UPM:

- `com.github-glitchenzo.nugetforunity` `4.5.0`
- `jp.hadashikick.vcontainer` `1.18.0`
- `com.cysharp.messagepipe` `1.8.2`
- `com.cysharp.unitask` `2.5.11`

NuGet:

- `System.Threading.Tasks.Extensions` `4.5.4`
- `Microsoft.Bcl.AsyncInterfaces` `8.0.0`
- `Microsoft.Bcl.TimeProvider` `8.0.0`
- `System.ComponentModel.Annotations` `5.0.0`
- `System.Runtime.CompilerServices.Unsafe` `6.1.2`
- `System.Threading.Channels` `8.0.0`
- `ObservableCollections` `3.3.4`
- `R3` `1.3.1`
- `ObservableCollections.R3` `3.3.4`
- `ZLinq` `1.5.6`
