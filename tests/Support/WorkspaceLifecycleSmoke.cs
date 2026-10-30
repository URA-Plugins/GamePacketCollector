using UmamusumeResponseAnalyzer.Plugin;
using UmamusumeResponseAnalyzer.TerminalGui;

static class WorkspaceLifecycleSmoke
{
    internal static void Run(WorkspaceSmokeSession ui, Func<IPlugin> create, string name)
    {
        using var directory = new TempCurrentDirectory(name + "-unused-lifecycle");
        using var context = new RuntimePluginContext(ui.Application);
        ui.Bootstrap.SwitchTo();
        var plugin = create();
        try
        {
            plugin.Initialize(context);
            ui.Flush();
            if (!ReferenceEquals(Workspace.Current, ui.Bootstrap))
                throw new InvalidOperationException(name + " Initialize changed the active workspace.");
            ui.ShowBootstrapLogs();
            var screen = ui.CaptureScreen();
            if (!screen.Contains(name, StringComparison.Ordinal))
                throw new InvalidOperationException("Collector initialization status was not visible: " + screen);
        }
        finally { plugin.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        ui.Flush();
        if (!ReferenceEquals(Workspace.Current, ui.Bootstrap))
            throw new InvalidOperationException(name + " unused Dispose changed the active workspace.");
    }
}
