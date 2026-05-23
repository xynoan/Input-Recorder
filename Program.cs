using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MouseInputRecorder;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new RecorderForm());
    }
}

internal sealed class RecorderForm : Form
{
    private readonly Button _recordButton = new() { Text = "Record", Width = 120, Height = 36 };
    private readonly Button _stopRecordButton = new() { Text = "Stop Recording", Width = 120, Height = 36, Enabled = false };
    private readonly Button _playButton = new() { Text = "Play", Width = 120, Height = 36, Enabled = false };
    private readonly Button _stopPlayButton = new() { Text = "Stop Playback", Width = 120, Height = 36, Enabled = false };
    private readonly CheckBox _loopCheckBox = new() { Text = "Enable loop playback", AutoSize = true };
    private readonly Label _loopCountLabel = new() { Text = "Number of times to play:", AutoSize = true, Enabled = false, Margin = new Padding(28, 10, 0, 0) };
    private readonly NumericUpDown _loopCountInput = new()
    {
        Minimum = 1,
        Maximum = 999,
        Value = 2,
        Width = 90,
        Enabled = false,
        Margin = new Padding(8, 6, 0, 0)
    };
    private readonly Label _statusLabel = new() { AutoSize = true, Text = "Ready. Click Record to capture mouse and keyboard input." };
    private readonly Label _countLabel = new() { AutoSize = true, Text = "Recorded events: 0" };

    private readonly InputRecorder _recorder = new();
    private CancellationTokenSource? _playbackCts;

    public RecorderForm()
    {
        Text = "Input Recorder";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(620, 250);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 52,
            Padding = new Padding(12, 12, 12, 4),
            WrapContents = false
        };
        controls.Controls.AddRange(new Control[] { _recordButton, _stopRecordButton, _playButton, _stopPlayButton });

        var playbackOptionsGroup = new GroupBox
        {
            Dock = DockStyle.Top,
            Height = 74,
            Text = "Playback repeat",
            Padding = new Padding(12, 10, 12, 10)
        };
        var playbackOptions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 8, 0, 0),
            WrapContents = false
        };
        _loopCheckBox.Margin = new Padding(0, 10, 0, 0);
        playbackOptions.Controls.AddRange(new Control[] { _loopCheckBox, _loopCountLabel, _loopCountInput });
        playbackOptionsGroup.Controls.Add(playbackOptions);

        var info = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(16, 10, 16, 16)
        };
        info.Controls.Add(_statusLabel);
        info.Controls.Add(_countLabel);
        info.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Tip: F8 stops recording or playback. Playback sends real mouse and keyboard input."
        });

        Controls.Add(info);
        Controls.Add(playbackOptionsGroup);
        Controls.Add(controls);

        _recordButton.Click += (_, _) => StartRecording();
        _stopRecordButton.Click += (_, _) => StopRecording();
        _playButton.Click += async (_, _) => await StartPlaybackAsync();
        _stopPlayButton.Click += (_, _) => StopPlayback();
        _loopCheckBox.CheckedChanged += (_, _) => SetLoopCountUi();
        FormClosing += (_, _) =>
        {
            StopPlayback();
            _recorder.Stop();
            NativeMethods.UnregisterHotKey(Handle, NativeMethods.StopHotKeyId);
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.RegisterHotKey(Handle, NativeMethods.StopHotKeyId, 0, Keys.F8);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WmHotKey && m.WParam.ToInt32() == NativeMethods.StopHotKeyId)
        {
            if (_recorder.IsRecording)
            {
                StopRecording();
            }
            else if (_playbackCts is not null)
            {
                StopPlayback();
            }
        }

        base.WndProc(ref m);
    }

    private void StartRecording()
    {
        StopPlayback();
        _recorder.Start();
        _recordButton.Enabled = false;
        _stopRecordButton.Enabled = true;
        _playButton.Enabled = false;
        _statusLabel.Text = "Recording mouse and keyboard input...";
        _countLabel.Text = "Recorded events: 0";
    }

    private void StopRecording()
    {
        _recorder.Stop();
        _recordButton.Enabled = true;
        _stopRecordButton.Enabled = false;
        _playButton.Enabled = _recorder.Events.Count > 0;
        _countLabel.Text = $"Recorded events: {_recorder.Events.Count}";
        _statusLabel.Text = _recorder.Events.Count == 0
            ? "No input was recorded."
            : "Recording saved in memory. Click Play to replay it.";
    }

    private async Task StartPlaybackAsync()
    {
        if (_recorder.Events.Count == 0 || _playbackCts is not null)
        {
            return;
        }

        var events = _recorder.Events.ToArray();
        _playbackCts = new CancellationTokenSource();
        SetPlaybackUi(isPlaying: true);
        var targetPlayCount = _loopCheckBox.Checked ? (int)_loopCountInput.Value : 1;
        _statusLabel.Text = targetPlayCount > 1 ? $"Playing 1 of {targetPlayCount}..." : "Playing...";

        try
        {
            for (var playNumber = 1; playNumber <= targetPlayCount; playNumber++)
            {
                await InputPlayer.PlayAsync(events, _playbackCts.Token);

                if (playNumber < targetPlayCount && !_playbackCts.IsCancellationRequested)
                {
                    _statusLabel.Text = $"Playing {playNumber + 1} of {targetPlayCount}...";
                }
            }

            _statusLabel.Text = "Playback complete.";
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Playback stopped.";
        }
        finally
        {
            _playbackCts?.Dispose();
            _playbackCts = null;
            SetPlaybackUi(isPlaying: false);
        }
    }

    private void StopPlayback()
    {
        _playbackCts?.Cancel();
    }

    private void SetPlaybackUi(bool isPlaying)
    {
        _recordButton.Enabled = !isPlaying;
        _stopRecordButton.Enabled = false;
        _playButton.Enabled = !isPlaying && _recorder.Events.Count > 0;
        _stopPlayButton.Enabled = isPlaying;
        _loopCheckBox.Enabled = !isPlaying;
        SetLoopCountUi();
    }

    private void SetLoopCountUi()
    {
        var enabled = _loopCheckBox.Checked && _loopCheckBox.Enabled;
        _loopCountLabel.Enabled = enabled;
        _loopCountInput.Enabled = enabled;
    }
}

