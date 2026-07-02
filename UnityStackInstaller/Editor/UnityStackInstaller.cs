#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace UnityStackInstaller.Editor
{
    [InitializeOnLoad]
    internal static class UnityStackInstaller
    {
        private const string DoneFilePath = "ProjectSettings/UnityStackInstaller.installed";
        private const string StateFilePath = "ProjectSettings/UnityStackInstaller.state";
        private const string GitIgnoreLine = "/[Aa]ssets/[Pp]ackages/*";

        private const string StageNuGet = "nuget";
        private const string StageUnityExtensions = "upm-unity-extensions";
        private const string StageDependentGitPackages = "dependent-git-packages";
        private const string StageDone = "done";

        private static AddAndRemoveRequest request;
        private static string nextStageAfterRequest;
        private static double nextRetryTime;

        private struct PackageSpec
        {
            public string Name;
            public string Url;

            public PackageSpec(string name, string url)
            {
                Name = name;
                Url = url;
            }
        }

        private struct NuGetSpec
        {
            public string Id;
            public string Version;

            public NuGetSpec(string id, string version)
            {
                Id = id;
                Version = version;
            }
        }

        private struct UnityExtensionPackageSpec
        {
            public string Name;
            public string Url;
            public string NuGetPackageId;

            public UnityExtensionPackageSpec(string name, string url, string nuGetPackageId)
            {
                Name = name;
                Url = url;
                NuGetPackageId = nuGetPackageId;
            }
        }

        [Serializable]
        private sealed class NuGetFlatContainerIndex
        {
            public string[] versions;
        }

        private static readonly PackageSpec[] FoundationPackages =
        {
            new PackageSpec(
                "jp.hadashikick.vcontainer",
                "https://github.com/hadashiA/VContainer.git?path=VContainer/Assets/VContainer"
            ),
            new PackageSpec(
                "com.cysharp.unitask",
                "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask"
            ),
            new PackageSpec(
                "com.cysharp.messagepipe",
                "https://github.com/Cysharp/MessagePipe.git?path=src/MessagePipe.Unity/Assets/Plugins/MessagePipe"
            ),
            new PackageSpec(
                "com.cysharp.messagepipe.vcontainer",
                "https://github.com/Cysharp/MessagePipe.git?path=src/MessagePipe.Unity/Assets/Plugins/MessagePipe.VContainer"
            ),
            new PackageSpec(
                "com.github-glitchenzo.nugetforunity",
                "https://github.com/GlitchEnzo/NuGetForUnity.git?path=/src/NuGetForUnity"
            ),
            new PackageSpec(
                "com.mackysoft.serializereference-extensions",
                "https://github.com/mackysoft/Unity-SerializeReferenceExtensions.git?path=Assets/MackySoft/MackySoft.SerializeReferenceExtensions"
            ),
            new PackageSpec(
                "com.annulusgames.lit-motion",
                "https://github.com/annulusgames/LitMotion.git?path=src/LitMotion/Assets/LitMotion"
            ),
            new PackageSpec(
                "com.annulusgames.lit-motion.animation",
                "https://github.com/annulusgames/LitMotion.git?path=src/LitMotion/Assets/LitMotion.Animation"
            )
        };

        private static readonly NuGetSpec[] NuGetPackages =
        {
            new NuGetSpec("R3", null),
            new NuGetSpec("ObservableCollections", null),
            new NuGetSpec("ObservableCollections.R3", null),
            new NuGetSpec("ZLinq", null)
        };

        private static readonly UnityExtensionPackageSpec[] UnityExtensionPackages =
        {
            new UnityExtensionPackageSpec(
                "com.cysharp.r3",
                "https://github.com/Cysharp/R3.git?path=src/R3.Unity/Assets/R3.Unity",
                "R3"
            ),
            new UnityExtensionPackageSpec(
                "com.cysharp.zlinq",
                "https://github.com/Cysharp/ZLinq.git?path=src/ZLinq.Unity/Assets/ZLinq.Unity",
                "ZLinq"
            )
        };

        private static readonly PackageSpec[] DependentGitPackages =
        {
            new PackageSpec(
                "com.seikasan.reactive-input-system",
                "https://github.com/seikasan/ReactiveInputSystem.git?path=Assets/ReactiveInputSystem"
            ),
            new PackageSpec(
                "com.seikasan.com.seikasan.r3-extensions",
                "https://github.com/seikasan/MyExtensions.git?path=R3"
            )
        };

        static UnityStackInstaller()
        {
            EditorApplication.delayCall += AutoInstallOnce;
        }

        [MenuItem("Tools/Unity Stack Installer/Install Packages")]
        private static void InstallFromMenu()
        {
            ResetState();
            RunInstaller();
        }

        [MenuItem("Tools/Unity Stack Installer/Update To Latest")]
        private static void UpdateToLatestFromMenu()
        {
            if (request != null)
            {
                Debug.LogWarning("[UnityStackInstaller] Package Manager is already running.");
                return;
            }

            if (File.Exists(DoneFilePath))
            {
                File.Delete(DoneFilePath);
            }

            WriteState(StageNuGet, true);
            RunInstaller();
        }

        [MenuItem("Tools/Unity Stack Installer/Install NuGet Core Only")]
        private static void InstallNuGetCoreOnlyFromMenu()
        {
            EnsureGitIgnore();
            InstallNuGetCorePackages();
        }

        [MenuItem("Tools/Unity Stack Installer/Reset Installer State")]
        private static void ResetStateFromMenu()
        {
            ResetState();
            Debug.Log("[UnityStackInstaller] Installer state reset.");
        }

        private static void AutoInstallOnce()
        {
            if (File.Exists(DoneFilePath))
            {
                return;
            }

            RunInstaller();
        }

        private static void RunInstaller()
        {
            if (request != null)
            {
                return;
            }

            if (EditorApplication.timeSinceStartup < nextRetryTime)
            {
                EditorApplication.delayCall += RunInstaller;
                return;
            }

            EnsureGitIgnore();

            string state = ReadState();

            if (string.IsNullOrEmpty(state))
            {
                InstallUpmPackages(FoundationPackages, StageNuGet);
                return;
            }

            if (state == StageNuGet)
            {
                InstallNuGetCorePackages();
                return;
            }

            if (state == StageUnityExtensions)
            {
                InstallUpmPackages(BuildUnityExtensionPackages(), StageDependentGitPackages);
                return;
            }

            if (state == StageDependentGitPackages)
            {
                InstallUpmPackages(DependentGitPackages, StageDone);
                return;
            }

            if (state == StageDone)
            {
                MarkDone();
                Debug.Log("[UnityStackInstaller] All packages are installed.");
                return;
            }

            Debug.LogWarning("[UnityStackInstaller] Unknown installer state. Resetting: " + state);
            ResetState();
            EditorApplication.delayCall += RunInstaller;
        }

        private static void InstallUpmPackages(PackageSpec[] packages, string nextStage)
        {
            bool forceAdd = IsUpdateMode();

            string[] packagesToAdd = packages
                .Where(package => forceAdd || !IsPackageInManifest(package.Name))
                .Select(package => package.Url)
                .ToArray();

            if (packagesToAdd.Length == 0)
            {
                WriteState(nextStage);
                EditorApplication.delayCall += RunInstaller;
                return;
            }

            Debug.Log("[UnityStackInstaller] Installing UPM packages:\n" + string.Join("\n", packagesToAdd));

            nextStageAfterRequest = nextStage;
            request = Client.AddAndRemove(packagesToAdd, new string[0]);

            EditorApplication.update -= TickPackageManager;
            EditorApplication.update += TickPackageManager;
        }

        private static void TickPackageManager()
        {
            if (request == null)
            {
                EditorApplication.update -= TickPackageManager;
                return;
            }

            if (request.Status == StatusCode.InProgress)
            {
                return;
            }

            if (request.Status == StatusCode.Success)
            {
                Debug.Log("[UnityStackInstaller] UPM package installation completed. Next stage: " + nextStageAfterRequest);
                WriteState(nextStageAfterRequest);

                request = null;
                nextStageAfterRequest = null;
                EditorApplication.update -= TickPackageManager;

                AssetDatabase.Refresh();
                EditorApplication.delayCall += RunInstaller;
                return;
            }

            if (request.Status == StatusCode.Failure)
            {
                Debug.LogError("[UnityStackInstaller] UPM package installation failed: " + request.Error.message);
                request = null;
                nextStageAfterRequest = null;
                EditorApplication.update -= TickPackageManager;
            }
        }

        private static void InstallNuGetCorePackages()
        {
            Type identifierType = FindType("NugetForUnity.Models.NugetPackageIdentifier");
            Type installerType = FindType("NugetForUnity.NugetPackageInstaller");

            if (identifierType == null || installerType == null)
            {
                Debug.Log("[UnityStackInstaller] Waiting for NuGetForUnity to finish compiling...");
                nextRetryTime = EditorApplication.timeSinceStartup + 3.0;
                EditorApplication.delayCall += RunInstaller;
                return;
            }

            ConstructorInfo constructor = identifierType.GetConstructor(new[] { typeof(string), typeof(string) });
            MethodInfo installMethod = FindInstallIdentifierMethod(installerType, identifierType);

            if (constructor == null || installMethod == null)
            {
                Debug.LogError("[UnityStackInstaller] Could not find NuGetForUnity install API. Constructor found: " + (constructor != null) + ", method found: " + (installMethod != null));
                return;
            }

            try
            {
                for (int i = 0; i < NuGetPackages.Length; i++)
                {
                    NuGetSpec package = NuGetPackages[i];
                    string version = ResolveNuGetVersion(package);
                    object identifier = constructor.Invoke(new object[] { package.Id, version });

                    PropertyInfo isManuallyInstalled = identifierType.GetProperty("IsManuallyInstalled", BindingFlags.Instance | BindingFlags.Public);
                    if (isManuallyInstalled != null && isManuallyInstalled.CanWrite)
                    {
                        isManuallyInstalled.SetValue(identifier, true, null);
                    }

                    Debug.Log("[UnityStackInstaller] Installing NuGet package: " + package.Id + " " + version);
                    object result = installMethod.Invoke(null, BuildInstallIdentifierArguments(installMethod, identifier));
                    if (result is bool && !(bool)result)
                    {
                        throw new InvalidOperationException("NuGetForUnity returned false while installing " + package.Id + " " + version);
                    }
                }

                TryDisableAssemblyVersionValidation();

                WriteState(StageUnityExtensions);
                AssetDatabase.Refresh();
                EditorApplication.delayCall += RunInstaller;
            }
            catch (TargetInvocationException exception)
            {
                Exception inner = exception.InnerException ?? exception;
                Debug.LogError("[UnityStackInstaller] NuGet package installation failed: " + inner);
            }
            catch (Exception exception)
            {
                Debug.LogError("[UnityStackInstaller] NuGet package installation failed: " + exception);
            }
        }

        private static PackageSpec[] BuildUnityExtensionPackages()
        {
            PackageSpec[] packages = new PackageSpec[UnityExtensionPackages.Length];

            for (int i = 0; i < UnityExtensionPackages.Length; i++)
            {
                UnityExtensionPackageSpec package = UnityExtensionPackages[i];
                string version = ResolveUnityExtensionVersion(package.NuGetPackageId);

                packages[i] = new PackageSpec(package.Name, package.Url + "#" + version);
            }

            return packages;
        }

        private static string ResolveUnityExtensionVersion(string nuGetPackageId)
        {
            string version = ReadStateValue(nuGetPackageId);
            if (!string.IsNullOrEmpty(version))
            {
                return version;
            }

            version = GetInstalledNuGetVersion(nuGetPackageId);
            if (!string.IsNullOrEmpty(version))
            {
                WriteStateValue(nuGetPackageId, version);
                return version;
            }

            version = ResolveLatestStableNuGetVersion(nuGetPackageId);
            if (!string.IsNullOrEmpty(version))
            {
                WriteStateValue(nuGetPackageId, version);
                return version;
            }

            throw new InvalidOperationException("Could not resolve NuGet version for " + nuGetPackageId + ".");
        }

        private static string ResolveNuGetVersion(NuGetSpec package)
        {
            if (!string.IsNullOrEmpty(package.Version))
            {
                return package.Version;
            }

            string version = ResolveLatestStableNuGetVersion(package.Id);
            if (string.IsNullOrEmpty(version))
            {
                throw new InvalidOperationException("Could not resolve latest stable NuGet version for " + package.Id + ".");
            }

            return version;
        }

        private static string ResolveLatestStableNuGetVersion(string packageId)
        {
            string url = "https://api.nuget.org/v3-flatcontainer/" + packageId.ToLowerInvariant() + "/index.json";

            using (WebClient client = new WebClient())
            {
                string json = client.DownloadString(url);
                NuGetFlatContainerIndex index = JsonUtility.FromJson<NuGetFlatContainerIndex>(json);

                if (index == null || index.versions == null || index.versions.Length == 0)
                {
                    return null;
                }

                for (int i = index.versions.Length - 1; i >= 0; i--)
                {
                    string version = index.versions[i];
                    if (!string.IsNullOrEmpty(version) && version.IndexOf('-') < 0)
                    {
                        return version;
                    }
                }

                return index.versions[index.versions.Length - 1];
            }
        }

        private static string GetInstalledNuGetVersion(string packageId)
        {
            Type managerType = FindType("NugetForUnity.InstalledPackagesManager");
            if (managerType == null)
            {
                return null;
            }

            string version = GetInstalledNuGetVersionByTryGetById(managerType, packageId);
            if (!string.IsNullOrEmpty(version))
            {
                return version;
            }

            version = GetInstalledNuGetVersionByInstalledPackages(managerType, packageId);
            if (!string.IsNullOrEmpty(version))
            {
                return version;
            }

            return GetInstalledNuGetVersionByPackageConfig(managerType, packageId);
        }

        private static string GetInstalledNuGetVersionByTryGetById(Type managerType, string packageId)
        {
            MethodInfo[] methods = managerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            foreach (MethodInfo method in methods)
            {
                if (method.Name != "TryGetById")
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 2 || parameters[0].ParameterType != typeof(string) || !parameters[1].ParameterType.IsByRef)
                {
                    continue;
                }

                object[] arguments = { packageId, null };
                object result = method.Invoke(null, arguments);
                if (result is bool && (bool)result && arguments[1] != null)
                {
                    return GetVersionString(arguments[1]);
                }
            }

            return null;
        }

        private static string GetInstalledNuGetVersionByInstalledPackages(Type managerType, string packageId)
        {
            PropertyInfo property = managerType.GetProperty("InstalledPackages", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (property == null)
            {
                return null;
            }

            object value = property.GetValue(null, null);
            System.Collections.IEnumerable packages = value as System.Collections.IEnumerable;
            if (packages == null)
            {
                return null;
            }

            foreach (object package in packages)
            {
                string id = GetStringProperty(package, "Id");
                if (string.Equals(id, packageId, StringComparison.OrdinalIgnoreCase))
                {
                    return GetVersionString(package);
                }
            }

            return null;
        }

        private static string GetInstalledNuGetVersionByPackageConfig(Type managerType, string packageId)
        {
            MethodInfo method = managerType.GetMethod("GetPackageConfigurationById", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null)
            {
                return null;
            }

            object config = method.Invoke(null, new object[] { packageId });
            return config == null ? null : GetVersionString(config);
        }

        private static string GetVersionString(object package)
        {
            string version = GetStringProperty(package, "Version");
            if (!string.IsNullOrEmpty(version))
            {
                return version;
            }

            object packageVersion = GetPropertyValue(package, "PackageVersion");
            return packageVersion == null ? null : packageVersion.ToString();
        }

        private static string GetStringProperty(object target, string name)
        {
            object value = GetPropertyValue(target, name);
            return value as string;
        }

        private static object GetPropertyValue(object target, string name)
        {
            if (target == null)
            {
                return null;
            }

            PropertyInfo property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property == null ? null : property.GetValue(target, null);
        }

        private static MethodInfo FindInstallIdentifierMethod(Type installerType, Type identifierType)
        {
            MethodInfo[] methods = installerType.GetMethods(BindingFlags.Public | BindingFlags.Static);

            foreach (MethodInfo method in methods)
            {
                if (method.Name != "InstallIdentifier")
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length >= 1 && parameters[0].ParameterType.IsAssignableFrom(identifierType))
                {
                    return method;
                }
            }

            return null;
        }

        private static object[] BuildInstallIdentifierArguments(MethodInfo installMethod, object identifier)
        {
            ParameterInfo[] parameters = installMethod.GetParameters();
            object[] arguments = new object[parameters.Length];
            arguments[0] = identifier;

            for (int i = 1; i < parameters.Length; i++)
            {
                if (parameters[i].ParameterType == typeof(bool))
                {
                    // NuGetForUnity currently exposes:
                    // InstallIdentifier(package, refreshAssets = true, isSlimRestoreInstall = false, allowUpdateForExplicitlyInstalled = true)
                    if (parameters[i].Name == "refreshAssets")
                    {
                        arguments[i] = true;
                    }
                    else if (parameters[i].Name == "isSlimRestoreInstall")
                    {
                        arguments[i] = false;
                    }
                    else if (parameters[i].Name == "allowUpdateForExplicitlyInstalled")
                    {
                        arguments[i] = true;
                    }
                    else if (parameters[i].HasDefaultValue)
                    {
                        arguments[i] = parameters[i].DefaultValue;
                    }
                    else
                    {
                        arguments[i] = false;
                    }
                }
                else if (parameters[i].HasDefaultValue)
                {
                    arguments[i] = parameters[i].DefaultValue;
                }
                else
                {
                    arguments[i] = Type.Missing;
                }
            }

            return arguments;
        }

        private static Type FindType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (Assembly assembly in assemblies)
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static void TryDisableAssemblyVersionValidation()
        {
            try
            {
                PropertyInfo property = typeof(PlayerSettings).GetProperty(
                    "assemblyVersionValidation",
                    BindingFlags.Public | BindingFlags.Static);

                if (property != null && property.PropertyType == typeof(bool) && property.CanWrite)
                {
                    property.SetValue(null, false, null);
                    Debug.Log("[UnityStackInstaller] Disabled PlayerSettings assembly version validation.");
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[UnityStackInstaller] Could not disable assembly version validation automatically: " + exception.Message);
            }
        }

        private static bool IsPackageInManifest(string packageName)
        {
            string manifestPath = Path.Combine(Directory.GetCurrentDirectory(), "Packages", "manifest.json");

            if (!File.Exists(manifestPath))
            {
                return false;
            }

            string manifest = File.ReadAllText(manifestPath);
            return manifest.Contains("\"" + packageName + "\"");
        }

        private static void EnsureGitIgnore()
        {
            string gitIgnorePath = Path.Combine(Directory.GetCurrentDirectory(), ".gitignore");

            if (!File.Exists(gitIgnorePath))
            {
                File.WriteAllText(
                    gitIgnorePath,
                    "# Additional" + Environment.NewLine +
                    GitIgnoreLine + Environment.NewLine
                );
                return;
            }

            string text = File.ReadAllText(gitIgnorePath);

            if (text.Contains(GitIgnoreLine))
            {
                return;
            }

            string prefix = text.EndsWith("\n") || text.EndsWith("\r\n")
                ? string.Empty
                : Environment.NewLine;

            File.AppendAllText(
                gitIgnorePath,
                prefix +
                Environment.NewLine +
                "# Additional" + Environment.NewLine +
                GitIgnoreLine + Environment.NewLine
            );
        }

        private static string ReadState()
        {
            string state = ReadStateValue("stage");
            if (!string.IsNullOrEmpty(state))
            {
                return state;
            }

            if (!File.Exists(StateFilePath))
            {
                return string.Empty;
            }

            string text = File.ReadAllText(StateFilePath).Trim();
            return text.Contains("=") ? string.Empty : text;
        }

        private static bool IsUpdateMode()
        {
            return ReadStateValue("mode") == "update";
        }

        private static string ReadStateValue(string key)
        {
            if (!File.Exists(StateFilePath))
            {
                return null;
            }

            string[] lines = File.ReadAllLines(StateFilePath);
            foreach (string line in lines)
            {
                int separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                string currentKey = line.Substring(0, separator).Trim();
                if (string.Equals(currentKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    return line.Substring(separator + 1).Trim();
                }
            }

            return null;
         }

        private static void WriteState(string state)
        {
            WriteState(state, IsUpdateMode());
        }

        private static void WriteState(string state, bool updateMode)
        {
            string directory = Path.GetDirectoryName(StateFilePath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string text = "stage=" + state + Environment.NewLine;
            if (updateMode)
            {
                text += "mode=update" + Environment.NewLine;
            }

            foreach (NuGetSpec package in NuGetPackages)
            {
                string version = ReadStateValue(package.Id);
                if (!string.IsNullOrEmpty(version))
                {
                    text += package.Id + "=" + version + Environment.NewLine;
                }
            }

            string reactiveInputSystemVersion = ReadStateValue("com.nuskey8.reactive-input-system");
            if (!string.IsNullOrEmpty(reactiveInputSystemVersion))
            {
                text += "com.nuskey8.reactive-input-system=" + reactiveInputSystemVersion + Environment.NewLine;
            }

            File.WriteAllText(StateFilePath, text);
        }

        private static void WriteStateValue(string key, string value)
        {
            string state = ReadState();
            bool updateMode = IsUpdateMode();

            string directory = Path.GetDirectoryName(StateFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string text = "stage=" + state + Environment.NewLine;
            if (updateMode)
            {
                text += "mode=update" + Environment.NewLine;
            }

            foreach (NuGetSpec package in NuGetPackages)
            {
                string version = string.Equals(package.Id, key, StringComparison.OrdinalIgnoreCase)
                    ? value
                    : ReadStateValue(package.Id);

                if (!string.IsNullOrEmpty(version))
                {
                    text += package.Id + "=" + version + Environment.NewLine;
                }
            }

            string reactiveInputSystemVersion = string.Equals(key, "com.nuskey8.reactive-input-system", StringComparison.OrdinalIgnoreCase)
                ? value
                : ReadStateValue("com.nuskey8.reactive-input-system");
            if (!string.IsNullOrEmpty(reactiveInputSystemVersion))
            {
                text += "com.nuskey8.reactive-input-system=" + reactiveInputSystemVersion + Environment.NewLine;
            }

            File.WriteAllText(StateFilePath, text);
        }

        private static void ResetState()
        {
            if (File.Exists(DoneFilePath))
            {
                File.Delete(DoneFilePath);
            }

            if (File.Exists(StateFilePath))
            {
                File.Delete(StateFilePath);
            }
        }

        private static void MarkDone()
        {
            string directory = Path.GetDirectoryName(DoneFilePath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(DoneFilePath, DateTimeOffset.Now.ToString("O"));

            if (File.Exists(StateFilePath))
            {
                File.Delete(StateFilePath);
            }
        }
    }
}
#endif
