using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Forms;
using DesktopPet.Interop;

namespace DesktopPet.App;

/// <summary>
/// The pet's speech bubble. Only its acknowledgement chrome and fallback title are localized;
/// notification titles and messages remain exactly as supplied by their sender.
/// </summary>
public partial class SpeechBubbleWindow : Window
{
    private IntPtr _hwnd;
    private double _dpiScale = 1.0;
    private int _pendingCount;
    private bool _usesFallbackTitle = true;
    private bool _closed;
    private bool _reducedMotion;

    /// <summary>Raised when the user clicks the bubble to acknowledge it.</summary>
    public event Action? Acknowledged;

    public bool IsShowing { get; private set; }

    public SpeechBubbleWindow(Func<string, bool>? copyText = null)
    {
        _copyText = copyText ?? TryCopyNotification;
        InitializeComponent();
        PetWindowBehavior.Attach(this);
        UiText.LanguageChanged += OnLanguageChanged;
        Closed += OnClosed;
        RefreshLocalizedChrome();

        Left = -10000;
        Top = -10000;
        try
        {
            Show();
            Hide();
        }
        catch
        {
            UiText.LanguageChanged -= OnLanguageChanged;
            Closed -= OnClosed;
            throw;
        }

        _hwnd = new WindowInteropHelper(this).Handle;
        MouseLeftButtonDown += (_, _) => Acknowledge();
    }

    public void ShowMessage(string title, string message, double dpiScale, bool gentle = true, string? source = null)
    {
        if (_closed) return;
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        _dpiScale = dpiScale;
        _usesFallbackTitle = string.IsNullOrWhiteSpace(title);
        TitleText.Text = _usesFallbackTitle ? UiText.Get("알림") : title;
        MessageText.Text = message ?? string.Empty;
        MessageText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        SetNotificationSource(source);
        _copyLabelKey = "복사";
        RefreshLocalizedChrome();

        IsShowing = true;
        Show();
        UpdateLayout();
        if (!_reducedMotion)
        {
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }
    }

    private string? _sourceKey;

    private void SetNotificationSource(string? source)
    {
        if (_closed) return;
        var normalized = source?.Trim().ToLowerInvariant();
        _sourceKey = normalized switch
        {
            "claude" => "claude",
            "codex" => "codex",
            "focus-timer" => "focus-timer",
            "test" => "test",
            _ => null
        };
        RefreshSourceBadge();
    }

    private void RefreshSourceBadge()
    {
        if (_closed) return;
        if (SourceText == null) return;

        string displayText = _sourceKey switch
        {
            "claude" => "Claude",
            "codex" => "Codex",
            "focus-timer" => UiText.Get("집중 타이머"),
            "test" => UiText.Get("테스트 알림"),
            _ => string.Empty
        };

        SourceText.Text = displayText;
        SourceText.Visibility = string.IsNullOrEmpty(displayText) ||
            string.Equals(displayText, TitleText.Text.Trim(), StringComparison.OrdinalIgnoreCase)
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private readonly Func<string, bool> _copyText;
    private string _copyLabelKey = "복사";

    private static bool TryCopyNotification(string text)
    {
        System.Windows.Clipboard.SetText(text);
        return true;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_closed || !IsShowing) return;

        string text = TitleText.Text + (MessageText.Text.Length > 0 ? Environment.NewLine + MessageText.Text : "");
        bool copied = false;

        try
        {
            copied = _copyText(text);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            copied = false;
        }

        _copyLabelKey = copied ? "복사했어요" : "다시 복사";
        RefreshLocalizedChrome();
    }

    public void SetPendingCount(int count)
    {
        if (_closed) return;
        _pendingCount = Math.Max(0, count);
        RefreshLocalizedChrome();
    }

    private void OnLanguageChanged()
    {
        if (_closed) return;
        if (Dispatcher.CheckAccess()) RefreshLocalizedChrome();
        else if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(RefreshLocalizedChrome);
    }

    private void RefreshLocalizedChrome()
    {
        if (_closed) return;
        AcknowledgementText.Text = _pendingCount > 0
            ? UiText.Format("클릭해서 확인 · 대기 {0}건", _pendingCount)
            : UiText.Get("클릭해서 확인");
        if (_usesFallbackTitle) TitleText.Text = UiText.Get("알림");
        RefreshSourceBadge();
        CopyButton.Content = UiText.Get(_copyLabelKey);
        CopyButton.ToolTip = UiText.Get("알림 내용 복사");
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        IsShowing = false;
        UiText.LanguageChanged -= OnLanguageChanged;
        Closed -= OnClosed;
    }

    /// <summary>Keeps the bubble parked above the pet as it moves around.</summary>
    public void FollowPet(double petLeft, double petTop, double petWidth)
    {
        if (!IsShowing)
        {
            return;
        }

        var left = petLeft + petWidth / 2 - ActualWidth / 2;
        var top = petTop - ActualHeight + 6;

        // Keep it on screen — a bubble half off the top edge is unreadable.
        var work = Screen.FromPoint(new System.Drawing.Point(
            (int)Math.Round((petLeft + petWidth / 2) * _dpiScale),
            (int)Math.Round(petTop * _dpiScale))).WorkingArea;
        var workLeft = work.Left / _dpiScale;
        var workTop = work.Top / _dpiScale;
        var workRight = work.Right / _dpiScale;
        var workBottom = work.Bottom / _dpiScale;
        left = Math.Clamp(left, workLeft + 4, Math.Max(workLeft + 4, workRight - ActualWidth - 4));
        top = Math.Clamp(top, workTop + 4, Math.Max(workTop + 4, workBottom - ActualHeight - 4));

        NativeMethods.SetWindowPos(
            _hwnd, NativeMethods.HWND_TOPMOST,
            (int)Math.Round(left * _dpiScale), (int)Math.Round(top * _dpiScale), 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private void Acknowledge()
    {
        if (!IsShowing)
        {
            return;
        }

        IsShowing = false;
        Hide();
        Acknowledged?.Invoke();
    }

    /// <summary>Closes the bubble without re-raising <see cref="Acknowledged"/> (the caller already acted).</summary>
    public void Dismiss()
    {
        if (_closed) return;
        BeginAnimation(OpacityProperty, null);
        IsShowing = false;
        Hide();
    }

    public void DismissSoftly()
    {
        if (!IsShowing) return;
        IsShowing = false;
        if (_reducedMotion)
        {
            BeginAnimation(OpacityProperty, null);
            Hide();
            return;
        }
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) => { if (!IsShowing) Hide(); };
        BeginAnimation(OpacityProperty, fade);
    }

    public void Shutdown()
    {
        if (_closed) return;
        BeginAnimation(OpacityProperty, null);
        IsShowing = false;
        Close();
    }

    public void SetReducedMotion(bool reducedMotion)
    {
        if (_closed || _reducedMotion == reducedMotion) return;
        _reducedMotion = reducedMotion;
        if (_reducedMotion)
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            if (!IsShowing)
            {
                Hide();
            }
        }
    }
}
