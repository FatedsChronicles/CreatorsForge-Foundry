using System.Reflection;
using System.Runtime.Loader;
using CreatorsForge.Foundry.NativeTestHost;
using CreatorsForge.Foundry.Testing;
using CreatorsForge.Foundry.Workspaces;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CreatorsForge.Foundry.PreviewHost;

internal sealed record ExecutablePreviewResult(
    string AdapterId,
    string DisplayName,
    IReadOnlyList<PreviewRuntimeElement> Elements,
    IReadOnlyList<string> Logs,
    string? ImagePngBase64);

internal static class ExecutablePreviewRenderer
{
    private const int MaximumImageBytes = 10 * 1024 * 1024;

    public static Task<ExecutablePreviewResult> RenderAsync(
        PreviewRuntimeRequest request,
        string runRoot,
        CancellationToken cancellationToken = default)
    {
        var execution = request.Execution ?? throw new InvalidOperationException("Executable preview input is missing.");
        return execution.Kind switch
        {
            PreviewRuntimeExecutionKinds.StaticWeb => RenderWebAsync(request, execution, runRoot, cancellationToken),
            PreviewRuntimeExecutionKinds.WinForms => RenderWinFormsAsync(request, execution, runRoot, cancellationToken),
            PreviewRuntimeExecutionKinds.ObsComponent => RenderObsAsync(request, execution, runRoot, cancellationToken),
            _ => throw new InvalidOperationException($"Executable preview kind '{execution.Kind}' is not supported."),
        };
    }

