using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Seikasan.UnityStackInstaller.Editor
{
    internal static class UnityStackInstaller
    {
        private const string MenuRoot = "Tools/Unity Stack Installer/";
        private const string PackageName = "com.seikasan.unity-stack-installer";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        [MenuItem(MenuRoot + "Install")]
        private static void Install()
        {
            RunWriteOperation("Install");
        }

        [MenuItem(MenuRoot + "Verify")]
        private static void Verify()
        {
            try
            {
                var context = InstallerContext.Load();
                var report = new Report("Unity Stack Installer Verify");

                ManifestFile.Verify(context.ManifestPath, context.Stack, report);
                PackagesConfigFile.Verify(context.ProjectRoot, context.Stack, report);

                ShowReport(report);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("Unity Stack Installer", ex.Message, "OK");
            }
        }

        [MenuItem(MenuRoot + "Repair")]
        private static void Repair()
        {
            RunWriteOperation("Repair");
        }

        private static void RunWriteOperation(string title)
        {
            try
            {
                var context = InstallerContext.Load();
                var report = new Report("Unity Stack Installer " + title);

                var manifestChanged = ManifestFile.Ensure(context.ManifestPath, context.Stack, report);
                var packagesConfigChanged = PackagesConfigFile.Ensure(context.ProjectRoot, context.Stack, report);

                if (manifestChanged)
                {
                    Client.Resolve();
                    report.Info("Requested Unity Package Manager resolve.");
                }

                if (packagesConfigChanged)
                {
                    AssetDatabase.Refresh();
                    report.Info("Refreshed AssetDatabase for packages.config.");
                }

                if (!manifestChanged && !packagesConfigChanged)
                {
                    report.Info("No changes were required.");
                }

                ShowReport(report);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("Unity Stack Installer", ex.Message, "OK");
            }
        }

        private static void ShowReport(Report report)
        {
            var message = report.ToMessage();
            Debug.Log(message);
            EditorUtility.DisplayDialog("Unity Stack Installer", message, "OK");
        }

        private sealed class StackDefinition
        {
            public RegistryDefinition openUpmRegistry;
            public PackageDefinition[] upm;
            public string nugetPackagesConfigPath;
            public string nugetTargetFramework;
            public PackageDefinition[] nuget;
        }

        private sealed class RegistryDefinition
        {
            public string name;
            public string url;
            public string[] scopes;
        }

        private sealed class PackageDefinition
        {
            public string id;
            public string version;
        }

        private sealed class InstallerContext
        {
            private InstallerContext(string projectRoot, string manifestPath, StackDefinition stack)
            {
                ProjectRoot = projectRoot;
                ManifestPath = manifestPath;
                Stack = stack;
            }

            public string ProjectRoot { get; }
            public string ManifestPath { get; }
            public StackDefinition Stack { get; }

            public static InstallerContext Load()
            {
                var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
                var stackPath = ResolveStackPath(projectRoot);

                if (!File.Exists(stackPath))
                {
                    throw new FileNotFoundException("stack.json was not found.", stackPath);
                }

                var stack = ReadStackDefinition(stackPath);
                ValidateStack(stack, stackPath);
                return new InstallerContext(projectRoot, manifestPath, stack);
            }

            private static string ResolveStackPath(string projectRoot)
            {
                var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(Assembly.GetExecutingAssembly());
                var packageAssetPath = packageInfo != null && !string.IsNullOrEmpty(packageInfo.assetPath)
                    ? packageInfo.assetPath
                    : "Packages/" + PackageName;

                return Path.GetFullPath(Path.Combine(projectRoot, packageAssetPath, "stack.json"));
            }

            private static StackDefinition ReadStackDefinition(string stackPath)
            {
                var rootValue = JsonParser.Parse(File.ReadAllText(stackPath, Utf8NoBom));
                if (!(rootValue is JsonObject root))
                {
                    throw new InvalidDataException("stack.json must be a JSON object: " + stackPath);
                }

                var registryObject = root.GetObject("openUpmRegistry");
                if (registryObject == null)
                {
                    throw new InvalidDataException("stack.json must define openUpmRegistry.");
                }

                return new StackDefinition
                {
                    openUpmRegistry = new RegistryDefinition
                    {
                        name = RequiredString(registryObject, "name"),
                        url = RequiredString(registryObject, "url"),
                        scopes = RequiredStringArray(registryObject, "scopes")
                    },
                    upm = RequiredPackages(root, "upm"),
                    nugetPackagesConfigPath = OptionalString(root, "nugetPackagesConfigPath"),
                    nugetTargetFramework = OptionalString(root, "nugetTargetFramework"),
                    nuget = RequiredPackages(root, "nuget")
                };
            }

            private static PackageDefinition[] RequiredPackages(JsonObject root, string fieldName)
            {
                var array = root.GetArray(fieldName);
                if (array == null)
                {
                    throw new InvalidDataException("stack.json must define " + fieldName + ".");
                }

                var packages = new List<PackageDefinition>();
                foreach (var item in array.Items)
                {
                    if (!(item is JsonObject packageObject))
                    {
                        throw new InvalidDataException("Every " + fieldName + " item must be an object.");
                    }

                    packages.Add(new PackageDefinition
                    {
                        id = RequiredString(packageObject, "id"),
                        version = RequiredString(packageObject, "version")
                    });
                }

                return packages.ToArray();
            }

            private static string[] RequiredStringArray(JsonObject root, string fieldName)
            {
                var array = root.GetArray(fieldName);
                if (array == null || array.Items.Count == 0)
                {
                    throw new InvalidDataException("stack.json must define " + fieldName + ".");
                }

                var values = new List<string>();
                foreach (var item in array.Items)
                {
                    if (!(item is JsonString value) || string.IsNullOrEmpty(value.Value))
                    {
                        throw new InvalidDataException("Every " + fieldName + " item must be a non-empty string.");
                    }

                    values.Add(value.Value);
                }

                return values.ToArray();
            }

            private static string RequiredString(JsonObject root, string fieldName)
            {
                var value = root.Get(fieldName);
                if (value is JsonString stringValue && !string.IsNullOrEmpty(stringValue.Value))
                {
                    return stringValue.Value;
                }

                throw new InvalidDataException("stack.json must define " + fieldName + " as a non-empty string.");
            }

            private static string OptionalString(JsonObject root, string fieldName)
            {
                var value = root.Get(fieldName);
                if (value == null)
                {
                    return null;
                }

                if (value is JsonString stringValue)
                {
                    return stringValue.Value;
                }

                throw new InvalidDataException("stack.json must define " + fieldName + " as a string.");
            }

            private static void ValidateStack(StackDefinition stack, string stackPath)
            {
                if (stack == null)
                {
                    throw new InvalidDataException("stack.json could not be parsed: " + stackPath);
                }

                if (stack.openUpmRegistry == null ||
                    string.IsNullOrEmpty(stack.openUpmRegistry.name) ||
                    string.IsNullOrEmpty(stack.openUpmRegistry.url) ||
                    stack.openUpmRegistry.scopes == null ||
                    stack.openUpmRegistry.scopes.Length == 0)
                {
                    throw new InvalidDataException("stack.json must define openUpmRegistry with name, url, and scopes.");
                }

                ValidatePackages(stack.upm, "upm");
                ValidatePackages(stack.nuget, "nuget");

                if (string.IsNullOrEmpty(stack.nugetPackagesConfigPath))
                {
                    stack.nugetPackagesConfigPath = "Assets/packages.config";
                }

                if (string.IsNullOrEmpty(stack.nugetTargetFramework))
                {
                    stack.nugetTargetFramework = "netstandard2.1";
                }
            }

            private static void ValidatePackages(PackageDefinition[] packages, string fieldName)
            {
                if (packages == null)
                {
                    throw new InvalidDataException("stack.json must define " + fieldName + ".");
                }

                foreach (var package in packages)
                {
                    if (package == null || string.IsNullOrEmpty(package.id) || string.IsNullOrEmpty(package.version))
                    {
                        throw new InvalidDataException("Every " + fieldName + " package must define id and version.");
                    }
                }
            }
        }

        private static class ManifestFile
        {
            public static bool Ensure(string manifestPath, StackDefinition stack, Report report)
            {
                var root = ReadRoot(manifestPath);
                var changed = EnsureOpenUpmRegistry(root, stack.openUpmRegistry, report);
                changed |= EnsureUpmDependencies(root, stack.upm, report);

                if (!changed)
                {
                    return false;
                }

                var directory = Path.GetDirectoryName(manifestPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(manifestPath, root.ToJson() + Environment.NewLine, Utf8NoBom);
                return true;
            }

            public static void Verify(string manifestPath, StackDefinition stack, Report report)
            {
                if (!File.Exists(manifestPath))
                {
                    report.Missing("Packages/manifest.json");
                    return;
                }

                var root = ReadRoot(manifestPath);
                VerifyOpenUpmRegistry(root, stack.openUpmRegistry, report);
                VerifyUpmDependencies(root, stack.upm, report);
            }

            private static JsonObject ReadRoot(string manifestPath)
            {
                if (!File.Exists(manifestPath))
                {
                    return new JsonObject();
                }

                var json = File.ReadAllText(manifestPath, Utf8NoBom);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new JsonObject();
                }

                var value = JsonParser.Parse(json);
                if (value is JsonObject root)
                {
                    return root;
                }

                throw new InvalidDataException("Packages/manifest.json must be a JSON object.");
            }

            private static bool EnsureOpenUpmRegistry(JsonObject root, RegistryDefinition registry, Report report)
            {
                var changed = false;
                var registries = root.GetArray("scopedRegistries");
                if (registries == null)
                {
                    registries = new JsonArray();
                    root.Set("scopedRegistries", registries);
                    changed = true;
                }

                var registryObject = registries.Items.OfType<JsonObject>()
                    .FirstOrDefault(item => string.Equals(item.GetString("url"), registry.url, StringComparison.Ordinal));

                if (registryObject == null)
                {
                    registryObject = new JsonObject();
                    registryObject.Set("name", new JsonString(registry.name));
                    registryObject.Set("url", new JsonString(registry.url));
                    registryObject.Set("scopes", ToJsonArray(registry.scopes));
                    registries.Items.Add(registryObject);
                    report.Added("OpenUPM scoped registry: " + registry.url);
                    return true;
                }

                var scopes = registryObject.GetArray("scopes");
                if (scopes == null)
                {
                    scopes = new JsonArray();
                    registryObject.Set("scopes", scopes);
                    changed = true;
                }

                var existingScopes = new HashSet<string>(
                    scopes.Items.OfType<JsonString>().Select(item => item.Value),
                    StringComparer.Ordinal);

                foreach (var scope in registry.scopes)
                {
                    if (existingScopes.Contains(scope))
                    {
                        report.Ok("OpenUPM scope already present: " + scope);
                        continue;
                    }

                    scopes.Items.Add(new JsonString(scope));
                    existingScopes.Add(scope);
                    changed = true;
                    report.Added("OpenUPM scope: " + scope);
                }

                return changed;
            }

            private static void VerifyOpenUpmRegistry(JsonObject root, RegistryDefinition registry, Report report)
            {
                var registries = root.GetArray("scopedRegistries");
                var registryObject = registries?.Items.OfType<JsonObject>()
                    .FirstOrDefault(item => string.Equals(item.GetString("url"), registry.url, StringComparison.Ordinal));

                if (registryObject == null)
                {
                    report.Missing("OpenUPM scoped registry: " + registry.url);
                    return;
                }

                report.Ok("OpenUPM scoped registry: " + registry.url);

                var scopes = registryObject.GetArray("scopes");
                var existingScopes = new HashSet<string>(
                    scopes?.Items.OfType<JsonString>().Select(item => item.Value) ?? Enumerable.Empty<string>(),
                    StringComparer.Ordinal);

                foreach (var scope in registry.scopes)
                {
                    if (existingScopes.Contains(scope))
                    {
                        report.Ok("OpenUPM scope: " + scope);
                    }
                    else
                    {
                        report.Missing("OpenUPM scope: " + scope);
                    }
                }
            }

            private static bool EnsureUpmDependencies(JsonObject root, PackageDefinition[] packages, Report report)
            {
                var changed = false;
                var dependencies = root.GetObject("dependencies");
                if (dependencies == null)
                {
                    dependencies = new JsonObject();
                    root.Set("dependencies", dependencies);
                    changed = true;
                }

                foreach (var package in packages)
                {
                    var value = dependencies.Get(package.id);
                    if (value == null)
                    {
                        dependencies.Set(package.id, new JsonString(package.version));
                        report.Added("UPM package: " + package.id + "@" + package.version);
                        changed = true;
                        continue;
                    }

                    if (value is JsonString version && version.Value == package.version)
                    {
                        report.Ok("UPM package already present: " + package.id + "@" + package.version);
                    }
                    else if (value is JsonString existingVersion)
                    {
                        report.VersionConflict("UPM package kept: " + package.id + "@" + existingVersion.Value + " (stack.json requires " + package.version + ")");
                    }
                    else
                    {
                        report.Warning("UPM package kept because dependency value is not a string: " + package.id);
                    }
                }

                return changed;
            }

            private static void VerifyUpmDependencies(JsonObject root, PackageDefinition[] packages, Report report)
            {
                var dependencies = root.GetObject("dependencies");
                if (dependencies == null)
                {
                    foreach (var package in packages)
                    {
                        report.Missing("UPM package: " + package.id + "@" + package.version);
                    }

                    return;
                }

                foreach (var package in packages)
                {
                    var value = dependencies.Get(package.id);
                    if (value == null)
                    {
                        report.Missing("UPM package: " + package.id + "@" + package.version);
                    }
                    else if (value is JsonString version && version.Value == package.version)
                    {
                        report.Ok("UPM package: " + package.id + "@" + package.version);
                    }
                    else if (value is JsonString existingVersion)
                    {
                        report.VersionConflict("UPM package: " + package.id + "@" + existingVersion.Value + " (stack.json requires " + package.version + ")");
                    }
                    else
                    {
                        report.Warning("UPM package has a non-string dependency value: " + package.id);
                    }
                }
            }

            private static JsonArray ToJsonArray(IEnumerable<string> values)
            {
                var array = new JsonArray();
                foreach (var value in values)
                {
                    array.Items.Add(new JsonString(value));
                }

                return array;
            }
        }

        private static class PackagesConfigFile
        {
            public static bool Ensure(string projectRoot, StackDefinition stack, Report report)
            {
                var packagesConfigPath = ResolveProjectPath(projectRoot, stack.nugetPackagesConfigPath);
                var document = ReadOrCreate(packagesConfigPath);
                var root = document.Root;
                if (root == null || root.Name.LocalName != "packages")
                {
                    throw new InvalidDataException(stack.nugetPackagesConfigPath + " must have a <packages> root element.");
                }

                var changed = false;
                foreach (var package in stack.nuget)
                {
                    var element = FindPackageElement(root, package.id);
                    if (element == null)
                    {
                        root.Add(new XElement(
                            "package",
                            new XAttribute("id", package.id),
                            new XAttribute("version", package.version),
                            new XAttribute("targetFramework", stack.nugetTargetFramework)));
                        report.Added("NuGet package: " + package.id + " " + package.version);
                        changed = true;
                        continue;
                    }

                    var version = (string)element.Attribute("version");
                    if (version == package.version)
                    {
                        report.Ok("NuGet package already present: " + package.id + " " + package.version);
                    }
                    else
                    {
                        report.VersionConflict("NuGet package kept: " + package.id + " " + (version ?? "(no version)") + " (stack.json requires " + package.version + ")");
                    }
                }

                if (!changed)
                {
                    return false;
                }

                var directory = Path.GetDirectoryName(packagesConfigPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                document.Save(packagesConfigPath);
                return true;
            }

            public static void Verify(string projectRoot, StackDefinition stack, Report report)
            {
                var packagesConfigPath = ResolveProjectPath(projectRoot, stack.nugetPackagesConfigPath);
                if (!File.Exists(packagesConfigPath))
                {
                    report.Missing(stack.nugetPackagesConfigPath);
                    foreach (var package in stack.nuget)
                    {
                        report.Missing("NuGet package: " + package.id + " " + package.version);
                    }

                    return;
                }

                var document = XDocument.Load(packagesConfigPath);
                var root = document.Root;
                if (root == null || root.Name.LocalName != "packages")
                {
                    report.Warning(stack.nugetPackagesConfigPath + " does not have a <packages> root element.");
                    return;
                }

                foreach (var package in stack.nuget)
                {
                    var element = FindPackageElement(root, package.id);
                    if (element == null)
                    {
                        report.Missing("NuGet package: " + package.id + " " + package.version);
                        continue;
                    }

                    var version = (string)element.Attribute("version");
                    if (version == package.version)
                    {
                        report.Ok("NuGet package: " + package.id + " " + package.version);
                    }
                    else
                    {
                        report.VersionConflict("NuGet package: " + package.id + " " + (version ?? "(no version)") + " (stack.json requires " + package.version + ")");
                    }
                }
            }

            private static XDocument ReadOrCreate(string packagesConfigPath)
            {
                if (File.Exists(packagesConfigPath))
                {
                    return XDocument.Load(packagesConfigPath, LoadOptions.PreserveWhitespace);
                }

                return new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement("packages"));
            }

            private static XElement FindPackageElement(XElement root, string packageId)
            {
                return root.Elements()
                    .FirstOrDefault(element =>
                        element.Name.LocalName == "package" &&
                        string.Equals((string)element.Attribute("id"), packageId, StringComparison.OrdinalIgnoreCase));
            }

            private static string ResolveProjectPath(string projectRoot, string projectRelativePath)
            {
                var fullPath = Path.GetFullPath(Path.Combine(projectRoot, projectRelativePath));
                var normalizedProjectRoot = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!fullPath.StartsWith(normalizedProjectRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(projectRelativePath + " points outside the Unity project.");
                }

                return fullPath;
            }
        }

        private sealed class Report
        {
            private readonly string title;
            private readonly List<string> added = new List<string>();
            private readonly List<string> missing = new List<string>();
            private readonly List<string> conflicts = new List<string>();
            private readonly List<string> warnings = new List<string>();
            private readonly List<string> ok = new List<string>();
            private readonly List<string> info = new List<string>();

            public Report(string title)
            {
                this.title = title;
            }

            public void Added(string message)
            {
                added.Add(message);
            }

            public void Missing(string message)
            {
                missing.Add(message);
            }

            public void VersionConflict(string message)
            {
                conflicts.Add(message);
            }

            public void Warning(string message)
            {
                warnings.Add(message);
            }

            public void Ok(string message)
            {
                ok.Add(message);
            }

            public void Info(string message)
            {
                info.Add(message);
            }

            public string ToMessage()
            {
                var builder = new StringBuilder();
                builder.AppendLine(title);
                AppendSection(builder, "Added", added);
                AppendSection(builder, "Missing", missing);
                AppendSection(builder, "Existing Version Kept", conflicts);
                AppendSection(builder, "Warnings", warnings);
                AppendSection(builder, "OK", ok);
                AppendSection(builder, "Info", info);
                return builder.ToString().TrimEnd();
            }

            private static void AppendSection(StringBuilder builder, string heading, List<string> values)
            {
                if (values.Count == 0)
                {
                    return;
                }

                builder.AppendLine();
                builder.AppendLine(heading + ":");
                foreach (var value in values)
                {
                    builder.AppendLine("- " + value);
                }
            }
        }

        private abstract class JsonValue
        {
            public abstract void Write(StringBuilder builder, int indent);

            public string ToJson()
            {
                var builder = new StringBuilder();
                Write(builder, 0);
                return builder.ToString();
            }

            protected static void Indent(StringBuilder builder, int indent)
            {
                builder.Append(' ', indent * 2);
            }

            protected static string Escape(string value)
            {
                if (value == null)
                {
                    return string.Empty;
                }

                var builder = new StringBuilder(value.Length + 8);
                foreach (var c in value)
                {
                    switch (c)
                    {
                        case '"':
                            builder.Append("\\\"");
                            break;
                        case '\\':
                            builder.Append("\\\\");
                            break;
                        case '\b':
                            builder.Append("\\b");
                            break;
                        case '\f':
                            builder.Append("\\f");
                            break;
                        case '\n':
                            builder.Append("\\n");
                            break;
                        case '\r':
                            builder.Append("\\r");
                            break;
                        case '\t':
                            builder.Append("\\t");
                            break;
                        default:
                            if (c < ' ')
                            {
                                builder.Append("\\u");
                                builder.Append(((int)c).ToString("x4"));
                            }
                            else
                            {
                                builder.Append(c);
                            }

                            break;
                    }
                }

                return builder.ToString();
            }
        }

        private sealed class JsonObject : JsonValue
        {
            private readonly List<KeyValuePair<string, JsonValue>> properties = new List<KeyValuePair<string, JsonValue>>();

            public JsonValue Get(string key)
            {
                for (var i = 0; i < properties.Count; i++)
                {
                    if (properties[i].Key == key)
                    {
                        return properties[i].Value;
                    }
                }

                return null;
            }

            public JsonObject GetObject(string key)
            {
                var value = Get(key);
                if (value == null)
                {
                    return null;
                }

                if (value is JsonObject jsonObject)
                {
                    return jsonObject;
                }

                throw new InvalidDataException(key + " must be a JSON object.");
            }

            public JsonArray GetArray(string key)
            {
                var value = Get(key);
                if (value == null)
                {
                    return null;
                }

                if (value is JsonArray jsonArray)
                {
                    return jsonArray;
                }

                throw new InvalidDataException(key + " must be a JSON array.");
            }

            public string GetString(string key)
            {
                return Get(key) is JsonString value ? value.Value : null;
            }

            public void Set(string key, JsonValue value)
            {
                for (var i = 0; i < properties.Count; i++)
                {
                    if (properties[i].Key == key)
                    {
                        properties[i] = new KeyValuePair<string, JsonValue>(key, value);
                        return;
                    }
                }

                properties.Add(new KeyValuePair<string, JsonValue>(key, value));
            }

            public override void Write(StringBuilder builder, int indent)
            {
                builder.Append('{');
                if (properties.Count == 0)
                {
                    builder.Append('}');
                    return;
                }

                builder.AppendLine();
                for (var i = 0; i < properties.Count; i++)
                {
                    Indent(builder, indent + 1);
                    builder.Append('"');
                    builder.Append(Escape(properties[i].Key));
                    builder.Append("\": ");
                    properties[i].Value.Write(builder, indent + 1);
                    if (i < properties.Count - 1)
                    {
                        builder.Append(',');
                    }

                    builder.AppendLine();
                }

                Indent(builder, indent);
                builder.Append('}');
            }
        }

        private sealed class JsonArray : JsonValue
        {
            public readonly List<JsonValue> Items = new List<JsonValue>();

            public override void Write(StringBuilder builder, int indent)
            {
                builder.Append('[');
                if (Items.Count == 0)
                {
                    builder.Append(']');
                    return;
                }

                builder.AppendLine();
                for (var i = 0; i < Items.Count; i++)
                {
                    Indent(builder, indent + 1);
                    Items[i].Write(builder, indent + 1);
                    if (i < Items.Count - 1)
                    {
                        builder.Append(',');
                    }

                    builder.AppendLine();
                }

                Indent(builder, indent);
                builder.Append(']');
            }
        }

        private sealed class JsonString : JsonValue
        {
            public JsonString(string value)
            {
                Value = value;
            }

            public string Value { get; }

            public override void Write(StringBuilder builder, int indent)
            {
                builder.Append('"');
                builder.Append(Escape(Value));
                builder.Append('"');
            }
        }

        private sealed class JsonLiteral : JsonValue
        {
            private readonly string value;

            public JsonLiteral(string value)
            {
                this.value = value;
            }

            public override void Write(StringBuilder builder, int indent)
            {
                builder.Append(value);
            }
        }

        private sealed class JsonParser
        {
            private readonly string json;
            private int index;

            private JsonParser(string json)
            {
                this.json = json;
            }

            public static JsonValue Parse(string json)
            {
                var parser = new JsonParser(json);
                var value = parser.ParseValue();
                parser.SkipWhitespace();
                if (!parser.IsEnd)
                {
                    throw new InvalidDataException("Unexpected JSON content at position " + parser.index + ".");
                }

                return value;
            }

            private bool IsEnd => index >= json.Length;

            private JsonValue ParseValue()
            {
                SkipWhitespace();
                if (IsEnd)
                {
                    throw new InvalidDataException("Unexpected end of JSON.");
                }

                var c = json[index];
                if (c == '{')
                {
                    return ParseObject();
                }

                if (c == '[')
                {
                    return ParseArray();
                }

                if (c == '"')
                {
                    return new JsonString(ParseString());
                }

                if (c == '-' || char.IsDigit(c))
                {
                    return new JsonLiteral(ParseNumber());
                }

                if (TryConsume("true"))
                {
                    return new JsonLiteral("true");
                }

                if (TryConsume("false"))
                {
                    return new JsonLiteral("false");
                }

                if (TryConsume("null"))
                {
                    return new JsonLiteral("null");
                }

                throw new InvalidDataException("Unexpected JSON token at position " + index + ".");
            }

            private JsonObject ParseObject()
            {
                Expect('{');
                var value = new JsonObject();
                SkipWhitespace();
                if (TryConsume('}'))
                {
                    return value;
                }

                while (true)
                {
                    SkipWhitespace();
                    var key = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    value.Set(key, ParseValue());
                    SkipWhitespace();

                    if (TryConsume('}'))
                    {
                        return value;
                    }

                    Expect(',');
                }
            }

            private JsonArray ParseArray()
            {
                Expect('[');
                var value = new JsonArray();
                SkipWhitespace();
                if (TryConsume(']'))
                {
                    return value;
                }

                while (true)
                {
                    value.Items.Add(ParseValue());
                    SkipWhitespace();

                    if (TryConsume(']'))
                    {
                        return value;
                    }

                    Expect(',');
                }
            }

            private string ParseString()
            {
                Expect('"');
                var builder = new StringBuilder();
                while (!IsEnd)
                {
                    var c = json[index++];
                    if (c == '"')
                    {
                        return builder.ToString();
                    }

                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }

                    if (IsEnd)
                    {
                        throw new InvalidDataException("Unexpected end of JSON string escape.");
                    }

                    var escaped = json[index++];
                    switch (escaped)
                    {
                        case '"':
                        case '\\':
                        case '/':
                            builder.Append(escaped);
                            break;
                        case 'b':
                            builder.Append('\b');
                            break;
                        case 'f':
                            builder.Append('\f');
                            break;
                        case 'n':
                            builder.Append('\n');
                            break;
                        case 'r':
                            builder.Append('\r');
                            break;
                        case 't':
                            builder.Append('\t');
                            break;
                        case 'u':
                            builder.Append(ParseUnicodeEscape());
                            break;
                        default:
                            throw new InvalidDataException("Invalid JSON string escape at position " + index + ".");
                    }
                }

                throw new InvalidDataException("Unterminated JSON string.");
            }

            private char ParseUnicodeEscape()
            {
                if (index + 4 > json.Length)
                {
                    throw new InvalidDataException("Invalid JSON unicode escape at position " + index + ".");
                }

                var value = 0;
                for (var i = 0; i < 4; i++)
                {
                    var c = json[index++];
                    value <<= 4;
                    if (c >= '0' && c <= '9')
                    {
                        value += c - '0';
                    }
                    else if (c >= 'a' && c <= 'f')
                    {
                        value += c - 'a' + 10;
                    }
                    else if (c >= 'A' && c <= 'F')
                    {
                        value += c - 'A' + 10;
                    }
                    else
                    {
                        throw new InvalidDataException("Invalid JSON unicode escape at position " + index + ".");
                    }
                }

                return (char)value;
            }

            private string ParseNumber()
            {
                var start = index;
                TryConsume('-');
                ReadDigits();

                if (TryConsume('.'))
                {
                    ReadDigits();
                }

                if (!IsEnd && (json[index] == 'e' || json[index] == 'E'))
                {
                    index++;
                    if (!IsEnd && (json[index] == '+' || json[index] == '-'))
                    {
                        index++;
                    }

                    ReadDigits();
                }

                return json.Substring(start, index - start);
            }

            private void ReadDigits()
            {
                if (IsEnd || !char.IsDigit(json[index]))
                {
                    throw new InvalidDataException("Expected JSON number digit at position " + index + ".");
                }

                while (!IsEnd && char.IsDigit(json[index]))
                {
                    index++;
                }
            }

            private void SkipWhitespace()
            {
                while (!IsEnd && char.IsWhiteSpace(json[index]))
                {
                    index++;
                }
            }

            private bool TryConsume(char c)
            {
                if (!IsEnd && json[index] == c)
                {
                    index++;
                    return true;
                }

                return false;
            }

            private bool TryConsume(string value)
            {
                if (index + value.Length > json.Length)
                {
                    return false;
                }

                for (var i = 0; i < value.Length; i++)
                {
                    if (json[index + i] != value[i])
                    {
                        return false;
                    }
                }

                index += value.Length;
                return true;
            }

            private void Expect(char c)
            {
                if (!TryConsume(c))
                {
                    throw new InvalidDataException("Expected '" + c + "' at position " + index + ".");
                }
            }
        }
    }
}
