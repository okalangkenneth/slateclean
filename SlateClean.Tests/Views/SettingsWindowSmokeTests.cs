using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using SlateClean.App.Views;

namespace SlateClean.Tests.Views;

public class SettingsWindowSmokeTests
{
    [StaFact]
    public void SettingsWindow_renders_without_binding_errors()
    {
        var errors = new List<string>();
        var listener = new BindingErrorTraceListener(errors);

        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level =
            SourceLevels.Warning | SourceLevels.Error;

        Window? window = null;
        try
        {
            window = new SettingsWindow
            {
                DataContext = new SettingsWindowStubViewModel(),
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
                ShowInTaskbar = false,
            };
            window.Show();

            DispatcherFrame frame = new();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            window?.Close();
            PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
        }

        Assert.True(
            errors.Count == 0,
            "WPF binding errors detected:\n" + string.Join("\n", errors));
    }

    private sealed class BindingErrorTraceListener : TraceListener
    {
        private readonly List<string> _errors;
        private readonly System.Text.StringBuilder _pending = new();

        public BindingErrorTraceListener(List<string> errors) => _errors = errors;

        public override void Write(string? message) => _pending.Append(message);

        public override void WriteLine(string? message)
        {
            _pending.Append(message);
            _errors.Add(_pending.ToString());
            _pending.Clear();
        }
    }
}