    private static Task<ExecutablePreviewResult> RenderWebAsync(
        PreviewRuntimeRequest request,
        PreviewRuntimeExecution execution,
        string runRoot,
        CancellationToken cancellationToken)
    {
        var entryPath = ResolveContainedFile(runRoot, execution.EntryPath);
        var contentRoot = Path.GetDirectoryName(entryPath)!;
        return RunStaAsync(async () =>
        {
            using var form = CreateOffscreenForm(request.Surface.ViewportWidth, request.Surface.ViewportHeight);
            using var webView = new WebView2 { Dock = DockStyle.Fill };
            form.Controls.Add(webView);
            form.Show();
            var profileRoot = Path.Combine(runRoot, "webview-profile");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profileRoot);
            var browserExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            environment.BrowserProcessExited += (_, _) => browserExited.TrySetResult();
            await webView.EnsureCoreWebView2Async(environment);
            var core = webView.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, args) =>
            {
                if (!IsAllowedLocalUri(args.Request.Uri, contentRoot))
                {
                    args.Response = core.Environment.CreateWebResourceResponse(
                        Stream.Null,
                        403,
                        "Blocked by Foundry executable preview",
                        "Content-Type: text/plain");
                }
            };
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.NavigationStarting += (_, args) =>
            {
                if (!IsAllowedLocalUri(args.Uri, contentRoot)) args.Cancel = true;
            };

            var navigation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            core.NavigationCompleted += (_, args) => navigation.TrySetResult(args.IsSuccess);
            core.Navigate(new Uri(entryPath).AbsoluteUri);
            if (!await navigation.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken) ||
                !await navigation.Task.ConfigureAwait(true))
            {
                throw new InvalidOperationException("The isolated browser could not load the staged document.");
            }
            await Task.Delay(300, cancellationToken).ConfigureAwait(true);
            var executedTitle = await core.ExecuteScriptAsync("document.title");
            await using var image = new MemoryStream();
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
            var bytes = image.ToArray();
            ValidateImage(bytes);
            form.Hide();
            webView.Dispose();
            form.Close();
            try
            {
                await browserExited.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
            }
            catch (TimeoutException)
            {
                throw new InvalidOperationException("The isolated browser did not release its disposable profile.");
            }
            return new ExecutablePreviewResult(
                "static-web-live-v1",
                "Static web - executable WebView2",
                [],
                [
                    "Executed staged HTML, CSS, and JavaScript in an isolated WebView2 profile.",
                    $"Live document title after script execution: {executedTitle}.",
                    "Blocked navigation, network requests, permissions, new windows, host objects, DevTools, and browser shortcuts.",
                ],
                Convert.ToBase64String(bytes));
        }, cancellationToken);
    }

    private static Task<ExecutablePreviewResult> RenderWinFormsAsync(
        PreviewRuntimeRequest request,
        PreviewRuntimeExecution execution,
        string runRoot,
        CancellationToken cancellationToken)
    {
        var artifactPath = ResolveContainedFile(runRoot, execution.ArtifactPath);
        return RunStaAsync(() =>
        {
            using var loadContext = new PreviewAssemblyLoadContext(artifactPath);
            var assembly = loadContext.LoadFromAssemblyPath(artifactPath);
            var formType = assembly.GetExportedTypes().FirstOrDefault(type =>
                !type.IsAbstract && typeof(Form).IsAssignableFrom(type));
            if (formType is null)
            {
                throw new InvalidOperationException("The built assembly exposes no public WinForms Form type.");
            }
            using var form = Activator.CreateInstance(formType) as Form ??
                throw new InvalidOperationException($"WinForms type '{formType.FullName}' could not be created.");
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new System.Drawing.Point(-32000, -32000);
            form.ShowInTaskbar = false;
            form.Width = Math.Clamp(request.Surface.ViewportWidth, 240, 3840);
            form.Height = Math.Clamp(request.Surface.ViewportHeight, 180, 2160);
            form.Show();
            System.Windows.Forms.Application.DoEvents();
            using var bitmap = new System.Drawing.Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height));
            using var image = new MemoryStream();
            bitmap.Save(image, System.Drawing.Imaging.ImageFormat.Png);
            var bytes = image.ToArray();
            ValidateImage(bytes);
            form.Hide();
            return Task.FromResult(new ExecutablePreviewResult(
                "winforms-live-v1",
                $"WinForms - executable {formType.Name}",
                [],
                [
                    $"Loaded the staged managed assembly and instantiated '{formType.FullName}' inside the isolated host.",
                    "Captured the live WinForms control tree to a bounded PNG; the assembly was never loaded by the Foundry desktop process.",
                ],
                Convert.ToBase64String(bytes)));
        }, cancellationToken);
    }

    private static async Task<ExecutablePreviewResult> RenderObsAsync(
        PreviewRuntimeRequest request,
        PreviewRuntimeExecution execution,
        string runRoot,
        CancellationToken cancellationToken)
    {
        var artifactPath = ResolveContainedFile(runRoot, execution.ArtifactPath);
        if (string.IsNullOrWhiteSpace(execution.ObsRoot) ||
            string.IsNullOrWhiteSpace(execution.ComponentId))
        {
            throw new InvalidOperationException("OBS executable preview requires a disposable OBS root and component ID.");
        }
        var nativeHost = Path.Combine(AppContext.BaseDirectory, "CreatorsForge.Foundry.NativeTestHost.dll");
        if (!File.Exists(nativeHost))
        {
            throw new InvalidOperationException("The isolated OBS native host is missing.");
        }
        var nativeResult = await ObsNativeProcessRunner.RunAsync(
            new ObsNativeHostRequest
            {
                PluginPath = artifactPath,
                ObsRoot = Path.GetFullPath(execution.ObsRoot),
                ExpectedSourceId = execution.ComponentId,
            },
            nativeHost,
            Path.Combine(runRoot, "obs-runtime"),
            TimeSpan.FromSeconds(6),
            cancellationToken).ConfigureAwait(false);
        if (!nativeResult.Completed || nativeResult.HostResult is not
            {
                ModuleLoadSucceeded: true,
                SourceCreated: true,
                SourceDestroyed: true,
            })
        {
            throw new InvalidOperationException(nativeResult.Failure ?? nativeResult.HostResult?.Error ??
                "The OBS module lifecycle did not complete.");
        }
        var structural = PreviewProviderAdapterRegistry.Render(request.Surface);
        var host = nativeResult.HostResult;
        var propertyX = Math.Max(180, request.Surface.ViewportWidth * 0.68) + 32;
        var propertyWidth = Math.Max(140, request.Surface.ViewportWidth - propertyX - 24);
        var liveElements = new List<PreviewRuntimeElement>
        {
            new("obs-live-status", "module-loaded", "Live module and source lifecycle passed", "success-badge", 24, 12, 360, 32),
        };
        for (var index = 0; index < host.Properties.Count && index < 16; index++)
        {
            var property = host.Properties[index];
            liveElements.Add(new(
                "obs-property",
                property.Name,
                string.IsNullOrWhiteSpace(property.Description) ? property.Name : property.Description,
                "input",
                propertyX,
                104 + index * 46,
                propertyWidth,
                38));
        }
        var elements = structural.Elements.Concat(liveElements).Take(48).ToArray();
        return new(
            "obs-component-live-v1",
            "OBS Studio - executable lifecycle",
            elements,
            [
                $"Loaded the staged plugin into libobs from '{Path.GetFileName(execution.ObsRoot)}'.",
                $"Registered, created, and destroyed source '{execution.ComponentId}' in the crash-isolated native host.",
                $"Executed the plugin properties callback and discovered {host.Properties.Count} live configuration controls.",
                "This component exposes no standalone pixel surface; Foundry retains the declared composition and overlays verified live lifecycle and property evidence.",
            ],
            null);
    }

    private static Form CreateOffscreenForm(int width, int height) => new()
    {
        Width = Math.Clamp(width, 240, 3840),
        Height = Math.Clamp(height, 180, 2160),
        StartPosition = FormStartPosition.Manual,
        Location = new System.Drawing.Point(-32000, -32000),
        ShowInTaskbar = false,
        FormBorderStyle = FormBorderStyle.None,
    };

    private static Task<T> RunStaAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                var context = new WindowsFormsSynchronizationContext();
                SynchronizationContext.SetSynchronizationContext(context);
                var task = action();
                while (!task.IsCompleted)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    System.Windows.Forms.Application.DoEvents();
                    Thread.Sleep(10);
                }
                completion.TrySetResult(task.GetAwaiter().GetResult());
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Foundry executable preview STA",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(cancellationToken);
    }

    private static bool IsAllowedLocalUri(string value, string contentRoot)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.IsFile) return false;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(contentRoot));
        var candidate = Path.GetFullPath(uri.LocalPath);
        return candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveContainedFile(string root, string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException("Executable preview paths must be isolated-host relative.");
        }
        var boundary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var candidate = Path.GetFullPath(Path.Combine(boundary, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(boundary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(candidate))
        {
            throw new InvalidOperationException("Executable preview file escaped the isolated run directory or is missing.");
        }
        return candidate;
    }

    private static void ValidateImage(byte[] bytes)
    {
        if (bytes.Length is < 8 or > MaximumImageBytes ||
            !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            throw new InvalidOperationException("Executable preview did not produce a valid bounded PNG frame.");
        }
    }

    private sealed class PreviewAssemblyLoadContext(string mainAssemblyPath) : AssemblyLoadContext(isCollectible: true), IDisposable
    {
        private readonly AssemblyDependencyResolver resolver = new(mainAssemblyPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var path = resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }

        public void Dispose() => Unload();
    }
}
