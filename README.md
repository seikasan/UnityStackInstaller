# UnityStackInstaller

Unity Stack Installer.

[![license](https://img.shields.io/badge/LICENSE-MIT-green.svg)](LICENSE)

## 概要

この Unity パッケージは、「私が」プロジェクトで共通して使用する依存関係を段階的にインストールします。

重要なのは、R3 と ZLinq は単なる Unity の Git パッケージではないという点です。これらを Unity にインストールするには、最初に NuGet パッケージを導入し、その後で Unity の Git パッケージを追加する必要があります。このインストーラーは、その順序に従って処理を行います。

## Unity Package Manager によってインストールされるもの

- VContainer
- UniTask
- MessagePipe
- MessagePipe.VContainer
- SerializeReference Extensions
- ReactiveInputSystem
- LitMotion
- LitMotion.Animation
- NuGetForUnity
- R3.Unity
- ZLinq.Unity

## NuGetForUnity によってインストールされるもの

- R3
- ObservableCollections
- ObservableCollections.R3
- ZLinq

## 使用方法

1. Window > Package ManagerからPackage Managerを開く
2. 「+」ボタン > Add package from git URL
3. 以下のURLを入力する

```
https://github.com/seikasan/UnityStackInstaller.git?path=UnityStackInstaller
```

インストーラーは一度だけ自動的に実行されます。再実行する場合は、以下を使用してください。

`Tools > Unity Stack Installer > Install Packages`

以前のバージョンによってプロジェクトがすでに壊れた状態になっている場合は、`Packages/manifest.json` から `com.cysharp.r3` と `com.cysharp.zlinq` を削除し、Unity に依存関係を再解決させてから、このバージョンをインポートしてインストーラーを再実行してください。

状態ファイルは以下です。

- `ProjectSettings/UnityStackInstaller.state`
- `ProjectSettings/UnityStackInstaller.installed`
