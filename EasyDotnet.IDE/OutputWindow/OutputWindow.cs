using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using EasyDotnet.Debugger;
using EasyDotnet.IDE.Commands;
using StreamJsonRpc;

namespace EasyDotnet.IDE.OutputWindow;

public static class AvaloniaOutputWindow
{
  private static SelectableTextBlock? s_textBlock;
  private static ScrollViewer? s_scrollViewer;

  private static JsonRpc? s_jsonRpc;
  private static bool s_isConnected;

  public static void Run(string pipeName) =>
    // Start Avalonia FIRST so the window appears
    BuildAvaloniaApp(pipeName).StartWithClassicDesktopLifetime([]);

  private static async Task ConnectToPipeAsync(string pipeName)
  {
    NamedPipeClientStream? pipeClient = null;

    try
    {
      SetText("⠋ Connecting to debugger...");

      await Task.Delay(100); // Let UI update

      pipeClient = new NamedPipeClientStream(
          ".",
          pipeName,
          PipeDirection.InOut,
          PipeOptions.Asynchronous);

      var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

      try
      {
        await pipeClient.ConnectAsync(cts.Token);
      }
      catch (OperationCanceledException)
      {
        throw new TimeoutException("Connection timed out after 10 seconds");
      }

      if (!pipeClient.IsConnected)
      {
        throw new InvalidOperationException("Pipe connected but IsConnected is false");
      }

      SetText("✓ Connected to debugger");
      await Task.Delay(500); // Show success message briefly
      SetText(""); // Clear for output
      s_isConnected = true;

      s_jsonRpc = ServerBuilder.Build(pipeClient, pipeClient);

      s_jsonRpc.AddLocalRpcMethod("debugger/output",
          (DebugOutputEvent outputEvent) => FormatAndDisplayOutput(outputEvent));

      s_jsonRpc.Disconnected += (sender, args) =>
      {
        s_isConnected = false;
        AppendOutput("\n[Debugger disconnected]\n");
      };

      s_jsonRpc.StartListening();

      await s_jsonRpc.Completion;
    }
    catch (TimeoutException)
    {
      SetText("✗ Connection timeout\nThe debugger may not be running.");
    }
    catch (PlatformNotSupportedException ex)
    {
      SetText($"✗ Platform not supported\n{ex.Message}");
    }
    catch (UnauthorizedAccessException)
    {
      SetText("✗ Access denied\nCheck if another process is using this pipe.");
    }
    catch (IOException)
    {
      SetText("✗ Connection failed\nCheck if the debugger is running.");
    }
    catch (Exception ex)
    {
      SetText($"✗ Error: {ex.Message}");
    }
    finally
    {
      try
      {
        pipeClient?.Dispose();
      }
      catch
      {
        // Ignore disposal errors
      }
    }
  }

  private static void FormatAndDisplayOutput(DebugOutputEvent outputEvent)
  {
    try
    {
      if (outputEvent.Source != null)
      {
        return;
      }

      foreach (var line in outputEvent.Output)
      {
        if (string.IsNullOrWhiteSpace(line))
        {
          AppendOutput("\n");
          continue;
        }

        AppendOutput($"{line}\n");
      }
    }
    catch (Exception ex)
    {
      AppendOutput($"[ERROR formatting output: {ex.Message}]\n");
    }
  }

  private static void SetText(string text) => Dispatcher.UIThread.Post(() =>
    {
      if (s_textBlock != null)
      {
        s_textBlock.Inlines = ParseTextWithLinks(text);
        s_scrollViewer?.ScrollToEnd();
      }
    });

  private static void AppendOutput(string text) => Dispatcher.UIThread.Post(() =>
                                                    {
                                                      if (s_textBlock != null && s_isConnected)
                                                      {
                                                        foreach (var inline in ParseTextWithLinks(text))
                                                        {
                                                          s_textBlock.Inlines.Add(inline);
                                                        }
                                                        s_scrollViewer?.ScrollToEnd();
                                                      }
                                                    });

  private static Avalonia.Controls.Documents.InlineCollection ParseTextWithLinks(string text)
  {
    var inlines = new Avalonia.Controls.Documents.InlineCollection();
    var urlPattern = @"(https?://[^\s]+)";
    foreach (var part in Regex.Split(text, urlPattern))
    {
      if (Regex.IsMatch(part, urlPattern))
      {
        var button = new Button
        {
          Content = part,
          Background = Brushes.Transparent,
          BorderThickness = new Thickness(0),
          Padding = new Thickness(0),
          Margin = new Thickness(0, -4, 0, -4), // Align better
          FontFamily = new FontFamily("JetBrains Mono,Cascadia Code,Fira Code,Consolas,monospace"),
          FontSize = 28,
          Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250)),
          Cursor = new Cursor(StandardCursorType.Hand)
        };

        var url = part;
        button.Click += (_, _) => OpenUrl(url);

        inlines.Add(new Avalonia.Controls.Documents.InlineUIContainer { Child = button });
      }
      else
      {
        inlines.Add(new Avalonia.Controls.Documents.Run(part));
      }
    }
    return inlines;
  }

  private static AppBuilder BuildAvaloniaApp(string pipeName) =>
      AppBuilder.Configure<App>()
          .UsePlatformDetect()
          .LogToTrace()
          .AfterSetup(_ =>
          {
            var window = CreateMainWindow(pipeName);

            window.KeyDown += (sender, e) =>
            {
              if (e.Key == Key.F11)
              {
                window.WindowState = window.WindowState == WindowState.FullScreen
        ? WindowState.Normal
        : WindowState.FullScreen;
              }
            };

            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
              desktop.MainWindow = window;

              // Keep window open even if connection fails
              desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            }
          });

  private static Window CreateMainWindow(string pipeName)
  {
    s_textBlock = new SelectableTextBlock
    {
      FontFamily = new FontFamily("JetBrains Mono,Cascadia Code,Fira Code,Consolas,monospace"),
      FontSize = 28,
      Foreground = new SolidColorBrush(Color.FromRgb(228, 228, 231)),
      TextWrapping = TextWrapping.Wrap,
      Padding = new Thickness(24),
      Margin = new Thickness(0, 0, 0, 24)
    };

    s_scrollViewer = new ScrollViewer
    {
      Content = s_textBlock,
      VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Visible,
      Padding = new Thickness(0),
      Background = new SolidColorBrush(Color.FromRgb(24, 24, 27))
    };

    var window = new Window
    {
      Title = "Debug Output",
      Width = 1200,
      Height = 800,
      Content = s_scrollViewer,
      Background = new SolidColorBrush(Color.FromRgb(24, 24, 27))
    };

    // Add Ctrl+C handler to close window
    window.KeyDown += (sender, e) =>
    {
      if (e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control)
      {
        window.Close();
      }
    };

    // Start connecting AFTER window is created
    window.Opened += (_, _) => Task.Run(() => ConnectToPipeAsync(pipeName));

    return window;
  }



  private static void OpenUrl(string url)
  {
    try
    {
      // Cross-platform URL opening
      if (OperatingSystem.IsWindows())
      {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
      }
      else if (OperatingSystem.IsLinux())
      {
        Process.Start("xdg-open", url);
      }
      else if (OperatingSystem.IsMacOS())
      {
        Process.Start("open", url);
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Failed to open URL {url}: {ex.Message}");
    }
  }

  private class App : Avalonia.Application
  {
    public override void Initialize() => Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
  }
}