internal sealed class InputRecorder
{
    private readonly List<InputEventRecord> _events = [];
    private NativeMethods.LowLevelMouseProc? _mouseHookProc;
    private NativeMethods.LowLevelKeyboardProc? _keyboardHookProc;
    private IntPtr _mouseHookHandle;
    private IntPtr _keyboardHookHandle;
    private Stopwatch _stopwatch = new();
    private long _lastEventMs;
    private Point _lastMovePoint;
    private long _lastMoveMs;

    public IReadOnlyList<InputEventRecord> Events => _events;
    public bool IsRecording => _mouseHookHandle != IntPtr.Zero || _keyboardHookHandle != IntPtr.Zero;

    public void Start()
    {
        Stop();
        _events.Clear();
        _lastEventMs = 0;
        _lastMoveMs = -1000;
        _lastMovePoint = Cursor.Position;
        _stopwatch = Stopwatch.StartNew();

        _mouseHookProc = MouseHookCallback;
        _keyboardHookProc = KeyboardHookCallback;

        _mouseHookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhMouseLl,
            _mouseHookProc,
            NativeMethods.GetModuleHandle(null),
            0);
        _keyboardHookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhKeyboardLl,
            _keyboardHookProc,
            NativeMethods.GetModuleHandle(null),
            0);

        if (_mouseHookHandle == IntPtr.Zero || _keyboardHookHandle == IntPtr.Zero)
        {
            Stop();
            throw new InvalidOperationException("Could not install the global input hooks.");
        }
    }

    public void Stop()
    {
        if (_mouseHookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = IntPtr.Zero;
        }

        if (_keyboardHookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = IntPtr.Zero;
        }
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<NativeMethods.MsllHookStruct>(lParam);
            var kind = MouseEventKindFromMessage(wParam.ToInt32(), data.MouseData);

            if (kind is not null && ShouldRecordMouse(kind.Value, data.Point))
            {
                AddEvent(new InputEventRecord(
                    InputEventType.Mouse,
                    kind.Value,
                    KeyboardEventKind.None,
                    data.Point.X,
                    data.Point.Y,
                    unchecked((short)((data.MouseData >> 16) & 0xffff)),
                    0,
                    0,
                    0));

                if (kind.Value == MouseEventKind.Move)
                {
                    _lastMovePoint = new Point(data.Point.X, data.Point.Y);
                    _lastMoveMs = _stopwatch.ElapsedMilliseconds;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            var kind = KeyboardEventKindFromMessage(message);
            var data = Marshal.PtrToStructure<NativeMethods.KbdllHookStruct>(lParam);

            if (kind is not null && data.VirtualKeyCode != (int)Keys.F8)
            {
                AddEvent(new InputEventRecord(
                    InputEventType.Keyboard,
                    MouseEventKind.None,
                    kind.Value,
                    0,
                    0,
                    0,
                    data.VirtualKeyCode,
                    data.ScanCode,
                    data.Flags));
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    private void AddEvent(InputEventRecord eventRecord)
    {
        var now = _stopwatch.ElapsedMilliseconds;
        _events.Add(eventRecord with { DelayMs = Math.Max(0, (int)(now - _lastEventMs)) });
        _lastEventMs = now;
    }

    private bool ShouldRecordMouse(MouseEventKind kind, NativeMethods.Point point)
    {
        if (kind != MouseEventKind.Move)
        {
            return true;
        }

        var elapsed = _stopwatch.ElapsedMilliseconds - _lastMoveMs;
        var dx = Math.Abs(point.X - _lastMovePoint.X);
        var dy = Math.Abs(point.Y - _lastMovePoint.Y);
        return elapsed >= 10 && (dx >= 2 || dy >= 2);
    }

    private static MouseEventKind? MouseEventKindFromMessage(int message, int mouseData)
    {
        return message switch
        {
            NativeMethods.WmMouseMove => MouseEventKind.Move,
            NativeMethods.WmLButtonDown => MouseEventKind.LeftDown,
            NativeMethods.WmLButtonUp => MouseEventKind.LeftUp,
            NativeMethods.WmRButtonDown => MouseEventKind.RightDown,
            NativeMethods.WmRButtonUp => MouseEventKind.RightUp,
            NativeMethods.WmMButtonDown => MouseEventKind.MiddleDown,
            NativeMethods.WmMButtonUp => MouseEventKind.MiddleUp,
            NativeMethods.WmMouseWheel when mouseData != 0 => MouseEventKind.Wheel,
            _ => null
        };
    }

    private static KeyboardEventKind? KeyboardEventKindFromMessage(int message)
    {
        return message switch
        {
            NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown => KeyboardEventKind.Down,
            NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp => KeyboardEventKind.Up,
            _ => null
        };
    }
}

internal static class InputPlayer
{
    public static async Task PlayAsync(IReadOnlyList<InputEventRecord> events, CancellationToken cancellationToken)
    {
        foreach (var recordedEvent in events)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (recordedEvent.DelayMs > 0)
            {
                await Task.Delay(recordedEvent.DelayMs, cancellationToken);
            }

            if (recordedEvent.Type == InputEventType.Mouse)
            {
                NativeMethods.SetCursorPos(recordedEvent.X, recordedEvent.Y);

                if (recordedEvent.MouseKind != MouseEventKind.Move)
                {
                    NativeMethods.SendMouseInput(recordedEvent.MouseKind, recordedEvent.WheelDelta);
                }
            }
            else if (recordedEvent.Type == InputEventType.Keyboard)
            {
                NativeMethods.SendKeyboardInput(
                    recordedEvent.KeyboardKind,
                    (ushort)recordedEvent.VirtualKeyCode,
                    (ushort)recordedEvent.ScanCode,
                    recordedEvent.KeyboardFlags);
            }
        }
    }
}

internal readonly record struct InputEventRecord(
    InputEventType Type,
    MouseEventKind MouseKind,
    KeyboardEventKind KeyboardKind,
    int X,
    int Y,
    short WheelDelta,
    int VirtualKeyCode,
    int ScanCode,
    int KeyboardFlags)
{
    public int DelayMs { get; init; }
}

internal enum InputEventType
{
    Mouse,
    Keyboard
}

internal enum MouseEventKind
{
    None,
    Move,
    LeftDown,
    LeftUp,
    RightDown,
    RightUp,
    MiddleDown,
    MiddleUp,
    Wheel
}

internal enum KeyboardEventKind
{
    None,
    Down,
    Up
}

internal static partial class NativeMethods
{
    public const int WhKeyboardLl = 13;
    public const int WhMouseLl = 14;
    public const int WmHotKey = 0x0312;
    public const int StopHotKeyId = 0x4d51;

    public const int WmKeyDown = 0x0100;
    public const int WmKeyUp = 0x0101;
    public const int WmSysKeyDown = 0x0104;
    public const int WmSysKeyUp = 0x0105;
    public const int WmMouseMove = 0x0200;
    public const int WmLButtonDown = 0x0201;
    public const int WmLButtonUp = 0x0202;
    public const int WmRButtonDown = 0x0204;
    public const int WmRButtonUp = 0x0205;
    public const int WmMButtonDown = 0x0207;
    public const int WmMButtonUp = 0x0208;
    public const int WmMouseWheel = 0x020A;

    private const int InputMouse = 0;
    private const int InputKeyboard = 1;
    private const int KeyboardLlInjected = 0x10;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfExtendedkey = 0x0001;
    private const uint MouseeventfLeftdown = 0x0002;
    private const uint MouseeventfLeftup = 0x0004;
    private const uint MouseeventfRightdown = 0x0008;
    private const uint MouseeventfRightup = 0x0010;
    private const uint MouseeventfMiddledown = 0x0020;
    private const uint MouseeventfMiddleup = 0x0040;
    private const uint MouseeventfWheel = 0x0800;

    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW")]
    public static partial IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW")]
    public static partial IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWindowsHookEx(IntPtr hhk);

    [LibraryImport("user32.dll")]
    public static partial IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr GetModuleHandle(string? lpModuleName);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, Keys vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    public static void SendMouseInput(MouseEventKind kind, short wheelDelta)
    {
        var input = new INPUT
        {
            Type = InputMouse,
            Union = new InputUnion
            {
                MouseInput = new MOUSEINPUT
                {
                    MouseData = kind == MouseEventKind.Wheel ? wheelDelta : 0,
                    DwFlags = MouseFlags(kind)
                }
            }
        };

        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    public static void SendKeyboardInput(KeyboardEventKind kind, ushort virtualKeyCode, ushort scanCode, int recordedFlags)
    {
        var flags = kind == KeyboardEventKind.Up ? KeyeventfKeyup : 0;

        if ((recordedFlags & 0x01) != 0)
        {
            flags |= KeyeventfExtendedkey;
        }

        var input = new INPUT
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                KeyboardInput = new KEYBDINPUT
                {
                    VirtualKeyCode = virtualKeyCode,
                    ScanCode = scanCode,
                    DwFlags = flags
                }
            }
        };

        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    private static uint MouseFlags(MouseEventKind kind)
    {
        return kind switch
        {
            MouseEventKind.LeftDown => MouseeventfLeftdown,
            MouseEventKind.LeftUp => MouseeventfLeftup,
            MouseEventKind.RightDown => MouseeventfRightdown,
            MouseEventKind.RightUp => MouseeventfRightup,
            MouseEventKind.MiddleDown => MouseeventfMiddledown,
            MouseEventKind.MiddleUp => MouseeventfMiddleup,
            MouseEventKind.Wheel => MouseeventfWheel,
            _ => 0
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MsllHookStruct
    {
        public Point Point;
        public int MouseData;
        public int Flags;
        public int Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KbdllHookStruct
    {
        public int VirtualKeyCode;
        public int ScanCode;
        public int Flags;
        public int Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT MouseInput;

        [FieldOffset(0)]
        public KEYBDINPUT KeyboardInput;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int Dx;
        public int Dy;
        public int MouseData;
        public uint DwFlags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKeyCode;
        public ushort ScanCode;
        public uint DwFlags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }
}
